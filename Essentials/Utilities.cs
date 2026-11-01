using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
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
            if (long.TryParse(nameOrId, out long id))
                return MyAPIGateway.Entities.TryGetEntityById(id, out entity);

            foreach (var ent in MyEntities.GetEntities())
            {
                if (ent.DisplayName == nameOrId)
                {
                    entity = ent;
                    return true;
                }
            }

            entity = null;
            return false;
        }

        /// <summary>
        /// Everything a command can find: first the characters of players online, then grids and other entities, then all other characters.
        /// MyEntities doesn't list seated characters, and a projection can have the same name as the real grid.
        /// The current character of a player is listed even if dead so a command can tell that, other dead bodies are not.
        /// </summary>
        public static IEnumerable<MyEntity> GetSearchableEntities()
        {
            var players = MySession.Static.Players.GetAllIdentities()
                .Where(x => x.Character != null && !x.Character.MarkedForClose)
                .ToLookup(x => MySession.Static.Players.IsPlayerOnline(x.IdentityId), x => (MyEntity)x.Character);
            var entities = MyEntities.GetEntities();

            return players[true]
                .Concat(entities.Where(x => !(x is MyCharacter) && !IsProjection(x)))
                .Concat(players[false])
                .Concat(entities.Where(x => x is MyCharacter character && !character.IsDead))
                .Distinct();
        }

        /// <summary>
        /// Finds entities by entity ID or exact name. The character of a player is also found by identity ID or Steam ID.
        /// An ID gives one entity, a name can give several, in the order of <see cref="GetSearchableEntities"/>.
        /// </summary>
        public static List<MyEntity> FindEntities(string nameOrId)
        {
            long.TryParse(nameOrId, out var id);
            if (id != 0 && MyEntities.TryGetEntityById(id, out MyEntity byId))
                return IsProjection(byId) ? new List<MyEntity>() : new List<MyEntity> { byId };

            ulong.TryParse(nameOrId, out var steamId);
            return GetSearchableEntities().Where(entity => GetName(entity) == nameOrId || entity.DisplayName == nameOrId
                || (entity is MyCharacter character && IsCharacterOf(character, nameOrId, id, steamId))).ToList();
        }

        private static bool IsCharacterOf(MyCharacter character, string name, long identityId, ulong steamId)
        {
            var characterIdentityId = character.GetPlayerIdentityId();
            return (identityId != 0 && characterIdentityId == identityId)
                || (steamId != 0 && GetSteamId(characterIdentityId) == steamId)
                || character.GetIdentity()?.DisplayName == name;
        }

        /// <summary>
        /// The name to show for an entity: planets and asteroids are known by the name of their storage.
        /// </summary>
        public static string GetName(IMyEntity entity)
        {
            return (entity as IMyVoxelBase)?.StorageName ?? entity.DisplayName;
        }

        //also true for a block of a projection. A grid can have no physics for other reasons, for example optimizations
        public static bool IsProjection(IMyEntity entity)
        {
            return entity.GetTopMostParent() is MyCubeGrid grid && (grid.IsPreview || grid.Projector != null);
        }

        /// <summary>
        /// A connector gives a physical link, so a docked ship and its base are one physical group. Rotors, pistons and hinges give a mechanical one.
        /// </summary>
        public static List<MyCubeGrid> GetConnectedGrids(MyCubeGrid grid, GridLinkTypeEnum link)
        {
            return MyCubeGridGroups.Static.GetGroups(link).GetGroupNodes(grid);
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

        // GetPosition is the grid's pivot, which can be far from its blocks. Fractions are cut off, not rounded.
        public static string FormatGridCenter(MyCubeGrid grid)
        {
            var center = grid.PositionComp.WorldAABB.Center;
            return $"{(long)center.X}:{(long)center.Y}:{(long)center.Z}";
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
