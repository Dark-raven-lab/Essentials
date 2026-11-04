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
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(name);
            if (rank == null) {
                Context.Respond($"Ранга '{name}' не существует!");
                return;
            }
            var defaultRank = EssentialsPlugin.Instance.Config.DefaultRank;
            if (rank.RankName == defaultRank) {
                Context.Respond("Нельзя удалить ранг по умолчанию. Сначала задайте другой через !pr setdefaultrank.");
                return;
            }

            RanksAndPermissionsModule.Ranks.Remove(rank);
            foreach (var other in RanksAndPermissionsModule.Ranks)
                other.Inherits.RemoveAll(n => n == rank.RankName);
            RanksAndPermissions.SaveRankData();

            //ValidateRanks does this reassignment only on load. The reserved list is touched only when the flag differs, so slots given with !admin reserve survive
            var defaultData = RanksAndPermissions.GetRankData(defaultRank);
            var syncReserved = defaultData != null && defaultData.ReservedSlot != rank.ReservedSlot;
            var moved = 0;
            var reservedChanged = false;
            foreach (var account in PlayerAccountModule.PlayersAccounts.Where(a => a.Rank == rank.RankName)) {
                account.Rank = defaultRank;
                if (syncReserved && SetReserved(account.SteamID, defaultData.ReservedSlot))
                    reservedChanged = true;
                moved++;
            }
            if (moved > 0)
                AccModule.SaveAccountData();
            if (reservedChanged)
                MySandboxGame.ConfigDedicated.Save();
            RanksAndPermissions.RebuildAllInheritedRanks();

            Context.Respond($"Ранг '{rank.RankName}' удалён. Игроков переведено на ранг '{defaultRank}': {moved}.");
        }

        [Command("setmaxhomes", "Задаёт, сколько домов могут иметь игроки ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetMaxHomes(string rankName, int value) {
            RanksAndPermissionsModule.RankData Rank = RanksAndPermissions.GetRankData(rankName);
            if (Rank == null) {
                Context.Respond($"Ранга '{rankName}' не существует!");
                return;
            }

            if (value < 0) {
                Context.Respond("Число домов не может быть отрицательным.");
                return;
            }

            Rank.MaxHomes = value;
            RanksAndPermissions.SaveRankData();
            Context.Respond($"Игроки с рангом '{Rank.RankName}' теперь могут установить домов: {value}");
        }

        [Command("reservedslot", "Включает (true) или выключает (false) резервный слот для ранга.")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetReservedSlot(string rankName, string boolVal) {
            if (!bool.TryParse(boolVal, out var reserved)) {
                Context.Respond("Аргумент должен быть true или false");
                return;
            }
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(rankName);
            if (rank == null) {
                Context.Respond($"Ранга '{rankName}' не существует!");
                return;
            }
            rank.ReservedSlot = reserved;
            RanksAndPermissions.SaveRankData();

            var changed = 0;
            foreach (var account in PlayerAccountModule.PlayersAccounts.Where(a => a.Rank == rank.RankName))
                if (SetReserved(account.SteamID, reserved))
                    changed++;
            if (changed > 0)
                MySandboxGame.ConfigDedicated.Save();

            Context.Respond($"Резервный слот у ранга '{rank.RankName}' {(reserved ? "включён" : "выключен")}. Изменён список резерва для игроков: {changed}.");
        }

        [Command("renamerank", "Переименовывает ранг, в том числе у игроков.")]
        [Permission(MyPromoteLevel.Admin)]
        public void RenameRank(string oldName, string newName) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(oldName);
            if (rank == null) {
                Context.Respond($"Ранга '{oldName}' не существует!");
                return;
            }
            if (RanksAndPermissions.GetRankData(newName) != null) {
                Context.Respond($"Ранг '{newName}' уже существует!");
                return;
            }

            rank.RankName = newName;
            foreach (var other in RanksAndPermissionsModule.Ranks)
                for (var i = 0; i < other.Inherits.Count; i++)
                    if (other.Inherits[i] == oldName)
                        other.Inherits[i] = newName;
            RanksAndPermissions.SaveRankData();

            var renamed = 0;
            foreach (var account in PlayerAccountModule.PlayersAccounts.Where(a => a.Rank == oldName)) {
                account.Rank = newName;
                renamed++;
            }
            if (renamed > 0)
                AccModule.SaveAccountData();

            var config = EssentialsPlugin.Instance.Config;
            if (config.DefaultRank == oldName) {
                config.DefaultRank = newName;
                EssentialsPlugin.Instance.Save();
            }

            Context.Respond($"Ранг '{oldName}' переименован в '{newName}'. Игроков с этим рангом: {renamed}.");
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

            data.Rank = rank.RankName;
            AccModule.SaveAccountData();
            if (SetReserved(data.SteamID, rank.ReservedSlot))
                MySandboxGame.ConfigDedicated.Save();
            MySession.Static.SetUserPromoteLevel(data.SteamID, RanksAndPermissions.ParseMyPromoteLevel(rank.KeenLevelRank));
            RanksAndPermissions.RebuildInheritedRanks(data.SteamID);
            Context.Respond($"Игроку {data.Player} задан ранг {rank.RankName}");
            Log.Info($"Игроку {data.Player} задан ранг {rank.RankName}");
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

        private static bool SetReserved(ulong steamId, bool reserved) {
            var list = MySandboxGame.ConfigDedicated.Reserved;
            if (steamId == 0 || list.Contains(steamId) == reserved)
                return false;
            if (reserved)
                list.Add(steamId);
            else
                list.Remove(steamId);
            return true;
        }
    }
}
