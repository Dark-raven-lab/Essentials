using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.World;
using Torch;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Managers.ChatManager;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;
using VRageRender.Utils;

namespace Essentials.Commands
{
    [Category("admin")]
    public class AdminModule : CommandModule
    {
        [Command("stats", "Показывает статистику производительности сервера")]
        [Permission(MyPromoteLevel.Admin)]
        public void Statistics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Общее:");
            Stats.Generic.WriteTo(sb);
            sb.AppendLine("Сеть:");
            Stats.Network.WriteTo(sb);
            sb.AppendLine("Время:");
            Stats.Timing.WriteTo(sb);

            if (Context?.Player?.SteamUserId > 0)
                ModCommunication.SendMessageTo(new DialogMessage("Статистика", null, sb.ToString()) , Context.Player.SteamUserId);
            else
                Context.Respond(sb.ToString());
        }

        [Command("playercount", "Показывает или задаёт максимальное число игроков на сервере")]
        [Permission(MyPromoteLevel.Admin)]
        public void PlayerCount(int count = -1)
        {
            if (count == -1)
            {
                Context.Respond($"Максимум игроков: {MyMultiplayer.Static.MemberLimit}. Сейчас онлайн: {MyMultiplayer.Static.MemberCount - 1}");
                return;
            }

            MyMultiplayer.Static.MemberLimit = count;
            Context.Respond($"Максимум игроков: {MyMultiplayer.Static.MemberLimit}. Сейчас онлайн: {MyMultiplayer.Static.MemberCount - 1}");
        }

        [Command("playerlist", "Показывает список игроков на сервере")]
        [Permission(MyPromoteLevel.Admin)]
        public void ListPlayers()
        {
            if(MySession.Static.Players.GetOnlinePlayerCount() == 0)
            {
                Context.Respond("Нет игроков онлайн");
                return;
            }
            StringBuilder sb = new StringBuilder();
            var players = MySession.Static.Players.GetOnlinePlayers();
            if (players.Count == 0)
            {
                Context.Respond("Нет игроков онлайн");
                return;
            }

            sb.AppendLine($"Игроков на сервере: {players.Count}");
            foreach(var player in players)
            {
                sb.AppendLine($"{player.DisplayName}");
                sb.AppendLine($">ID личности: {player.Identity.IdentityId}");
                sb.AppendLine($">SteamId: {player.Id.SteamId}");
            }
            if (Context.Player == null)
                Context.Respond(sb.ToString());
            else if (Context?.Player?.SteamUserId > 0)
            {
                ModCommunication.SendMessageTo(new DialogMessage("Игроки онлайн", $"Онлайн: {players.Count}", sb.ToString()), Context.Player.SteamUserId);
            }
        }


        [Command("runauto", "Сразу запускает автокоманду с указанным именем")]
        [Permission(MyPromoteLevel.Admin)]
        public void RunAuto(string name)
        {
            var command = EssentialsPlugin.Instance.Config.AutoCommands.FirstOrDefault(c => c.Name.Equals(name));
            if (command == null)
            {
                Context.Respond($"Автокоманда с именем {name} не найдена");
                return;
            }
            Context.Respond($"Запуск автокоманды {command.Name}");
            command.RunNow();
        }

        [Command("cancelauto", "Сразу отменяет автокоманду с указанным именем")]
        [Permission(MyPromoteLevel.Admin)]
        public void EndAuto(string commandName)
        {
            var command = EssentialsPlugin.Instance.Config.AutoCommands.FirstOrDefault(c => c.Name.Equals(commandName));
            if (command == null)
            {
                Context.Respond($"Автокоманда с именем {commandName} не найдена");
                return;
            }
            Context.Respond($"Автокоманда {command.Name} отменена");
            command.Cancel();
        }

        [Command("cancelautobyindex", "Сразу отменяет автокоманду с указанным номером")]
        [Permission(MyPromoteLevel.Admin)]
        public void EndByOrder(int commandNumber = 0)
        {
            var commands = new List<AutoCommand>(EssentialsPlugin.Instance.Config.AutoCommands.Where(x=>x.IsRunning()));

            if (commands.Count == 0)
            {
                Context.Respond("Нет активных автокоманд");
                return;
            }

            if (commandNumber < 1 || commandNumber > commands.Count)
            {
                Context.Respond($"Номер {commandNumber} вне допустимого диапазона");
                return;
            }
            var command = commands[commandNumber - 1];
            if (command == null)
            {
                Context.Respond($"Автокоманда с таким номером не найдена");
                return;
            }
            Context.Respond($"Автокоманда {command.Name} отменена");
            command.Cancel();
        }

        [Command("listauto", "Показывает все найденные автокоманды")]
        [Permission(MyPromoteLevel.Admin)]
        public void ListAuto()
        {
            var commands = EssentialsPlugin.Instance.Config.AutoCommands;
            StringBuilder sb = new StringBuilder();
            int count = 1;

            foreach (var command in commands)
            {
                sb.AppendLine((count++) + " " + command.Name);
            }

            if (Context.Player == null)
            {
                Context.Respond("Текущие автокоманды:");
                Context.Respond(sb.ToString());

            }

            else
            {
                ModCommunication.SendMessageTo(new DialogMessage("Текущие автокоманды",$"Найдено команд: {commands.Count}",sb.ToString()), Context.Player.SteamUserId);
            }
        }

        [Command("listrunningauto", "Показывает все запущенные автокоманды")]
        [Permission(MyPromoteLevel.Admin)]
        public void ListAutoRunning()
        {
            var commands = new List<AutoCommand>(EssentialsPlugin.Instance.Config.AutoCommands.Where(x=>x.IsRunning()));

            if (commands.Count == 0)
            {
                Context.Respond("Нет активных автокоманд");
                return;
            }
            StringBuilder sb = new StringBuilder();
            int count = 1;

            foreach (var command in commands)
            {
                sb.AppendLine((count++) + " " + command.Name);
            }

            if (Context.Player == null)
            {
                Context.Respond("Текущие автокоманды:");
                Context.Respond(sb.ToString());

            }

            else
            {
                ModCommunication.SendMessageTo(new DialogMessage("Текущие автокоманды",$"Найдено команд: {commands.Count}",sb.ToString()), Context.Player.SteamUserId);
            }
        }

        [Command("set toolbar", "Делает вашу текущую панель инструментов панелью по умолчанию для новых игроков.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetToolbar()
        {
            if (Context.Player == null)
            {
                Context.Respond("Команда недоступна из консоли.");
                return;
            }

            var toolbar = MySession.Static.Toolbars.TryGetPlayerToolbar(new MyPlayer.PlayerId(Context.Player.SteamUserId));
            if (toolbar == null)
            {
                Context.Respond("Не удалось найти вашу панель инструментов.");
                return;
            }

            EssentialsPlugin.Instance.Config.DefaultToolbar = toolbar.GetObjectBuilder();
            Context.Respond("Новая панель инструментов по умолчанию установлена.");
        }

        [Command("setrank", "Задаёт уровень прав игрока.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetRank(string playerNameOrId, string rank)
        {
            ulong.TryParse(playerNameOrId, out var id);
            id = Utilities.GetPlayerByNameOrId(playerNameOrId)?.SteamUserId ?? id;

            //a SteamID nobody has played with would get the level silently, so only known players are accepted
            if (id == 0 || MySession.Static.Players.TryGetIdentityId(id) == 0)
            {
                Context.Respond($"Игрок '{playerNameOrId}' не найден, или ID неверный.");
                return;
            }

            if (!Enum.TryParse<MyPromoteLevel>(rank, true, out var promoteLevel) || !Enum.IsDefined(typeof(MyPromoteLevel), promoteLevel) || promoteLevel > MyPromoteLevel.Admin)
            {
                Context.Respond($"Неверный уровень '{rank}'.");
                return;
            }

            MySession.Static.SetUserPromoteLevel(id, promoteLevel);
            Context.Respond($"Игроку '{playerNameOrId}' (SteamID {id}) задан уровень '{promoteLevel}'.");
        }

        [Command("reserve", "Добавляет игрока в список зарезервированных слотов.")]
        [Permission(MyPromoteLevel.Admin)]
        public void ReserveSlot(string playerNameOrId)
        {
            ulong.TryParse(playerNameOrId, out var id);
            id = Utilities.GetPlayerByNameOrId(playerNameOrId)?.SteamUserId ?? id;
            
            if (id == 0)
            {
                Context.Respond($"Игрок '{playerNameOrId}' не найден, или ID неверный.");
                return;
            }

            if (MySandboxGame.ConfigDedicated.Reserved.Contains(id))
            {
                Context.Respond($"ID {id} уже в списке зарезервированных.");
                return;
            }
            
            MySandboxGame.ConfigDedicated.Reserved.Add(id);
            MySandboxGame.ConfigDedicated.Save();
            Context.Respond($"ID {id} добавлен в зарезервированные слоты.");
        }

        [Command("unreserve", "Убирает игрока из списка зарезервированных слотов.")]
        [Permission(MyPromoteLevel.Admin)]
        public void UnreserveSlot(string playerNameOrId)
        {
            ulong.TryParse(playerNameOrId, out var id);
            id = Utilities.GetPlayerByNameOrId(playerNameOrId)?.SteamUserId ?? id;
            
            if (id == 0)
            {
                Context.Respond($"Игрок '{playerNameOrId}' не найден, или ID неверный.");
                return;
            }
            
            if (!MySandboxGame.ConfigDedicated.Reserved.Contains(id))
            {
                Context.Respond($"ID {id} нет в списке зарезервированных.");
                return;
            }
            
            MySandboxGame.ConfigDedicated.Reserved.Remove(id);
            MySandboxGame.ConfigDedicated.Save();
            Context.Respond($"ID {id} убран из зарезервированных слотов.");
        }

        private IChatManagerServer ChatManager => EssentialsPlugin.Instance.Torch.CurrentSession.Managers.GetManager<IChatManagerServer>();

        [Command("mute", "Запрещает игроку чат и команды на N минут, без минут или 0 — навсегда.")]
        public void MuteUser(string user, int timeout = 0)
        {
            if (!Utilities.TryGetPlayerByNameOrSteamId(user, out var steamId, out var name))
            {
                RespondUserNotFound(user);
                return;
            }

            var expiry = timeout > 0 ? DateTime.Now.AddMinutes(timeout) : (DateTime?)null;

            if (EssentialsPlugin.Instance.Mutes.Mute(steamId, expiry))
                Context.Respond($"Игрок {name} ({steamId}) замьючен");
            else
                Context.Respond($"Мут игрока {name} ({steamId}) обновлён");
        }

        [Command("unmute", "Снимает мут с игрока. Игрок указывается по нику или SteamID.")]
        public void UnmuteUser(string user)
        {
            if (!Utilities.TryGetPlayerByNameOrSteamId(user, out var steamId, out var name))
            {
                //a muted player can be gone from the world since, for example after !sandbox clean
                if (!ulong.TryParse(user, out steamId) || !ChatManager.MutedUsers.Contains(steamId))
                {
                    RespondUserNotFound(user);
                    return;
                }

                name = user;
            }

            if (!EssentialsPlugin.Instance.Mutes.Unmute(steamId))
                Context.Respond($"Не удалось снять мут: игрок {name} не замьючен.");
            else
                Context.Respond($"С игрока {name} ({steamId}) снят мут");
        }

        private void RespondUserNotFound(string user)
        {
            Context.Respond(ulong.TryParse(user, out _) ? $"SteamID {user} не найден в мире" : $"Игрок {user} не найден");
        }

        [Command("list mute", "Показывает всех замьюченных игроков и оставшееся время мута.")]
        public void ListMute()
        {
            if (ChatManager.MutedUsers.Count == 0)
            {
                Context.Respond("Нет замьюченных игроков.");
                return;
            }

            var sb = new StringBuilder();

            foreach (var m in ChatManager.MutedUsers)
            {
                var s = MySession.Static.Players.TryGetIdentityNameFromSteamId(m);
                if(string.IsNullOrEmpty(s))
                    s = m.ToString();
                var t = EssentialsPlugin.Instance.Mutes.GetExpiry(m);

                sb.AppendLine($"{s}: {(t.HasValue ? (t.Value - DateTime.Now).ToString(@"d\.hh\:mm\:ss") : "без срока")}");
            }

            if (Context.SentBySelf)
            {
                Context.Respond(sb.ToString());
                return;
            }
            var ms = new DialogMessage("Замьюченные игроки", content: sb.ToString());
            ModCommunication.SendMessageTo(ms, Context.Player.SteamUserId);
        }

        [Command("give", "Кладёт указанное количество предмета в инвентарь игрока")]
        [Permission(MyPromoteLevel.Admin)]
        public void give(string playerName, string itemType, string item, int quantity) {
            string type = "MyObjectBuilder_" + itemType;
            VRage.Game.MyDefinitionId.TryParse(type, item, out VRage.Game.MyDefinitionId defID);

            if (defID.ToString().Contains("null")) {
                Context.Respond("Неверный тип предмета");
                return;
            }

            if (playerName != "*") {
                var p = Utilities.GetPlayerByNameOrId(playerName);
                if (p == null) {
                    Context.Respond("Игрок не найден");
                    return;
                }
                Sandbox.Game.MyVisualScriptLogicProvider.AddToPlayersInventory(p.IdentityId, defID, quantity);
                ModCommunication.SendMessageTo(new NotificationMessage($"Вам выдано: {quantity} {item} {itemType}", 5000, "Blue"), p.SteamUserId);
            }

            else {
                foreach (var p in MySession.Static.Players.GetOnlinePlayers()) {
                    var player = Utilities.GetPlayerByNameOrId(p.DisplayName);
                    Sandbox.Game.MyVisualScriptLogicProvider.AddToPlayersInventory(p.Identity.IdentityId, defID, quantity);
                    ModCommunication.SendMessageTo(new NotificationMessage($"Вам выдано: {quantity} {item} {itemType}", 5000, "Blue"), player.SteamUserId);
                }
            }
            Context.Respond("Предметы выданы!");
        }
    }
}
