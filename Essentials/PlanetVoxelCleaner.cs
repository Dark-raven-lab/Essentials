using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Essentials.Utils;
using VRage;
using VRage.Game.Entity;
using VRage.Voxels;
using VRageMath;

namespace Essentials
{
    /// <summary>
    /// Finds the changed parts of the planets' voxel storages and reverts those with no grid or character nearby.
    /// Connected clients revert the same cubes through the game's own voxel operation event.
    /// </summary>
    public static class PlanetVoxelCleaner
    {
        public class PlanetInfo
        {
            public string Name;
            public long ChangedCells;
            public long ProtectedCells;
            public long CleanedCells;
            public string Error;

            public long CleanableCells => ChangedCells - ProtectedCells;
            public long CleanableVoxels => CleanableCells * CellVoxels;
        }

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private const long CellVoxels = MyOctreeStorage.LeafSizeInVoxels * MyOctreeStorage.LeafSizeInVoxels * MyOctreeStorage.LeafSizeInVoxels;

        //cells are reverted in aligned cubes of 128 voxels: one delete and one notification each, and the storage collapses back into one leaf
        private const int RangeLod = 3;

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo ContentLeavesField = typeof(MyOctreeStorage).GetField("m_contentLeaves", Private);
        private static readonly FieldInfo MaterialLeavesField = typeof(MyOctreeStorage).GetField("m_materialLeaves", Private);
        private static readonly FieldInfo ContentNodesField = typeof(MyOctreeStorage).GetField("m_contentNodes", Private);
        private static readonly FieldInfo MaterialNodesField = typeof(MyOctreeStorage).GetField("m_materialNodes", Private);
        private static readonly FieldInfo StorageLockField = typeof(MyStorageBase).GetField("StorageLock", Private);
        private static readonly FieldInfo ChildMaskField = typeof(MyOctreeStorage).Assembly.GetType("Sandbox.Engine.Voxels.MyOctreeNode")?.GetField("ChildMask");
        private static readonly MethodInfo VoxelOperationSphereMethod = typeof(MyVoxelBase).GetMethod("PerformVoxelOperationSphere_Implementation", Private);

        public static bool IsSupported => ContentLeavesField != null && MaterialLeavesField != null && ContentNodesField != null
            && MaterialNodesField != null && StorageLockField != null && ChildMaskField != null && VoxelOperationSphereMethod != null;

        /// <param name="distance">Cubes with a grid or character closer than this are protected.</param>
        /// <param name="maxCells">Stops cleaning all planets after this many cells of 16 voxels, 0 for no limit. The last cube can go over it. The next run goes on with the rest.</param>
        /// <param name="clean">False only counts the cells.</param>
        public static List<PlanetInfo> Run(double distance, int maxCells, bool clean)
        {
            var result = new List<PlanetInfo>();
            var cellsLeft = maxCells > 0 ? maxCells : long.MaxValue;
            var nearEntities = new List<MyEntity>();

            foreach (var planet in MyEntities.GetEntities().OfType<MyPlanet>().OrderBy(p => p.StorageName).ToList())
            {
                var storage = planet.Storage as MyOctreeStorage;
                if (storage?.DataProvider == null)
                    continue;

                var info = new PlanetInfo { Name = planet.StorageName };

                //one planet failing can't lose the report of what was already reverted, or stop the others
                try
                {
                    ProcessPlanet(planet, storage, distance, clean, ref cellsLeft, nearEntities, info);
                }
                catch (Exception e)
                {
                    info.Error = e.Message;
                    Log.Error(e, $"Planet voxel cleaner failed on {info.Name}");
                }

                result.Add(info);
            }

            return result;
        }

        private static void ProcessPlanet(MyPlanet planet, MyOctreeStorage storage, double distance, bool clean, ref long cellsLeft, List<MyEntity> nearEntities, PlanetInfo info)
        {
            var toClean = new List<KeyValuePair<ulong, long>>();

            //the last changes are still in the write cache and would get into the save later
            if (storage.CachedWrites)
                storage.WritePending(true);

            foreach (var range in GetChangedRanges(storage))
            {
                info.ChangedCells += range.Value;

                if (HasEntityNearby(planet, Unpack(range.Key), distance, nearEntities))
                {
                    info.ProtectedCells += range.Value;
                    continue;
                }

                if (clean && cellsLeft > 0)
                {
                    toClean.Add(range);
                    cellsLeft -= range.Value;
                }
            }

            if (toClean.Count == 0)
                return;

            //what the game does before any voxel operation
            planet.BeforeContentChanged = true;

            foreach (var range in toClean)
            {
                Revert(planet, Unpack(range.Key));
                info.CleanedCells += range.Value;
            }
        }

        /// <returns>The packed coordinates of the aligned cubes to revert, with the number of changed cells in each.</returns>
        private static Dictionary<ulong, long> GetChangedRanges(MyOctreeStorage storage)
        {
            var cells = new HashSet<ulong>();

            //the storage writes its cached chunks on other threads
            using (((FastResourceLock)StorageLockField.GetValue(storage)).AcquireSharedUsing())
            {
                AddChangedCells((Dictionary<ulong, IMyOctreeLeafNode>)ContentLeavesField.GetValue(storage), (IDictionary)ContentNodesField.GetValue(storage), cells);
                AddChangedCells((Dictionary<ulong, IMyOctreeLeafNode>)MaterialLeavesField.GetValue(storage), (IDictionary)MaterialNodesField.GetValue(storage), cells);
            }

            var ranges = new Dictionary<ulong, long>();
            foreach (var key in cells)
            {
                var cell = Unpack(key);

                //a part bigger than a leaf only comes back when the delete collapses its parent, it has to take the whole parent
                var lod = Math.Max(RangeLod, cell.Lod > 0 ? cell.Lod + 1 : 0);

                //nothing above the root to collapse it into
                if (CubeSize(lod) >= storage.Size.X)
                    continue;

                var rangeKey = new MyCellCoord(lod, CubeMin(cell) >> (lod + MyOctreeStorage.LeafLodCount)).PackId64();
                ranges.TryGetValue(rangeKey, out var count);
                ranges[rangeKey] = count + (1L << (3 * cell.Lod));
            }

            return MergeNestedRanges(ranges);
        }

        //Written voxels that turned out uniform are not stored at all: the leaf is removed and the node has no child there.
        //The other children are leaves: an untouched part is a read-only one, that takes its data from the planet's generator.
        private static void AddChangedCells(Dictionary<ulong, IMyOctreeLeafNode> leaves, IDictionary nodes, HashSet<ulong> cells)
        {
            foreach (var leaf in leaves)
            {
                if (!leaf.Value.ReadOnly)
                    cells.Add(leaf.Key);
            }

            foreach (DictionaryEntry node in nodes)
            {
                var childMask = (byte)ChildMaskField.GetValue(node.Value);
                if (childMask == byte.MaxValue)
                    continue;

                var parent = Unpack((ulong)node.Key);
                for (int i = 0; i < 8; i++)
                {
                    if ((childMask & (1 << i)) != 0)
                        continue;

                    var child = new MyCellCoord(parent.Lod, (parent.CoordInLod << 1) + new Vector3I(i & 1, (i >> 1) & 1, (i >> 2) & 1)).PackId64();
                    if (!leaves.ContainsKey(child))
                        cells.Add(child);
                }
            }
        }

        private static Dictionary<ulong, long> MergeNestedRanges(Dictionary<ulong, long> ranges)
        {
            var merged = new Dictionary<ulong, long>();
            if (ranges.Count == 0)
                return merged;

            var maxLod = ranges.Keys.Max(k => Unpack(k).Lod);
            foreach (var range in ranges.OrderByDescending(r => Unpack(r.Key).Lod))
            {
                var cube = Unpack(range.Key);
                var added = false;

                for (var lod = cube.Lod + 1; lod <= maxLod && !added; lod++)
                {
                    var outer = new MyCellCoord(lod, cube.CoordInLod >> (lod - cube.Lod)).PackId64();
                    if (merged.ContainsKey(outer))
                    {
                        merged[outer] += range.Value;
                        added = true;
                    }
                }

                if (!added)
                    merged.Add(range.Key, range.Value);
            }

            return merged;
        }

        private static bool HasEntityNearby(MyPlanet planet, MyCellCoord cube, double distance, List<MyEntity> nearEntities)
        {
            var size = CubeSize(cube.Lod);
            var center = CubeMin(cube) + new Vector3I(size / 2);
            MyVoxelCoordSystems.VoxelCoordToWorldPosition(planet.PositionLeftBottomCorner, ref center, out Vector3D worldCenter);

            //half of the cube's diagonal, so the distance is counted from the edge of the cube and not from its center
            var sphere = new BoundingSphereD(worldCenter, distance + size * Math.Sqrt(3) / 2);

            nearEntities.Clear();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, nearEntities);
            return nearEntities.Any(e => e is MyCubeGrid || e is MyCharacter);
        }

        private static void Revert(MyPlanet planet, MyCellCoord cube)
        {
            var min = CubeMin(cube);

            //not the last voxel of the cube: for the leaves the end of the range is exclusive
            var size = CubeSize(cube.Lod);
            var max = min + new Vector3I(size);

            planet.Storage.DeleteRange(MyStorageDataTypeFlags.ContentAndMaterial, min, max, true);

            //A client reverts the bounding box of the sphere, its floor and ceiling less one voxel at the top.
            //Half a voxel of offset makes that the same range as above.
            var center = planet.PositionLeftBottomCorner + min + new Vector3D(size / 2 + 0.5);
            Events.RaiseEvent<MyVoxelBase, Vector3D, float, byte, MyVoxelBase.OperationType>(planet.RootVoxel, VoxelOperationSphereMethod, center, size / 2, 0, MyVoxelBase.OperationType.Revert);
        }

        //new MyCellCoord(ulong) reads the 32 bit format, the keys of the storage are 64 bit
        private static MyCellCoord Unpack(ulong key)
        {
            return new MyCellCoord(MyCellCoord.UnpackLod(key), MyCellCoord.UnpackCoord(key));
        }

        private static int CubeSize(int lod)
        {
            return MyOctreeStorage.LeafSizeInVoxels << lod;
        }

        private static Vector3I CubeMin(MyCellCoord cube)
        {
            return cube.CoordInLod << (cube.Lod + MyOctreeStorage.LeafLodCount);
        }
    }
}
