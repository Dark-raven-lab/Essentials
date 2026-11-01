using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using System;
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