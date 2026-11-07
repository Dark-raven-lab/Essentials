using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.World;
using Torch;
using NLog;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;
using VRageRender.Utils;
using VRageMath;
using Newtonsoft.Json;
using System.IO;

namespace Essentials.Commands {
    [Category("pr")]
    public class RanksModule : CommandModule {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public PlayerAccountModule AccModule = new PlayerAccountModule();
        public RanksAndPermissionsModule RanksAndPermissions = new RanksAndPermissionsModule();


        [Command("createrank", "Создаёт ранг.")]
        [Permission(MyPromoteLevel.Admin)]
        public void CreateRank(string name) {
            if (!RanksAndPermissions.GenerateRank(name)) {
                Context.Respond("Такой ранг уже существует!");
                return;
            }
            Context.Respond("Ранг создан!");
        }

        [Command("delrank", "Удаляет ранг.")]
        [Permission(MyPromoteLevel.Admin)]
        public void DeleteRank(string name) {
            Context.Respond(RankOperations.DeleteRank(name));
        }

        [Command("setmaxhomes", "Задаёт, сколько домов могут иметь игроки ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetMaxHomes(string rankName, int value) {
            Context.Respond(RankOperations.SetMaxHomes(rankName, value));
        }

        [Command("reservedslot", "Включает (true) или выключает (false) резервный слот для ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetReservedSlot(string rankName, string boolVal) {
            if (!bool.TryParse(boolVal, out var reserved)) {
                Context.Respond("Аргумент должен быть true или false");
                return;
            }
            Context.Respond(RankOperations.SetReservedSlot(rankName, reserved));
        }

        [Command("renamerank", "Переименовывает ранг, в том числе у игроков.")]
        [Permission(MyPromoteLevel.Admin)]
        public void RenameRank(string oldName, string newName) {
            Context.Respond(RankOperations.RenameRank(oldName, newName));
        }

        [Command("setlevel", "Задаёт рангу уровень прав (None, Scripter, Moderator, SpaceMaster, Admin) и сразу даёт его игрокам ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetLevel(string rankName, string level) {
            Context.Respond(RankOperations.SetLevel(rankName, level));
        }

        [Command("setprefix", "Задаёт префикс ника в чате для ранга. Без префикса убирает его.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetPrefix(string rankName, string prefix = "") {
            //the prefix may hold spaces, so it is the rest of the line
            Context.Respond(RankOperations.SetPrefix(rankName, string.Join(" ", Context.Args.Skip(1))));
        }

        [Command("showprefix", "Включает (true) или выключает (false) префикс ранга в чате.")]
        [Permission(MyPromoteLevel.Admin)]
        public void ShowPrefix(string rankName, string boolVal) {
            if (!bool.TryParse(boolVal, out var show)) {
                Context.Respond("Аргумент должен быть true или false");
                return;
            }
            Context.Respond(RankOperations.SetShowPrefix(rankName, show));
        }

        [Command("info", "Показывает настройки, права и число игроков ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Info(string rankName) {
            var rank = RanksAndPermissions.GetRankData(rankName);
            if (rank == null) {
                Context.Respond(RankOperations.NotFound(rankName));
                return;
            }

            string List(List<string> items) => items.Count == 0 ? "нет" : string.Join(", ", items);
            var sb = new StringBuilder($"Ранг {rank.RankName}");
            if (rank.RankName == EssentialsPlugin.Instance.Config.DefaultRank)
                sb.Append(" (по умолчанию)");
            sb.Append($"\nПрефикс: {(string.IsNullOrEmpty(rank.Prefix) ? "нет" : rank.Prefix)}, {(rank.DisplayPrefix ? "показывается" : "не показывается")}");
            sb.Append($"\nДомов: {rank.MaxHomes}, резервный слот: {(rank.ReservedSlot ? "да" : "нет")}, уровень прав: {rank.KeenLevelRank}");
            sb.Append($"\nНаследует: {List(rank.Inherits)}");
            sb.Append($"\nРазрешено: {List(rank.Permissions.Allowed)}");
            sb.Append($"\nЗапрещено: {List(rank.Permissions.Disallowed)}");
            sb.Append($"\nИгроков: {RankOperations.Members(rank.RankName).Count}");
            Context.Respond(sb.ToString());
        }

        [Command("members", "Показывает игроков ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Members(string rankName) {
            var rank = RanksAndPermissions.GetRankData(rankName);
            if (rank == null) {
                Context.Respond(RankOperations.NotFound(rankName));
                return;
            }

            var names = RankOperations.Members(rank.RankName).Select(a => a.Player ?? a.SteamID.ToString()).OrderBy(n => n).ToList();
            Context.Respond(names.Count == 0
                ? $"У ранга '{rank.RankName}' нет игроков."
                : $"Игроки ранга '{rank.RankName}' ({names.Count}): {string.Join(", ", names)}");
        }

        [Command("setdefaultrank", "Задаёт ранг, который получают новые игроки.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetDefaultRank(string name) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(name);
            if (rank == null) {
                Context.Respond($"Ранга '{name}' не существует!");
                return;
            }
            EssentialsPlugin.Instance.Config.DefaultRank = name;
            EssentialsPlugin.Instance.Save();
            Context.Respond($"Ранг по умолчанию: '{name}'!");
            Log.Info($"Ранг по умолчанию: '{name}'!");
        }

        [Command("setrank", "Задаёт игроку ранг, а с ним уровень прав и резервный слот.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetRank(string playerNameOrID, string rankName) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(rankName);
            if (rank == null) {
                Context.Respond("Такого ранга не существует!");
                return;
            }

            ulong.TryParse(playerNameOrID, out var id);
            id = Utilities.GetPlayerByNameOrId(playerNameOrID)?.SteamUserId ?? id;
            var identityId = id == 0 ? 0 : MySession.Static.Players.TryGetIdentityId(id);
            //GetPlayerByNameOrId sees only online players, so an offline player's name or identity ID is resolved through the identity
            if (identityId == 0) {
                var identity = Utilities.GetIdentityByNameOrIds(playerNameOrID);
                var steamId = identity == null ? 0 : MySession.Static.Players.TryGetSteamId(identity.IdentityId);
                if (steamId != 0) {
                    id = steamId;
                    identityId = identity.IdentityId;
                }
            }

            var accounts = PlayerAccountModule.PlayersAccounts;
            var data = (id != 0 ? accounts.FirstOrDefault(a => a.SteamID == id) : null)
                       ?? accounts.FirstOrDefault(a => a.Player == playerNameOrID);

            if (data == null) {
                //a SteamID nobody has played with would get an account silently, so only known players are accepted
                if (identityId == 0) {
                    Context.Respond($"Игрок '{playerNameOrID}' не найден, или ID неверный.");
                    return;
                }
                Log.Warn($"Player {playerNameOrID} does not have a registered player object... Creating one");
                data = new PlayerAccountModule.PlayerAccountData {
                    Player = MySession.Static.Players.TryGetIdentity(identityId)?.DisplayName ?? playerNameOrID,
                    SteamID = id,
                    IdentityID = identityId,
                };
                accounts.Add(data);
            }

            Context.Respond(RankOperations.SetPlayerRank(data, rank));
        }

        [Command("populate", "Отключена.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Populate(string rank, string level) {

            Context.Respond("Команда отключена");
            return;

            var commandManager = EssentialsPlugin.Instance.CommandManager;
            if (commandManager == null) {
                Context.Respond("Чтобы показать команды, нужна активная сессия");
                return;
            }
            commandManager.Commands.GetNode(Context.Args, out CommandTree.CommandNode node);

            if (node != null) {
                var command = node.Command;
                var children = node.Subcommands.Where(e => e.Value.Command?.MinimumPromoteLevel.ToString() == level).Select(x => x.Key);

                var sb = new StringBuilder();

                if (command != null && (Context.Player == null || command.MinimumPromoteLevel <= Context.Player.PromoteLevel)) {
                    sb.AppendLine($"Синтаксис: {command.SyntaxHelp}");
                    sb.Append(command.HelpText);
                }

                if (node.Subcommands.Count() != 0)
                    sb.Append($"\nПодкоманды: {string.Join(", ", children)}");

                Context.Respond(sb.ToString());
            }
            else {
                var sb = new StringBuilder();
                foreach (var command in commandManager.Commands.WalkTree()) {
                    if (command.IsCommand && (Context.Player == null || command.Command.MinimumPromoteLevel <= Context.Player.PromoteLevel))
                        sb.AppendLine($"{command.Command.SyntaxHelp}\n    {command.Command.HelpText}");
                }
                Context.Respond($"Доступные команды: {sb}");
            }
        }

        [Command("addinheritance", "Даёт рангу все права другого ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddInheritance(string rankName, string inheritanceName) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(rankName);
            if (rank != null) {
                RanksAndPermissionsModule.RankData InheritRank = RanksAndPermissions.GetRankData(inheritanceName);
                if (InheritRank == null) {
                    Context.Respond("Ранга, который вы добавляете в наследование, не существует!");
                    return;
                }
                rank.Inherits.Add(inheritanceName);
                RanksAndPermissions.UpdateRankObject(rank);
                RanksAndPermissions.RebuildAllInheritedRanks();
                Context.Respond("Наследование добавлено!");
                return;
            }
            Context.Respond("Ранга, которому вы добавляете наследование, не существует");
        }

        [Command("delinheritance", "Убирает у ранга права, полученные от другого ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void DelInheritance(string rankName, string inheritanceName) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(rankName);
            if (rank != null) {
                if (!rank.Inherits.Contains(inheritanceName)) {
                    Context.Respond($"{rank.RankName} не наследует {inheritanceName}");
                    return;
                }
                rank.Inherits.Remove(inheritanceName);
                RanksAndPermissions.UpdateRankObject(rank);
                RanksAndPermissions.RebuildAllInheritedRanks();
                Context.Respond("Наследование убрано");
                return;
            }
            Context.Respond("Ранга, у которого вы убираете наследование, не существует");

        }

        [Command("addperm", "Разрешает рангу команду. С минусом перед командой снимает разрешение.")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddPermission(string rankName, string command) {
            RanksAndPermissionsModule.RankData data = new RanksAndPermissionsModule.RankData();
            bool found = false;
            foreach (var Rank in RanksAndPermissionsModule.Ranks) {
                if (Rank.RankName == rankName) {
                    data = Rank;
                    found = true;
                    break;
                }
            }

            if (!found) {
                Context.Respond($"Ранга '{rankName}' не существует!");
                return;
            }

            if (command.Substring(0,1) == "-") {
                string stringAfterChar = command.Substring(command.IndexOf("-") + 1);
                if (data.Permissions.Allowed.Contains(stringAfterChar)) {
                    data.Permissions.Allowed.Remove(stringAfterChar);
                    Context.Respond($"Снято разрешение на команду '{command}' у ранга {data.RankName}!");
                    RanksAndPermissions.UpdateRankObject(data);
                    return;
                }
                Context.Respond($"Команды '{stringAfterChar}' нет в списке.");
                return;
            }

            if (!data.Permissions.Allowed.Contains(command)) {
                data.Permissions.Allowed.Add(command);
                Context.Respond($"Рангу {data.RankName} разрешена команда '{command}'!");
                RanksAndPermissions.UpdateRankObject(data);
                return;
            }

            Context.Respond($"Рангу '{data.RankName}' уже разрешена команда '{command}'");
        }

        [Command("delperm", "Запрещает рангу команду. С минусом перед командой снимает запрет.")]
        [Permission(MyPromoteLevel.Admin)]
        public void RemovePermission(string rankName, string command) {
            RanksAndPermissionsModule.RankData data = new RanksAndPermissionsModule.RankData();
            bool found = false;
            foreach (var Rank in RanksAndPermissionsModule.Ranks) {
                if (Rank.RankName == rankName) {
                    data = Rank;
                    found = true;
                    break;
                }
            }

            if (!found) {
                Context.Respond($"Ранга '{rankName}' не существует!");
                return;
            }

            if (command.Substring(0, 1) == "-") {
                string stringAfterChar = command.Substring(command.IndexOf("-") + 1);
                if (data.Permissions.Disallowed.Contains(stringAfterChar)) {
                    data.Permissions.Disallowed.Remove(stringAfterChar);
                    Context.Respond($"Ранг обновлён");
                    RanksAndPermissions.UpdateRankObject(data);
                    return;
                }
                Context.Respond($"Команды '{stringAfterChar}' нет в списке.");
                return;
            }

            if (!data.Permissions.Disallowed.Contains(command)) {
                data.Permissions.Disallowed.Add(command);
                Context.Respond($"Рангу {data.RankName} запрещена команда '{command}'!");
                RanksAndPermissions.UpdateRankObject(data);
                return;
            }

            Context.Respond($"Рангу {data.RankName} уже запрещена команда '{command}'!");
        }


        [Command("addplayerperm", "Разрешает игроку команду. С минусом перед командой снимает разрешение.")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddPlayerPermission(string playerName, string command) {
            PlayerAccountModule.PlayerAccountData data = new PlayerAccountModule.PlayerAccountData();
            bool found = false;
            foreach (var player in PlayerAccountModule.PlayersAccounts) {
                if (player.Player == playerName) {
                    data = player;
                    found = true;
                    break;
                }
            }

            if (!found) {
                Context.Respond($"У игрока '{playerName}' нет зарегистрированного аккаунта!");
                return;
            }

            if (command.Substring(0, 1) == "-") {
                string stringAfterChar = command.Substring(command.IndexOf("-") + 1);
                if (data.Permissions.Allowed.Contains(stringAfterChar)) {
                    data.Permissions.Allowed.Remove(stringAfterChar);
                    Context.Respond($"Снято разрешение на команду '{command}' у игрока {data.Player}!");
                    AccModule.UpdatePlayerAccount(data);
                    return;
                }
                Context.Respond($"Команды '{stringAfterChar}' нет в списке.");
                return;
            }

            if (!data.Permissions.Allowed.Contains(command)) {
                data.Permissions.Allowed.Add(command);
                Context.Respond($"Игроку {data.Player} разрешена команда '{command}'!");
                AccModule.UpdatePlayerAccount(data);
                return;
            }

            Context.Respond($"Игроку '{data.Player}' уже разрешена команда '{command}'");
        }

        [Command("delplayerperm", "Запрещает игроку команду. С минусом перед командой снимает запрет.")]
        [Permission(MyPromoteLevel.Admin)]
        public void RemovePlayerPermission(string playerName, string command) {
            PlayerAccountModule.PlayerAccountData data = new PlayerAccountModule.PlayerAccountData();
            bool found = false;
            foreach (var player in PlayerAccountModule.PlayersAccounts) {
                if (player.Player == playerName) {
                    data = player;
                    found = true;
                    break;
                }
            }

            if (!found) {
                Context.Respond($"У игрока '{playerName}' нет зарегистрированного аккаунта!");
                return;
            }

            if (command.Substring(0, 1) == "-") {
                string stringAfterChar = command.Substring(command.IndexOf("-") + 1);
                if (data.Permissions.Disallowed.Contains(stringAfterChar)) {
                    data.Permissions.Disallowed.Remove(stringAfterChar);
                    Context.Respond($"Ранг обновлён");
                    AccModule.UpdatePlayerAccount(data);
                    return;
                }
                Context.Respond($"Команды '{stringAfterChar}' нет в списке.");
                return;
            }

            if (!data.Permissions.Disallowed.Contains(command)) {
                data.Permissions.Disallowed.Add(command);
                Context.Respond($"Игроку {data.Player} запрещена команда '{command}'!");
                AccModule.UpdatePlayerAccount(data);
                return;
            }

            Context.Respond($"Игроку {data.Player} уже запрещена команда '{command}'!");
        }

        [Command("listranks", "Показывает все ранги.")]
        [Permission(MyPromoteLevel.None)]
        public void ListRanks() {
            bool found = false;
            string Ranks = "Ранги: ";
            foreach (var rank in RanksAndPermissionsModule.Ranks) {
                found = true;
                Ranks += ($"{rank.RankName},");
            }
            if(!found) {
                Context.Respond("Ранги не найдены");
                return;
            }
            Ranks = Ranks.TrimEnd(',');
            Context.Respond(Ranks);
        }
    }
}
