using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Torch.API;
using System.Threading.Tasks;
using Torch.Commands;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game.World;

namespace Essentials {
    //Possibly build extension plugin which will sync discord roles with custom roles?
    public class RanksAndPermissionsModule {
        public static List<RankData> Ranks = new List<RankData>();
        public static PlayerAccountModule PlayerAccountModule = new PlayerAccountModule();
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public static Dictionary<ulong, List<RankData>> PlayersInheritedRanksStore = new Dictionary<ulong, List<RankData>>();
        public bool debug = true;

        public class Permissions {
            public List<string> Allowed { get; set; } = new List<string>();
            public List<string> Disallowed { get; set; } = new List<string>();
        }

        public class RankData {
            [JsonProperty(Order = 1)]
            public string RankName { get; set; }

            [JsonProperty(Order = 2)]
            public string Prefix { get; set; } = "";

            [JsonProperty(Order = 3)]
            public bool DisplayPrefix { get; set; } = false;

            [JsonProperty(Order = 4)]
            public int MaxHomes { get; set; } = 3;

            [JsonProperty(Order = 5)]
            public bool ReservedSlot { get; set; } = false;

            [JsonProperty(Order = 6)]
            public string KeenLevelRank { get; set; } = "None";

            [JsonProperty(Order = 7)]
            public Permissions Permissions = new Permissions();

            [JsonProperty(Order = 8)]
            public List<string> Inherits { get; set; } = new List<string>();

            [JsonProperty(Order = 9)]
            public Dictionary<Guid, object> PluginData { get; set; } = new Dictionary<Guid, object>();
        }

        public void UpdateRankObject(RankData obj) {
            var objectToRepalce = Ranks.Where(i => i.RankName == obj.RankName).First();
            var index = Ranks.IndexOf(objectToRepalce);
            if (index != -1)
                Ranks[index] = obj;
            SaveRankData();
        }

        public void SaveRankData() {
            File.WriteAllText(EssentialsPlugin.Instance.rankDataPath, JsonConvert.SerializeObject(Ranks, Formatting.Indented));
            RankOperations.RaiseChanged();
        }

        public bool GenerateRank(string name) {
            RankData data = new RankData();
            bool found = false;
            foreach (var Rank in Ranks) {
                if (Rank.RankName == name) {
                    found = true;
                    Log.Info("Default rank already generated!");
                    return false;
                }
            }

            if (!found) {
                Log.Warn($"Creating new rank object called '{name}'");
                data.RankName = name;
                Ranks.Add(data);
                SaveRankData();
                return true;
            }
            return false;
        }
        public RankData GetRankData(string name) {
            RankData rank = new RankData();
            bool found = false;
            foreach (var RankObject in Ranks) {
                if (RankObject.RankName == name) {
                    found = true;
                    rank = RankObject;
                    break;
                }
            }
            if (found)
                return rank;
            return null;
        }
        /// <summary>
        /// The personal permissions of the player come first, then the player's rank, then the ranks it inherits, the
        /// nearest first. The first of them that names the command decides, a ban winning over an allow within one;
        /// "*" counts only in a list that does not name the command.
        /// </summary>
        /// <returns>Null when no list names the command or "*": the ranks leave it to Torch.</returns>
        public bool? RankHasPermission(string rank, string cmd, ulong forSteamID) {
            RankData data = GetRankData(rank);
            if (data == null) {
                Log.Error($"GetRankData({rank}) returned null.");
                return null;
            }

            if (!PlayersInheritedRanksStore.ContainsKey(forSteamID))
                BuildInheritedRanks(forSteamID);

            var lists = new List<Permissions>();
            var account = PlayerAccountModule.GetAccount(forSteamID);
            if (account != null)
                lists.Add(account.Permissions);
            lists.Add(data.Permissions);
            lists.AddRange(PlayersInheritedRanksStore[forSteamID].Select(r => r.Permissions));

            foreach (var list in lists) {
                if (list.Disallowed.Contains(cmd))
                    return false;
                if (list.Allowed.Contains(cmd))
                    return true;
                if (list.Disallowed.Contains("*"))
                    return false;
                if (list.Allowed.Contains("*"))
                    return true;
            }
            return null;
        }

        public void HasCommandPermission(Command command, IMyPlayer player, bool hasPermission, ref bool? hasPermissionOverride) {
            if (!EssentialsPlugin.Instance.Config.EnableRanks || player == null)
                return;

            string playersRank = PlayerAccountModule.GetRank(player.SteamUserId);
            string cmd = string.Join(" ", command.Path);
            var hasPerm = RankHasPermission(playersRank, cmd, player.SteamUserId);
            if(!debug) {
                Log.Error($"HasPerm returned {hasPerm}");
            }

            //a rank without a word on the command keeps what Torch decided, so an empty rank blocks nothing
            if (hasPerm == null)
                return;

            if (EssentialsPlugin.Instance.Config.OverrideVanillaPerms && hasPerm == true)
                hasPermissionOverride = true;

            if (hasPermission && hasPerm == false) {
                hasPermissionOverride = false;
                Log.Info($"{player.DisplayName} tried to use the blocked command '{cmd}'");
                ModCommunication.SendMessageTo(new NotificationMessage($"У вас нет прав на эту команду!", 10000, "Red"), player.SteamUserId);
            }
        }

        public static List<string> GetInheritRankList(ulong steamID)
        {
            var ranksStore = new List<RankData>(PlayersInheritedRanksStore[steamID]);
            var permList = new List<string>();
            foreach (var rankData in ranksStore)
            {
                permList.Add(rankData.RankName);
                permList.AddRange(rankData.Inherits.Where(x=>!permList.Contains(x)));
            }

            return permList;

        }

        public MyPromoteLevel ParseMyPromoteLevel(string stringValue) {
            MyPromoteLevel myPromoteLevel = new MyPromoteLevel();

            switch(stringValue) {
                case "None":
                    myPromoteLevel = MyPromoteLevel.None;
                    break;

                case "Scripter":
                    myPromoteLevel = MyPromoteLevel.SpaceMaster;
                    break;

                case "SpaceMaster":
                    myPromoteLevel = MyPromoteLevel.SpaceMaster;
                    break;

                case "Moderator":
                    myPromoteLevel = MyPromoteLevel.Moderator;
                    break;

                case "Admin":
                    myPromoteLevel = MyPromoteLevel.Admin;
                    break;
            }
            return myPromoteLevel;
        }

        public void RegisterInheritedRanks(IPlayer player) {
            ulong steamID = player.SteamId;
            Log.Info($"Binding ranks to {player.Name}'s session (Expires when server restarts)");
            RankData MainRank = GetRankData(PlayerAccountModule.GetRank(steamID));
            if (!PlayersInheritedRanksStore.ContainsKey(steamID))
                BuildInheritedRanks(steamID);
            if (EssentialsPlugin.Instance.Config.OverrideVanillaPerms) {
                MySession.Static.SetUserPromoteLevel(steamID, ParseMyPromoteLevel(MainRank.KeenLevelRank));
            }
            string Ranks =($"{MainRank.RankName},");
            foreach (RankData inherited in PlayersInheritedRanksStore[steamID]) {
                Ranks += ($"{inherited.RankName},");
            }
            Ranks = Ranks.TrimEnd(',');
            Log.Info($"The following ranks have been assiged to {player.Name}: {Ranks}");
        }

        private void BuildInheritedRanks(ulong steamID) {
            PlayersInheritedRanksStore[steamID] = new List<RankData>();
            GetInheritedRanks(GetRankData(PlayerAccountModule.GetRank(steamID)), steamID);
        }

        //the store is built once per session on join, so rank changes made with commands must rebuild it for players already online
        public void RebuildInheritedRanks(ulong steamID) {
            if (PlayersInheritedRanksStore.ContainsKey(steamID))
                BuildInheritedRanks(steamID);
        }

        public void RebuildAllInheritedRanks() {
            foreach (var steamID in PlayersInheritedRanksStore.Keys.ToList())
                BuildInheritedRanks(steamID);
        }

        public void GetInheritedRanks(RankData toplevel, ulong steamID) {
            if (toplevel == null) {
                Log.Warn("GetInheritedRanks was passed a null RankData Object!");
                return;
            }
            var list = PlayersInheritedRanksStore[steamID];
            foreach (var name in toplevel.Inherits) {
                var rank = GetRankData(name);
                //ranks that inherit each other in a loop would otherwise recurse until the stack overflows
                if (rank == null || list.Contains(rank))
                    continue;
                list.Add(rank);
                GetInheritedRanks(rank, steamID);
            }
        }
    }
}
