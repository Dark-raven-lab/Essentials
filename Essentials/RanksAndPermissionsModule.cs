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
        public bool RankHasPermission(string rank, string cmd, ulong forSteamID) {
            Dictionary<string, List<string>> InheritedPerms = new Dictionary<string, List<string>>();
            RankData data = GetRankData(rank);
            PlayerAccountModule.PlayerAccountData Account = PlayerAccountModule.GetAccount(forSteamID);
            if (data == null) {
                Log.Error($"GetRankData({rank}) returned null.");
                return false;
            }
            bool hasPermission = false;
            /*
             * If there ranks in the Inherit list,
             * check their permissions first.
             */
            if (data.Inherits.Count != 0) {
                InheritedPerms = GetInheritPermList(forSteamID);

                if (InheritedPerms["Allowed"].Contains("*") && !InheritedPerms["Disallowed"].Contains(cmd)) {
                    hasPermission = true;
                }
                else if (InheritedPerms["Allowed"].Contains(cmd)) {
                    hasPermission = true;
                }
                else if (InheritedPerms["Disallowed"].Contains("*") && !InheritedPerms["Allowed"].Contains(cmd)) {
                    hasPermission = false;
                }
                else if ((InheritedPerms["Disallowed"].Contains(cmd))) {
                    hasPermission = false;
                }

                if (hasPermission)
                    return hasPermission;
            }

            /*
             * Check the players main rank
             * permissions
             */
            if (data.Permissions.Allowed.Count == 0 && data.Permissions.Disallowed.Count == 0) {
                return hasPermission;
            }

            if (data.Permissions.Allowed.Contains("*") && !data.Permissions.Disallowed.Contains(cmd)) {
                hasPermission = true;
            }
            else if (data.Permissions.Allowed.Contains(cmd)) {
                hasPermission = true;
            }
            else if (data.Permissions.Disallowed.Contains("*") && !data.Permissions.Allowed.Contains(cmd)) {
                hasPermission = false;
            }
            else if (data.Permissions.Disallowed.Contains(cmd)) {
                hasPermission = false;
            }

            if (hasPermission)
                return hasPermission;


            /*
             * Check the player specific permissions.
             */
            if (Account.Permissions.Allowed.Count == 0 && Account.Permissions.Disallowed.Count == 0) {
                return hasPermission;
            }

            if (Account.Permissions.Allowed.Contains("*") && !Account.Permissions.Disallowed.Contains(cmd)) {
                hasPermission = true;
            }
            else if (Account.Permissions.Allowed.Contains(cmd)) {
                hasPermission = true;
            }
            else if (Account.Permissions.Disallowed.Contains("*") && !Account.Permissions.Allowed.Contains(cmd)) {
                hasPermission = false;
            }
            else if (Account.Permissions.Disallowed.Contains(cmd)) {
                hasPermission = false;
            }

            //return result
            return hasPermission;
        }

        public void HasCommandPermission(Command command, IMyPlayer player, bool hasPermission, ref bool? hasPermissionOverride) {
            if (!EssentialsPlugin.Instance.Config.EnableRanks)
                return;

            string playersRank = PlayerAccountModule.GetRank(player.SteamUserId);
            string cmd = "";
            foreach (var part in command.Path) {
                cmd += part + " ";
            }
            cmd = cmd.TrimEnd();
            bool hasPerm = RankHasPermission(playersRank, cmd, player.SteamUserId);
            if(!debug) {
                Log.Error($"HasPerm returned {hasPerm}");
            }

            if (EssentialsPlugin.Instance.Config.OverrideVanillaPerms && hasPerm)
                hasPermissionOverride = hasPerm;

            if (hasPermission && !hasPerm) {
                hasPermissionOverride = hasPerm;
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

        public Dictionary<string, List<string>> GetInheritPermList(ulong steamID) {
            Dictionary<string, List<string>> Perms = new Dictionary<string, List<string>>();

            List<string> Allowed = new List<string>();
            List<string> Disallowed = new List<string>();

            foreach (RankData rank in PlayersInheritedRanksStore[steamID]) {
                foreach (var AllowedCommand in rank.Permissions.Allowed) {
                    if (!Allowed.Contains(AllowedCommand)) {
                        Allowed.Add(AllowedCommand);
                    }
                }

                foreach (var DisallowedCommand in rank.Permissions.Disallowed) {
                    if (!Disallowed.Contains(DisallowedCommand)) {
                        Disallowed.Add(DisallowedCommand);
                    }
                }
            }

            Perms.Add("Allowed", Allowed);
            Perms.Add("Disallowed", Disallowed);
            return Perms;

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
