using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace Essentials.Commands
{
    [Category("poll")]
    public class PollModule : CommandModule
    {
        private const int MaxDays = 365;

        private static PollStorage Polls => EssentialsPlugin.Instance.Polls;

        [Command("create", "Создаёт опрос «за/против» на указанное число дней.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Create(int days, string question)
        {
            if (days < 1 || days > MaxDays)
            {
                Context.Respond($"Число дней должно быть от 1 до {MaxDays}.");
                return;
            }

            //the parameter only shows the question in the syntax help: Torch gives it the first word,
            //the question is the rest of the line, so it needs no quotes
            question = Context.RawArgs.Trim();
            var space = question.IndexOf(' ');
            question = space < 0 ? string.Empty : question.Substring(space + 1).Trim().Trim('"').Trim();
            if (question.Length == 0)
            {
                Context.Respond("Укажите вопрос: !poll create <дней> <вопрос>");
                return;
            }

            var poll = Polls.Create(question, TimeSpan.FromDays(days));
            Context.Respond($"Опрос #{poll.Id} создан.");
        }

        [Command("list", "Показывает опросы: все, active — идущие, finished — завершённые.")]
        [Permission(MyPromoteLevel.None)]
        public void List(string filter = null)
        {
            var polls = Polls.Active.Concat(Polls.Finished);
            var title = "Опросы";
            switch (filter?.ToLowerInvariant())
            {
                case null:
                    break;
                case "active":
                    polls = Polls.Active;
                    title = "Идущие опросы";
                    break;
                case "finished":
                    polls = Polls.Finished;
                    title = "Завершённые опросы";
                    break;
                default:
                    Context.Respond("Укажите active, finished или ничего.");
                    return;
            }

            var list = polls.ToList();
            if (list.Count == 0)
            {
                Context.Respond($"{title}: нет.");
                return;
            }

            Context.Respond($"{title}:\n{string.Join("\n", list.Select(PollStorage.Describe))}");
        }

        [Command("show", "Показывает вопрос, срок и результат опроса. Админ видит, кто как голосовал.")]
        [Permission(MyPromoteLevel.None)]
        public void Show(int id)
        {
            if (!TryGetPoll(id, out var poll))
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"Опрос #{poll.Id}: {poll.Question}");
            sb.AppendLine($"Начат: {poll.StartUtc.ToLocalTime():dd.MM.yyyy HH:mm}");
            sb.AppendLine(poll.Finished
                ? $"Завершён: {poll.EndUtc.ToLocalTime():dd.MM.yyyy HH:mm}"
                : $"Завершится: {poll.EndUtc.ToLocalTime():dd.MM.yyyy HH:mm} (осталось {PollStorage.FormatRemaining(poll)})");
            sb.Append($"За: {poll.Yes}, против: {poll.No}");

            if (Context.Player != null && poll.Votes.TryGetValue(Context.Player.SteamUserId, out var own))
                sb.Append($"\nВаш голос: {PollStorage.AnswerText(own)}");

            //anonymous for players, an admin sees the voters to notice alt accounts
            if (Context.Player == null || Context.Player.PromoteLevel >= MyPromoteLevel.Admin)
                foreach (var vote in poll.Votes.OrderByDescending(v => v.Value))
                    sb.Append($"\n  {PollStorage.AnswerText(vote.Value)}: {GetName(vote.Key)}");

            Context.Respond(sb.ToString());
        }

        [Command("yes", "Голос «за» в опросе с указанным номером.")]
        [Permission(MyPromoteLevel.None)]
        public void Yes(int id) => Vote(id, true);

        [Command("no", "Голос «против» в опросе с указанным номером.")]
        [Permission(MyPromoteLevel.None)]
        public void No(int id) => Vote(id, false);

        [Command("close", "Досрочно завершает опрос, результат сохраняется.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Close(int id)
        {
            if (!TryGetPoll(id, out var poll))
                return;

            if (poll.Finished)
            {
                Context.Respond($"Опрос #{id} уже завершён.");
                return;
            }

            Polls.Finish(poll);
            Context.Respond($"Опрос #{id} завершён.");
        }

        [Command("delete", "Удаляет опрос вместе с результатом.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Delete(int id)
        {
            Context.Respond(Polls.Delete(id) ? $"Опрос #{id} удалён." : $"Опроса #{id} нет.");
        }

        [Command("announce", "Пишет идущие опросы в общий чат. Если опросов нет, молчит.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Announce()
        {
            //an AutoCommand runs as the server, its answer only goes to the log
            if (Polls.AnnounceActive() == 0 && Context.Player != null)
                Context.Respond("Идущих опросов нет.");
        }

        [Command("help", "Показывает команды опросов.")]
        [Permission(MyPromoteLevel.None)]
        public void Help()
        {
            var commands = EssentialsPlugin.Instance.CommandManager.Commands;
            commands.GetNode(new List<string> { "poll" }, out var poll);

            //a player sees only the commands their level allows, the admin ones stay hidden
            var sb = new StringBuilder("Команды опросов:");
            foreach (var command in commands.WalkTree(poll).Where(n => n.IsCommand).Select(n => n.Command)
                         .Where(c => Context.Player == null || c.MinimumPromoteLevel <= Context.Player.PromoteLevel)
                         .OrderBy(c => c.SyntaxHelp))
                sb.Append($"\n{command.SyntaxHelp}\n    {command.HelpText}");

            Context.Respond(sb.ToString());
        }

        private void Vote(int id, bool yes)
        {
            if (Context.Player == null)
            {
                Context.Respond("Команда доступна только игрокам.");
                return;
            }

            if (!TryGetPoll(id, out var poll))
                return;

            if (poll.Finished)
            {
                Context.Respond($"Опрос #{id} уже завершён, голосовать нельзя.");
                return;
            }

            var answer = PollStorage.AnswerText(yes);
            var previous = Polls.SetVote(poll, Context.Player.SteamUserId, yes);
            if (previous == null)
                Context.Respond($"Ваш голос «{answer}» в опросе #{id} учтён.");
            else if (previous == yes)
                Context.Respond($"Вы уже голосовали «{answer}» в опросе #{id}.");
            else
                Context.Respond($"Ваш голос в опросе #{id} изменён на «{answer}».");
        }

        private bool TryGetPoll(int id, out PollStorage.Poll poll)
        {
            poll = Polls.Get(id);
            if (poll == null)
                Context.Respond($"Опроса #{id} нет.");
            return poll != null;
        }

        private static string GetName(ulong steamId)
        {
            var identityId = MySession.Static.Players.TryGetIdentityId(steamId);
            return MySession.Static.Players.TryGetIdentity(identityId)?.DisplayName ?? steamId.ToString();
        }
    }
}
