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


        [Command("createrank")]
        [Permission(MyPromoteLevel.Admin)]
        public void CreateRank(string name) {
            if (!RanksAndPermissions.GenerateRank(name)) {
                Context.Respond("Такой ранг уже существует!");
                return;
            }
            Context.Respond("Ранг создан!");
        }

        [Command("delrank")]
        [Permission(MyPromoteLevel.Admin)]
        public void DeleteRank(string name) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(name);
            if (rank == null) {
                Context.Respond($"Ранга '{name}' не существует!");
                return;
            }
            RanksAndPermissionsModule.Ranks.Remove(rank);
            RanksAndPermissions.SaveRankData();

        }

        [Command("setmaxhomes")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetMaxHomes(string rankName, int value) {
            RanksAndPermissionsModule.RankData Rank = RanksAndPermissions.GetRankData(rankName);
            if (Rank == null) {
                Context.Respond($"Ранга '{rankName}' не существует!");
                return;
            }

            Rank.MaxHomes = value;
            Context.Respond($"Игроки с рангом '{Rank.RankName}' теперь могут установить домов: {value}");
        }

        [Command("reservedslot")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetReservedSlot(string rankName, string boolVal) {
            if (boolVal != "true" || boolVal != "false") {
                Context.Respond("Аргумент должен быть true или false");
            }
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(rankName);
            if (rank == null) {
                Context.Respond($"Ранга '{rankName}' не существует!");
                return;
            }
            rank.ReservedSlot = bool.Parse(boolVal);
            RanksAndPermissions.UpdateRankObject(rank);

        }

        [Command("renamerank")]
        [Permission(MyPromoteLevel.Admin)]
        public void RenameRank(string oldName, string newName) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(oldName);
            if (rank == null) {
                Context.Respond($"Ранга '{oldName}' не существует!");
                return;
            }
            rank.RankName = newName;
            RanksAndPermissions.UpdateRankObject(rank);
            RanksAndPermissions.UpdateRegisteredPlayersRanks(newName);
        }

        [Command("setdefaultrank")]
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

        [Command("setrank")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetRank(string playerNameOrID, string rankName) {
            RanksAndPermissionsModule.RankData rank = RanksAndPermissions.GetRankData(rankName);

            ulong.TryParse(playerNameOrID, out var id);
            id = Utilities.GetPlayerByNameOrId(playerNameOrID)?.SteamUserId ?? id;
            /*IMyPlayer player = Utilities.GetPlayerByNameOrId(playerName);*/
            if (id == 0) {
                Context.Respond($"Игрок '{playerNameOrID}' не найден, или ID неверный.");
                return;
            }
            var player = Utilities.GetPlayerByNameOrId(playerNameOrID);
            if (rank == null) {
                Context.Respond("Такого ранга не существует!");
                return;
            }

            PlayerAccountModule.PlayerAccountData data = new PlayerAccountModule.PlayerAccountData();
            var RegisteredPlayerNames = PlayerAccountModule.PlayersAccounts.Select(o => o.Player).ToList();
            var RegisteredPlayerSteamIDs = PlayerAccountModule.PlayersAccounts.Select(o => o.SteamID).ToList();
            if (!RegisteredPlayerNames.Contains(playerNameOrID) && !RegisteredPlayerSteamIDs.Contains(id)) {
                Log.Warn($"Player {playerNameOrID} does have registered player object... Creating one");
                data.Player = playerNameOrID;
                data.SteamID = player.SteamUserId;
            }
            
            if(RegisteredPlayerNames.Contains(playerNameOrID)) {
                data = PlayerAccountModule.PlayersAccounts.Single(a => a.Player == playerNameOrID);
            } 

            if(RegisteredPlayerSteamIDs.Contains(id)) {
                data = PlayerAccountModule.PlayersAccounts.Single(a => a.SteamID == id);
            }

            data.Rank = rank.RankName;
            if (rank.ReservedSlot && !MySandboxGame.ConfigDedicated.Reserved.Contains(id)) {
                MySandboxGame.ConfigDedicated.Reserved.Add(id);
            } else if (!rank.ReservedSlot && MySandboxGame.ConfigDedicated.Reserved.Contains(id)) {
                MySandboxGame.ConfigDedicated.Reserved.Remove(id);
            }
            MySession.Static.SetUserPromoteLevel(data.SteamID, RanksAndPermissions.ParseMyPromoteLevel(rank.KeenLevelRank));
            Context.Respond($"Игроку {data.Player} задан ранг {rank.RankName}");
            Log.Info($"Игроку {data.Player} задан ранг {rank.RankName}");
            AccModule.UpdatePlayerAccount(data);
        }

        [Command("populate")]
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

        [Command("addinheritance")]
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
                Context.Respond("Наследование добавлено!");
                return;
            }
            Context.Respond("Ранга, которому вы добавляете наследование, не существует");
        }

        [Command("delinheritance")]
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
                Context.Respond("Наследование убрано");
                return;
            }
            Context.Respond("Ранга, у которого вы убираете наследование, не существует");

        }

        [Command("addperm")]
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
                    Context.Respond($"Снят запрет на команду '{command}' у ранга {data.RankName}!");
                    RanksAndPermissions.UpdateRankObject(data);
                    return;
                }
            }

            if (!data.Permissions.Allowed.Contains(command)) {
                data.Permissions.Allowed.Add(command);
                Context.Respond($"Рангу {data.RankName} разрешена команда '{command}'!");
                RanksAndPermissions.UpdateRankObject(data);
                return;
            }

            Context.Respond($"Рангу '{data.RankName}' уже разрешена команда '{command}'");
        }

        [Command("delperm")]
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
            }

            if (!data.Permissions.Disallowed.Contains(command)) {
                data.Permissions.Disallowed.Add(command);
                Context.Respond($"Рангу {data.RankName} запрещена команда '{command}'!");
                RanksAndPermissions.UpdateRankObject(data);
                return;
            }

            Context.Respond($"Рангу {data.RankName} уже запрещена команда '{command}'!");
        }


        [Command("addplayerperm")]
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
                    Context.Respond($"Снят запрет на команду '{command}' у игрока {data.Player}!");
                    AccModule.UpdatePlayerAccount(data);
                    return;
                }
            }

            if (!data.Permissions.Allowed.Contains(command)) {
                data.Permissions.Allowed.Add(command);
                Context.Respond($"Игроку {data.Player} разрешена команда '{command}'!");
                AccModule.UpdatePlayerAccount(data);
                return;
            }

            Context.Respond($"Игроку '{data.Player}' уже разрешена команда '{command}'");
        }

        [Command("delplayerperm")]
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
            }

            if (!data.Permissions.Disallowed.Contains(command)) {
                data.Permissions.Disallowed.Add(command);
                Context.Respond($"Игроку {data.Player} запрещена команда '{command}'!");
                AccModule.UpdatePlayerAccount(data);
                return;
            }

            Context.Respond($"Игроку {data.Player} уже запрещена команда '{command}'!");
        }

        [Command("listranks")]
        [Permission(MyPromoteLevel.None)]
        public void ListRanks(string listRankName) {
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
