using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Newtonsoft.Json;
using VRage.ObjectBuilders;

namespace Essentials
{
    public static class Utilities
    {
        public static bool HasBlockType(this IMyCubeGrid grid, string typeName)
        {
            foreach (var block in ((MyCubeGrid)grid).GetFatBlocks())

                if (string.Compare(block.BlockDefinition.Id.TypeId.ToString().Substring(16), typeName, StringComparison.InvariantCultureIgnoreCase) == 0)
                    return true;

            return false;
        }

        public static bool HasBlockSubtype(this IMyCubeGrid grid, string subtypeName)
        {
            foreach (var block in ((MyCubeGrid)grid).GetFatBlocks())
                if (string.Compare(block.BlockDefinition.Id.SubtypeName, subtypeName, StringComparison.InvariantCultureIgnoreCase) == 0)
                    return true;

            return false;
        }
        
        public static bool HasBlockTypeFast(this IMyCubeGrid grid, string typeName)
        {
            var types = typeName.Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<MyObjectBuilderType>();

            foreach (var s in types)
            {
                if (MyObjectBuilderType.TryParse(s, out var typeId))
                {
                    list.Add(typeId);
                }
            }

            if (list.Count == 0)
            {
                return false;
            }
            
            foreach (var block in ((MyCubeGrid) grid).GetFatBlocks())
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (block.BlockDefinition.Id.TypeId == list[i])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool HasBlockSubtypeFast(this IMyCubeGrid grid, string subtypeName)
        {
            var subtypes = subtypeName.Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<MyStringHash>();

            foreach (var s in subtypes)
            {
                list.Add(MyStringHash.TryGet(s));
            }

            foreach (var block in ((MyCubeGrid) grid).GetFatBlocks())
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (block.BlockDefinition.Id.SubtypeId == list[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryGetEntityByNameOrId(string nameOrId, out IMyEntity entity)
        {
            return TryGetEntityByNameOrId(nameOrId, null, out entity);
        }

        /// <param name="nameFilter">Skips entities in the search by name, an ID finds what it finds.</param>
        public static bool TryGetEntityByNameOrId(string nameOrId, Func<IMyEntity, bool> nameFilter, out IMyEntity entity)
        {
            if (long.TryParse(nameOrId, out long id))
                return MyAPIGateway.Entities.TryGetEntityById(id, out entity);

            foreach (var ent in MyEntities.GetEntities())
            {
                if (ent.DisplayName == nameOrId && (nameFilter == null || nameFilter(ent)))
                {
                    entity = ent;
                    return true;
                }
            }

            entity = null;
            return false;
        }

        public static IMyIdentity GetIdentityByNameOrIds(string playerNameOrIds) 
        {
            foreach (var identity in MySession.Static.Players.GetAllIdentities()) 
            {
                if (identity.DisplayName == playerNameOrIds)
                    return identity;

                if (long.TryParse(playerNameOrIds, out long identityId)) 
                    if (identity.IdentityId == identityId)
                        return identity;

                if (ulong.TryParse(playerNameOrIds, out ulong steamId)) 
                {
                    ulong id = GetSteamId(identity.IdentityId);
                    if (id == steamId)
                        return identity;
                }
            }

            return null;
        }

        public static IMyPlayer GetPlayerByNameOrId(string nameOrPlayerId)
        {
            if (!long.TryParse(nameOrPlayerId, out long id))
            {
                foreach (var identity in MySession.Static.Players.GetAllIdentities())
                {
                    if (identity.DisplayName == nameOrPlayerId)
                    {
                        id = identity.IdentityId;
                    }
                }
            }

            if (MySession.Static.Players.TryGetPlayerId(id, out MyPlayer.PlayerId playerId))
            {
                if (MySession.Static.Players.TryGetPlayerById(playerId, out MyPlayer player))
                {
                    return player;
                }
            }

            return null;
        }

        //a player's serial id is 0, the serial ids of the game's bots start at 1
        public static bool TryGetHumanPlayerId(long identityId, out MyPlayer.PlayerId playerId)
        {
            return MySession.Static.Players.TryGetPlayerId(identityId, out playerId) && playerId.SteamId != 0 && playerId.SerialId == 0;
        }

        /// <summary>
        /// Finds any human player of the world, online or not. Of the players with the same name the one online comes first.
        /// </summary>
        public static bool TryGetPlayerByNameOrSteamId(string nameOrSteamId, out ulong steamId, out string name)
        {
            ulong.TryParse(nameOrSteamId, out var parsedId);
            steamId = 0;
            name = null;

            foreach (var identity in MySession.Static.Players.GetAllIdentities())
            {
                if (!TryGetHumanPlayerId(identity.IdentityId, out var playerId))
                    continue;

                if (identity.DisplayName != nameOrSteamId && playerId.SteamId != parsedId)
                    continue;

                var online = MySession.Static.Players.IsPlayerOnline(identity.IdentityId);
                if (steamId == 0 || online)
                {
                    steamId = playerId.SteamId;
                    name = identity.DisplayName;
                }

                if (online)
                    break;
            }

            return steamId != 0;
        }

        public static ulong GetSteamId(long identityId)
        {
            return MySession.Static.Players.TryGetSteamId(identityId);
        }

        public static int GetOnlinePlayerCount()
        {
            var result = 0;

            result =  MySession.Static.Players.GetOnlinePlayers()
                .Count(x => x.IsRealPlayer && !string.IsNullOrEmpty(x.DisplayName));

            return result;
        }

        public static List<MyPlayer> GetOnlinePlayers()
        {
            var result = new List<MyPlayer>(MySession.Static.Players.GetOnlinePlayers()
                .Where(x => x.IsRealPlayer && !string.IsNullOrEmpty(x.DisplayName)));
            return result;
        }

        public static string FormatDataSize(double size)
        {
            string p = MyUtils.FormatByteSizePrefix(ref size);
            return $"{size:N}{p}B";
        }

        public static string DictionaryToJson(Dictionary<string, object> dict) {
            return JsonConvert.SerializeObject(dict, Formatting.Indented);
        }

        public static Dictionary<string, object> JsonToDictionary(string json) {
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        }
    }
}
