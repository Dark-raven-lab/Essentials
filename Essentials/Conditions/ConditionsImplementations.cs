using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vector3D = VRageMath.Vector3D;

namespace Essentials.Commands
{
    [ConditionModule]
    public static class ConditionsImplementations
    {
        [Condition("name", helpText: "Ищет гриды с подходящим именем. Можно использовать регулярное выражение.")]
        public static bool NameMatches(MyCubeGrid grid, Regex regex)
        {
            if (string.IsNullOrEmpty(grid.DisplayName))
                return false;

            return regex.IsMatch(grid.DisplayName);
        }

        [Condition("blockslessthan", helpText: "Ищет гриды, в которых блоков меньше указанного числа.")]
        public static bool BlocksLessThan(MyCubeGrid grid, int count)
        {
            return grid.BlocksCount < count;
        }

        [Condition("pcugreaterthan", helpText: "Ищет гриды, у которых PCU больше указанного числа.")]
        public static bool PCUGreaterThan(MyCubeGrid grid, int pcu)
        {
            return grid.BlocksPCU > pcu;
        }

        [Condition("pculessthan", helpText: "Ищет гриды, у которых PCU меньше указанного числа.")]
        public static bool PCULessThan(MyCubeGrid grid, int pcu)
        {
            return grid.BlocksPCU < pcu;
        }

        [Condition("hasgridtype", helpText: "Ищет гриды указанного типа (large | small | ship | static).")]
        public static bool HasGridType(MyCubeGrid grid, string gridType) 
        {
            if (string.IsNullOrEmpty(gridType))
                return false;

            if (string.Compare(gridType, "static", StringComparison.InvariantCultureIgnoreCase) == 0)
                return grid.IsStatic;

            if (string.Compare(gridType, "ship", StringComparison.InvariantCultureIgnoreCase) == 0) 
                return !grid.IsStatic;

            if (string.Compare(gridType, "large", StringComparison.InvariantCultureIgnoreCase) == 0)
                return grid.GridSizeEnum == VRage.Game.MyCubeSize.Large;

            if (string.Compare(gridType, "small", StringComparison.InvariantCultureIgnoreCase) == 0) 
                return grid.GridSizeEnum == VRage.Game.MyCubeSize.Small;

            // In all other cases, just return false.
            return false;
        }
        
        [Condition("hasownertype", helpText: "Ищет гриды с указанным типом владельца (npc | player | nobody).")]
        public static bool HasOwnerType(MyCubeGrid grid, string ownerType)
        {
            if (string.IsNullOrEmpty(ownerType))
                return false;

            // Get the owner type of the grid.
            var gridOwnerType = Utils.Ownership.GetOwnerType(grid);

            // Check provided input string.
            switch (ownerType.ToLower().Trim())
            {
                // Check if grid is owner by an NPC.
                case "npc":
                case "npcs":
                    return gridOwnerType == Utils.Ownership.OwnerType.NPC;

                // Check if the grid is owned by a Player.
                case "player":
                case "players":
                    return gridOwnerType == Utils.Ownership.OwnerType.Player;

                // Check if the grid is owned by Nobody.
                case "nobody":
                    return gridOwnerType == Utils.Ownership.OwnerType.Nobody;
            }

            // In all other cases, just return false.
            return false;
        }

        [Condition("blocksgreaterthan", helpText: "Ищет гриды, в которых блоков больше указанного числа.")]
        public static bool BlocksGreaterThan(MyCubeGrid grid, int count)
        {
            return grid.BlocksCount > count;
        }

        [Condition("haspower", "nopower", "Ищет гриды с питанием или без него.")]
        public static bool HasPower(MyCubeGrid grid)
        {
            foreach (var b in grid.GetFatBlocks())
            {
                var c = b.Components?.Get<MyResourceSourceComponent>();
                if (c == null)
                    continue;

                //some sources don't have electricity and Keen apparently doesn't know what TryGetValue is
                if (!c.ResourceTypes.Contains(MyResourceDistributorComponent.ElectricityId))
                    continue;

                if (c.HasCapacityRemainingByType(MyResourceDistributorComponent.ElectricityId) && c.ProductionEnabledByType(MyResourceDistributorComponent.ElectricityId))
                    return true;
            }

            return false;
        }

        [Condition("insideplanet", helpText: "Ищет гриды, застрявшие внутри планет.")]
        public static bool InsidePlanet(MyCubeGrid grid)
        {
            var s = grid.PositionComp.WorldVolume;
            var voxels = new List<MyVoxelBase>();
            MyGamePruningStructure.GetAllVoxelMapsInSphere(ref s, voxels);

            if (!voxels.Any())
                return false;

            foreach (var v in voxels)
            {
                var planet = v as MyPlanet;
                if (planet == null)
                    continue;

                var dist2center = Vector3D.DistanceSquared(s.Center, planet.PositionComp.WorldVolume.Center);
                if (dist2center <= (planet.MaximumRadius * planet.MaximumRadius) / 2)
                    return true;
            }

            return false;
        }

        [Condition("playerdistancelessthan", "playerdistancegreaterthan", "Ищет гриды дальше или ближе указанного расстояния от игроков.")]
        public static bool PlayerDistanceLessThan(MyCubeGrid grid, double dist)
        {
            dist *= dist;
            foreach (var player in MySession.Static.Players.GetOnlinePlayers())
            {
                if (Vector3D.DistanceSquared(player.GetPosition(), grid.PositionComp.GetPosition()) < dist && IsLivingHuman(player))
                    return true;
            }
            return false;
        }

        // GetOnlinePlayers includes bots, and GetPosition is zero for a player who controls nothing.
        private static bool IsLivingHuman(MyPlayer player)
        {
            if (!player.IsRealPlayer || player.Controller?.ControlledEntity?.Entity == null)
                return false;

            var character = player.Identity?.Character;
            return character != null && !character.IsDead;
        }

        [Condition("poweredgriddistancegreaterthan", helpText: "Ищет гриды дальше указанного расстояния от других гридов с питанием.")]
        public static bool PoweredGridDistanceGreaterThan(MyCubeGrid grid, double dist)
        {
            // Returns 'true' if the count of matched grids from a list of all grids is zero for these conditions:
            //        the distance from grid to entity is less than dist
            //        the grid is powered
            // Otherwise, returns 'false'
            dist *= dist;
            return MyEntities.GetEntities().Where(
                x => 
                (VRageMath.Vector3.DistanceSquared(x.PositionComp.GetPosition(), grid.PositionComp.GetPosition()) < dist 
                    && (x.GetType() == typeof(MyCubeGrid))                
                )).Cast<MyCubeGrid>().Where(y => !y.EntityId.Equals(grid.EntityId) && IsPowered(y))
            .Count() == 0;
        }

        // MyCubeGrid exposes no IsPowered member; power state lives on the grid's
        // resource distributor, which is null for grids with no power systems.
        private static bool IsPowered(MyCubeGrid grid)
        {
            var distributor = grid?.GridSystems?.ResourceDistributor;
            if (distributor == null)
                return false;

            var state = distributor.ResourceState;
            return state != VRage.MyResourceStateEnum.NoPower
                && state != VRage.MyResourceStateEnum.Disconnected;
        }

        [Condition("centerdistancelessthan", "centerdistancegreaterthan", "Ищет гриды дальше или ближе указанного расстояния от центра.")]
        public static bool CenterDistanceLessThan(MyCubeGrid grid, double dist)
        {
            dist *= dist;
            
            return Vector3D.DistanceSquared(Vector3D.Zero, grid.PositionComp.GetPosition()) < dist;
        }

        [Condition("ownedby", helpText: "Ищет гриды указанного владельца. Можно указать ник, ID личности, 'nobody' или 'pirates'.")]
        public static bool OwnedBy(MyCubeGrid grid, OwnerFilter owner)
        {
            return owner.Matches(grid);
        }

        /// <summary>
        /// The owner is resolved once per scan: looking up an identity walks through all of them.
        /// </summary>
        public sealed class OwnerFilter
        {
            private readonly Func<MyCubeGrid, bool> _matches;

            public OwnerFilter(string str)
            {
                _matches = Resolve(str);
            }

            public bool Matches(MyCubeGrid grid)
            {
                return _matches(grid);
            }

            private static Func<MyCubeGrid, bool> Resolve(string str)
            {
                long identityId;

                if (string.Compare(str, "nobody", StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    return grid => grid.BigOwners.Count == 0;
                }

                if (string.Compare(str, "npc", StringComparison.Ordinal) == 0)
                {
                    return grid => grid.BigOwners.Count > 0 &&
                                   MySession.Static.Factions.IsNpcFaction(grid.BigOwners.FirstOrDefault());
                }

                if (string.Compare(str, "pirates", StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    identityId = MyPirateAntennas.GetPiratesId();
                    var pirateFaction = MySession.Static.Factions.GetPlayerFaction(identityId);
                    if (pirateFaction != null && pirateFaction.Members.Count > 1)
                    {
                        return grid => grid.BigOwners.Count > 0 &&
                                       pirateFaction.Members.ContainsKey(grid.BigOwners.FirstOrDefault());
                    }
                }
                else
                {
                    var player = Utilities.GetIdentityByNameOrIds(str);
                    if (player != null)
                        identityId = player.IdentityId;
                    else if (!long.TryParse(str, out identityId) || !MySession.Static.Players.IdentityIsNpc(identityId))
                        return grid => false;
                }

                return grid => grid.BigOwners.Contains(identityId);
            }
        }

        [Condition("ownerinactive", helpText: "Ищет гриды игроков, которые вместе с сокланами не заходили указанное число дней (1–36500). Грид остаётся, если хоть один его блок принадлежит недавно заходившему игроку. Гриды NPC и ботов не подходят. Гриды без владельца условие тоже пропускает: если указать его одно, удалятся и они. Указывайте его вместе с другими условиями, например hasgridtype ship или blockslessthan 100.")]
        public static bool OwnerInactive(MyCubeGrid grid, InactiveOwnerFilter filter)
        {
            return filter.Matches(grid);
        }

        /// <summary>
        /// Built once per scan: the cutoff is fixed and each owner is looked up once, whatever thread the grid lands on.
        /// </summary>
        public sealed class InactiveOwnerFilter
        {
            private enum OwnerState
            {
                NotHuman,
                Active,
                Inactive
            }

            private readonly DateTime _cutoff;
            private readonly ConcurrentDictionary<long, OwnerState> _states = new ConcurrentDictionary<long, OwnerState>();
            private readonly Func<long, OwnerState> _classify;

            public InactiveOwnerFilter(string days)
            {
                if (!int.TryParse(days, out var d) || d < 1 || d > 36500)
                    throw new ArgumentException("число дней должно быть от 1 до 36500");

                _cutoff = DateTime.Now - TimeSpan.FromDays(d);
                _classify = Classify;
            }

            public bool Matches(MyCubeGrid grid)
            {
                // A grid without owners of its own (wheel, rotor or piston head) gets the parent's BigOwners on the
                // game thread only, so it leaves the decision to the owned grids of its group.
                var smallOwners = grid.SmallOwners;
                if (smallOwners.Count == 0)
                    return true;

                // Empty when none of the owned blocks is functional; a subgrid gets the parent's owners instead,
                // but on the game thread only.
                var bigOwners = grid.BigOwners;
                if (bigOwners.Count == 0)
                    return false;

                // The main owners must all be inactive players, and an active player keeps the grid even with
                // a single block. NPC and bot blocks among the rest don't count.
                return bigOwners.All(o => GetState(o) == OwnerState.Inactive)
                       && smallOwners.All(o => GetState(o) != OwnerState.Active);
            }

            private OwnerState GetState(long identityId)
            {
                return _states.GetOrAdd(identityId, _classify);
            }

            // A player is inactive when neither they nor any player of their faction has been seen since the cutoff.
            private OwnerState Classify(long identityId)
            {
                if (!Utilities.TryGetHumanPlayerId(identityId, out _))
                    return OwnerState.NotHuman;

                if (!IsAbsent(identityId))
                    return OwnerState.Active;

                var faction = MySession.Static.Factions.GetPlayerFaction(identityId);
                var mateSeen = faction != null && faction.Members.Keys.Any(id =>
                    id != identityId && Utilities.TryGetHumanPlayerId(id, out _) && !IsAbsent(id));

                return mateSeen ? OwnerState.Active : OwnerState.Inactive;
            }

            // Expects a human player. A player whose identity can't be found counts as seen, so their grids stay.
            private bool IsAbsent(long identityId)
            {
                var players = MySession.Static.Players;
                if (players.IsPlayerOnline(identityId))
                    return false;

                var identity = players.TryGetIdentity(identityId);
                return identity != null && Utilities.GetLastSeen(identity) < _cutoff;
            }
        }

        [Condition("hastype", "notype", "Ищет гриды с блоками указанного типа.")]
        public static bool BlockType(MyCubeGrid grid, string str)
        {
            return grid.HasBlockType(str);
        }

        [Condition("hastype-fast", "notype-fast", "Ищет гриды с блоками указанного типа.")]
        public static bool BlockTypeFast(MyCubeGrid grid, string str)
        {
            return grid.HasBlockTypeFast(str);
        }
        
        [Condition("hassubtype", "nosubtype", "Ищет гриды с блоками указанного подтипа.")]
        public static bool BlockSubType(MyCubeGrid grid, string str)
        {
            return grid.HasBlockSubtype(str);
        }
        
        [Condition("hassubtype-fast", "nosubtype-fast", "Ищет гриды с блоками указанного подтипа.")]
        public static bool BlockSubTypeFast(MyCubeGrid grid, string str)
        {
            return grid.HasBlockSubtypeFast(str);
        }

        [Condition("haspilot", helpText: "Ищет гриды с пилотами.")]
        public static bool Piloted(MyCubeGrid grid)
        {
            return grid.GetFatBlocks().OfType<MyShipController>().Any(b => b.Pilot != null);
        }
    }
}