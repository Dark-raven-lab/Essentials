using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Sandbox.Game.World;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;

namespace Essentials.Commands
{
    public class VotingModule : CommandModule
    {
        public enum Status
        {
            voteStandby,
            voteInProgress,
            voteCancel,

            // Last vote
            voteFail,
            voteSuccess
        }

        private static readonly Logger Log = LogManager.GetLogger("Essentials Voting");
        private static AutoCommand _command;
        private static string voteInProgress;
        private static readonly HashSet<ulong> _voteReg = new HashSet<ulong>();
        private static readonly HashSet<ulong> _voteAgainst = new HashSet<ulong>();
        private static readonly Dictionary<ulong, DateTime> _voteCooldown = new Dictionary<ulong, DateTime>();
        public static Status VoteStatus = Status.voteStandby;

        //last vote info for debugging
        private static string lastVoteName;
        private static Status voteResult;
        private static double voteResultPercentage;

        [Command("vote", "starts a vote for a command")]
        [Permission(MyPromoteLevel.None)]
        public void Vote(string name)
        {
            if (Context.Player == null)
            {
                Context.Respond("Команда доступна только в игре");
                return;
            }

            //a cancelled vote still has its countdown running until the next second
            if (VoteStatus == Status.voteCancel)
            {
                Context.Respond($"Голосование {voteInProgress} отменяется, повторите через пару секунд");
                return;
            }

            if (VoteStatus == Status.voteInProgress)
            {
                Context.Respond(
                    $"Сейчас идёт голосование {voteInProgress}. Введите команды !yes или !no для учёта вашего голоса.");
                return;
            }

            _command = EssentialsPlugin.Instance.Config.AutoCommands.FirstOrDefault(c => !string.IsNullOrEmpty(c.Name) 
                && c.CommandTrigger == Trigger.Vote && c.Name.Equals(name));

            if (_command == null)
            {
                Context.Respond($"Голосование {name} не найдено");
                return;
            }


            var steamid = Context.Player.SteamUserId;
            if (_voteCooldown.TryGetValue(steamid, out var activeCooldown))
            {
                var difference = activeCooldown - DateTime.Now;
                if (difference.TotalSeconds > 0)
                {
                    Context.Respond(
                        $"Перезарядка: команду можно использовать снова через {difference.Minutes:N0} мин. {difference.Seconds:N0} сек.");
                    return;
                }

                _voteCooldown[steamid] = DateTime.Now.AddSeconds(_command.TriggerCount);
            }

            else
            {
                _voteCooldown.Add(steamid, DateTime.Now.AddSeconds(_command.TriggerCount));
            }

            var voteDuration = TimeSpan.Parse(_command.Interval);
            // voting status
            voteInProgress = name;
            VoteStatus = Status.voteInProgress;
            Log.Info($"Vote {name} started by {Context.Player.DisplayName} ({steamid})");
            var announcement = $"Началось голосование {name}. Введите команды !yes или !no для учёта вашего голоса.";
            SendToChat(announcement);
            ModCommunication.SendMessageToClients(new NotificationMessage(announcement, 15000, "Blue"));
            VoteYes();
            //vote countdown
            Task.Run(() =>
            {
                //without this a failed countdown would block all the next votes
                try
                {
                    var countdown = VoteCountdown(voteDuration).GetEnumerator();
                    while (countdown.MoveNext()) Thread.Sleep(1000);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Vote countdown failed");
                    VoteEnd();
                }
            });

            lastVoteName = voteInProgress;
        }

        [Command("vote cancel", "Cancels current vote in progress")]
        [Permission(MyPromoteLevel.Admin)]
        public void VoteCancel()
        {
            if (VoteStatus == Status.voteInProgress)
                VoteStatus = Status.voteCancel;
            else
                Context.Respond("Нет активного голосования");
        }

        [Command("vote list", "Lists all possible vote commands")]
        [Permission(MyPromoteLevel.None)]
        public void VoteList()
        {
            StringBuilder sb = new StringBuilder();
            var voteCommands = new List<AutoCommand>(EssentialsPlugin.Instance.Config.AutoCommands.Where(x=>x.CommandTrigger == Trigger.Vote));
            if (Context.Player == null)sb.AppendLine($"Найдено: {voteCommands.Count}");
            var c = 1;
            foreach (var command in voteCommands)
            {
                if (string.IsNullOrEmpty(command.Name)) continue;
                sb.AppendLine($"{c}. {command.Name}");
                c++;
            }

            if (Context.Player == null)
            {
                Context.Respond(sb.ToString());
                return;
            }
            ModCommunication.SendMessageTo(new DialogMessage("Голосования", $"Найдено: {voteCommands.Count}", sb.ToString()),Context.Player.SteamUserId);
        }

        [Command("no", "Submit a no vote")]
        [Permission(MyPromoteLevel.None)]
        public void VoteNo()
        {
            if (Context.Player == null)
                return;

            if (VoteStatus != Status.voteInProgress)
            {
                Context.Respond("Нет активного голосования");
                return;
            }

            var steamid = Context.Player.SteamUserId;

            if (!_voteAgainst.Add(steamid))
            {
                Context.Respond("Ваш голос против уже учтён");
                return;
            }

            _voteReg.Remove(steamid);
            Context.Respond("Ваш голос против учтён");
            SendToChat($"Голосование {voteInProgress}: {GetProgress()}");
        }

        [Command("yes", "Submit a yes vote")]
        [Permission(MyPromoteLevel.None)]
        public void VoteYes()
        {
            if (Context.Player == null)
                return;

            if (VoteStatus != Status.voteInProgress)
            {
                Context.Respond("Нет активного голосования");
                return;
            }

            var steamid = Context.Player.SteamUserId;
            if (!_voteReg.Add(steamid))
            {
                Context.Respond("Ваш голос за уже учтён");
                return;
            }

            _voteAgainst.Remove(steamid);
            Context.Respond("Ваш голос за учтён");
            SendToChat($"Голосование {voteInProgress}: {GetProgress()}");
        }

        //debug
        [Command("vote debug", "prints out info from the voting module")]
        [Permission(MyPromoteLevel.Admin)]
        public void VoteDebgug()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"Текущий статус: {VoteStatus.ToString()}");
            sb.AppendLine($"Текущее голосование: {voteInProgress}");
            sb.AppendLine($"Голосов за: {_voteReg.Count}");
            sb.AppendLine($"Голосов против: {_voteAgainst.Count}");
            sb.AppendLine();
            sb.AppendLine("Последнее голосование");
            if (lastVoteName != null)
                sb.AppendLine($"Название: {lastVoteName}");
            sb.AppendLine($"Результат: {voteResult.ToString()}");
            sb.AppendLine($"Процент: {voteResultPercentage}");
            if (Context.Player == null)
                Context.Respond(sb.ToString());
            else if (Context?.Player?.SteamUserId > 0)
                ModCommunication.SendMessageTo(new DialogMessage("Отладка голосования", null, sb.ToString()),
                    Context.Player.SteamUserId);
        }

        //vote reset
        [Command("vote reset", "Resets the voting module data including cooldowns")]
        [Permission(MyPromoteLevel.Admin)]
        public void VoteReset()
        {
            if (VoteStatus == Status.voteInProgress) VoteCancel();
            _voteReg.Clear();
            _voteAgainst.Clear();
            _voteCooldown.Clear();
            lastVoteName = null;
            voteResult = Status.voteStandby;
            voteResultPercentage = 0;
            Context.Respond("Данные голосования сброшены");
            Log.Info($"Voting module reset by {Context.Player?.DisplayName ?? "console"}");
        }


        //vote countdown
        private IEnumerable VoteCountdown(TimeSpan time)
        {
            //whole seconds, the last step has to reach 0 for the vote to end
            for (var i = Math.Ceiling(time.TotalSeconds); i >= 0; i--)
            {
                if (VoteStatus != Status.voteInProgress || _voteReg.Count + _voteAgainst.Count < 1 || Utilities.GetOnlinePlayerCount() < 1)
                {
                    SendToChat($"Голосование {voteInProgress} отменено");
                    voteResult = Status.voteCancel;
                    VoteEnd();
                    yield break;
                }

                if (i >= 60 && i % 60 == 0)
                {
                    SendToChat($"Голосование {voteInProgress} завершится через {i / 60} мин.: {GetProgress()}");
                    yield return null;
                }

                else if (i > 0)
                {
                    if (i < 11)
                        SendToChat($"Голосование {voteInProgress} завершится через {i} сек.");
                    yield return null;
                }
                else
                {
                    var votes = _voteReg.Count;
                    var online = Utilities.GetOnlinePlayerCount();
                    var summary = $"за {votes}, против {_voteAgainst.Count}, онлайн {online}, нужно {GetRequiredVotes(online)}";
                    double vr = (double)votes / online;
                    if (IsPassed(votes, online))
                    {
                        SendToChat($"Голосование {voteInProgress} прошло: {summary}");
                        voteResult = Status.voteSuccess;
                        _command.RunNow();
                    }
                    else
                    {
                        SendToChat($"Голосование {voteInProgress} не прошло: {summary}");
                        voteResult = Status.voteFail;
                    }

                    voteResultPercentage = vr * 100;
                    VoteEnd();
                    yield break;
                }
            }
        }


        public void VoteEnd()
        {
            //Make sure it's all good for next round
            _command = null;
            voteInProgress = null;
            _voteReg.Clear();
            _voteAgainst.Clear();
            //last: from here on a new vote can start
            VoteStatus = Status.voteStandby;
        }

        private void SendToChat(string message)
        {
            Context.Torch.CurrentSession.Managers.GetManager<IChatManagerClient>().SendMessageAsSelf(message);
        }

        private static string GetProgress()
        {
            var online = Utilities.GetOnlinePlayerCount();
            var required = GetRequiredVotes(online);
            return $"{_voteReg.Count} из {required} {(required % 10 == 1 && required % 100 != 11 ? "нужного голоса" : "нужных голосов")}, против {_voteAgainst.Count} (онлайн {online})";
        }

        private static bool IsPassed(int votes, int online)
        {
            return (double)votes / online >= _command.TriggerRatio && votes > _voteAgainst.Count;
        }

        private static int GetRequiredVotes(int online)
        {
            var votes = 1;
            while (!IsPassed(votes, online))
                votes++;
            return votes;
        }
    }
}
