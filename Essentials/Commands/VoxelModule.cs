using Essentials.Utils;
using NLog;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Game.World.Generator;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Network;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using static Sandbox.Game.Entities.MyVoxelBase;
using Parallel = ParallelTasks.Parallel;

namespace Essentials.Commands
{
    [Category("voxels")]
    public class VoxelModule : CommandModule
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        

        [ReflectedGetter(Name = "m_asteroidsModule")]
        private static Func<MyProceduralWorldGenerator, MyProceduralAsteroidCellGenerator> _asteroidGenerator;
        [ReflectedSetter(Name = "m_isClosingEntities")]
        private static Action<MyProceduralAsteroidCellGenerator, bool> _deletingSet;

        private static MyProceduralAsteroidCellGenerator _generatorInstance;
        private static MyProceduralAsteroidCellGenerator GeneratorInstance => _generatorInstance ?? (_generatorInstance = _asteroidGenerator(MyProceduralWorldGenerator.Static));

        [Command("help", "Показывает все команды вокселей.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Help()
        {
            var commands = EssentialsPlugin.Instance.CommandManager.Commands;
            commands.GetNode(new List<string> { "voxels" }, out var voxels);

            var sb = new StringBuilder();
            foreach (var command in commands.WalkTree(voxels).Where(n => n.IsCommand).Select(n => n.Command).OrderBy(c => c.SyntaxHelp))
            {
                sb.AppendLine(command.SyntaxHelp);
                sb.AppendLine($"   {command.HelpText}");
            }

            Context.Respond(sb.ToString());
        }

        [Command("reset all", "Сбрасывает все воксельные карты.")]
        public void ResetAll(bool deleteStorage = false)
        {
            var voxelMaps = MyEntities.GetEntities().OfType<MyVoxelBase>();

            var resetIds = new List<long>();
            int deleted = 0;

            foreach (var map in voxelMaps)
            {
                try
                {
                    if (map.StorageName == null || map.Storage.DataProvider == null)
                        continue;

                    long id = map.EntityId;

                    if (deleteStorage && map is MyVoxelMap)
                    {
                        using (PinDelete())
                            map.Close();
                        deleted++;
                    }
                    else
                    {
                        map.Storage.Reset(MyStorageDataTypeFlags.All);
                        resetIds.Add(id);
                    }
                }
                catch (Exception e)
                {
                    _log.Error($"{e.Message}\n{e.StackTrace}");
                }
            }

            ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

            Context.Respond($"Сброшено воксельных карт: {resetIds.Count}, удалено: {deleted}.");
        }

        [Command("cleanup asteroids", "Сбрасывает все астероиды, рядом с которыми нет гридов и игроков.")]
        public void CleanupAsteroids(bool deleteStorage = false)
        {
            var voxelMaps = MyEntities.GetEntities().OfType<MyVoxelMap>();

            var resetIds = new List<long>();
            int deleted = 0;

            foreach (var map in voxelMaps)
            {
                try
                {
                    if (map.StorageName == null || map.Storage.DataProvider == null)
                        continue;

                    var s = map.PositionComp.WorldVolume;
                    var nearEntities = new List<MyEntity>();

                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref s, nearEntities);

                    if (nearEntities.Any(e => e is MyCubeGrid || e is MyCharacter))
                        continue;

                    long id = map.EntityId;

                    if (deleteStorage)
                    {
                        using (PinDelete())
                            map.Close();
                        deleted++;
                    }
                    else
                    {
                        map.Storage.Reset(MyStorageDataTypeFlags.All);
                        resetIds.Add(id);
                    }
                }
                catch (Exception e)
                {
                    _log.Error($"{e.Message}\n{e.StackTrace}");
                }
            }

            ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

            Context.Respond($"Сброшено воксельных карт: {resetIds.Count}, удалено: {deleted}.");
        }

        [Command("cleanup distant", "Сбрасывает все астероиды, в указанном радиусе от которых нет гридов и игроков.")]
        public void CleanupAsteroidsDistant(double distance = 1000, bool deleteStorage = false)
        {

            var voxelMaps = MyEntities.GetEntities().OfType<MyVoxelMap>();

            var resetIds = new List<long>();
            int deleted = 0;

            foreach (var map in voxelMaps)
            {
                try
                {
                    if (map.StorageName == null || map.Storage.DataProvider == null)
                        continue;

                    var s = new BoundingSphereD(map.PositionComp.GetPosition(), distance);
                    var nearEntities = new List<MyEntity>();

                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref s, nearEntities);

                    if (nearEntities.Any(e => e is MyCubeGrid || e is MyCharacter))
                        continue;

                    long id = map.EntityId;

                    if (deleteStorage)
                    {
                        using (PinDelete())
                            map.Close();
                        deleted++;
                    }
                    else
                    {
                        map.Storage.Reset(MyStorageDataTypeFlags.All);
                        resetIds.Add(id);
                    }
                }
                catch (Exception e)
                {
                    _log.Error($"{e.Message}\n{e.StackTrace}");
                }
            }

            ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

            Context.Respond($"Сброшено воксельных карт: {resetIds.Count}, удалено: {deleted}.");
        }

        [Command("scan planets", "Показывает, сколько изменённых воксельных ячеек планет не имеют гридов и игроков в указанном радиусе.")]
        [Permission(MyPromoteLevel.Admin)]
        public void ScanPlanets(double distance = 1000)
        {
            RunPlanetCleaner(distance, 0, false);
        }

        [Command("cleanup planets", "Восстанавливает изменённые воксельные ячейки планет, в указанном радиусе от которых нет гридов и игроков.")]
        [Permission(MyPromoteLevel.Admin)]
        public void CleanupPlanets(double distance = 1000, int maxCells = 50000)
        {
            RunPlanetCleaner(distance, maxCells, true);
        }

        private void RunPlanetCleaner(double distance, int maxCells, bool clean)
        {
            //written this way for NaN, which would turn off the protection of grids and players
            if (!(distance >= 0) || double.IsInfinity(distance) || maxCells < 0)
            {
                Context.Respond("Расстояние должно быть неотрицательным числом, maxCells тоже не может быть отрицательным.");
                return;
            }

            if (!PlanetVoxelCleaner.IsSupported)
            {
                Context.Respond("Эта версия игры не поддерживается: не найдены поля хранилища вокселей. Ничего не изменено.");
                _log.Error("Planet voxel cleaner: voxel storage fields not found");
                return;
            }

            var planets = PlanetVoxelCleaner.Run(distance, maxCells, clean);

            var sb = new StringBuilder();
            foreach (var p in planets)
                sb.AppendLine($"{p.Name}: изменённых ячеек: {p.ChangedCells:#,##0}, защищено: {p.ProtectedCells:#,##0}, можно очистить: {p.CleanableCells:#,##0} (вокселей до {p.CleanableVoxels:#,##0}){(p.Error == null ? "" : $", ошибка после очистки ячеек {p.CleanedCells:#,##0}: {p.Error}")}");

            if (clean)
                sb.Append($"Очищено ячеек: {planets.Sum(p => p.CleanedCells):#,##0} из {planets.Sum(p => p.CleanableCells):#,##0}.");
            else
                sb.Append($"Всего можно очистить ячеек: {planets.Sum(p => p.CleanableCells):#,##0}.");

            var report = sb.ToString();
            Context.Respond(report);
            _log.Info($"Planet voxels, distance {distance}, maxCells {maxCells}:\n{report}");
        }

        [Command("reset planets", "Сбрасывает все планеты.")]
        public void ResetPlanets()
        {
            var voxelMaps = MyEntities.GetEntities().OfType<MyPlanet>();

            var resetIds = new List<long>();

            foreach (var map in voxelMaps)
            {
                try
                {
                    if (map.StorageName == null || map.Storage.DataProvider == null)
                        continue;

                    map.Storage.Reset(MyStorageDataTypeFlags.All);
                    resetIds.Add(map.EntityId);
                }
                catch (Exception e)
                {
                    _log.Error($"{e.Message}\n{e.StackTrace}");
                }
            }

            ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

            Context.Respond($"Сброшено воксельных карт: {resetIds.Count}.");
        }

        [Command("reset planet", "Сбрасывает планету с указанным названием.")]
        public void ResetPlanet(string planetName)
        {
            var maps = new List<MyPlanet>(2);
            foreach (MyPlanet map in MyEntities.GetEntities().OfType<MyPlanet>())
            {
                if (map.StorageName?.Contains(planetName, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    maps.Add(map);
            }

            switch (maps.Count)
            {
                case 0:
                    Context.Respond($"Планета с названием {planetName} не найдена");
                    return;
                case 1:
                    var map = maps[0];
                    map.Storage.Reset(MyStorageDataTypeFlags.All);
                    ModCommunication.SendMessageToClients(new VoxelResetMessage(new long[] { map.EntityId }));
                    Context.Respond($"Планета {map.Name} сброшена");
                    return;
                default:
                    Context.Respond($"Планет по запросу '{planetName}': {maps.Count}. Выберите из списка:");
                    Context.Respond(string.Join("\r\n", maps.Select(m => m.StorageName)));
                    return;
            }
        }


        [Command("reset area", "Восстанавливает воксели в указанном радиусе от игрока")]
        [Permission(MyPromoteLevel.Admin)]
        public void ResetVoxelArea(float Radius)
        {
            if (Context.Player == null)
            {
                Context.Respond("Команда доступна только в игре!");
                return;
            }


            if (Radius <= 0)
            {
                Context.Respond("Неверный радиус!");
                return;
            }


            if (ResetVoxelInArea(Context.Player.GetPosition(), Radius))
            {
                Context.Respond("Воксели восстановлены!");
            }
            else
            {
                Context.Respond("Не удалось восстановить воксели! Подробности в логе!");
            }


        }

        [Command("reset gps", "Восстанавливает воксели в указанном радиусе от заданной точки")]
        [Permission(MyPromoteLevel.Admin)]
        public void ResetVoxelArea(float X, float Y, float Z, float Radius)
        {
            Vector3D ResetTarget = new Vector3D(X, Y, Z);
            if (Radius <= 0)
            {
                Context.Respond("Неверный радиус!");
                return;
            }


            if (ResetVoxelInArea(ResetTarget, Radius))
            {
                Context.Respond("Воксели восстановлены!");
            }
            else
            {
                Context.Respond("Не удалось восстановить воксели! Подробности в логе!");
            }
        }

        public static bool ResetVoxelInArea(Vector3D Center, float Radius, bool UpdatePlayers = true)
        {

            try
            {
                BoundingSphereD Sphere = new BoundingSphereD(Center, Radius);
                List<MyVoxelBase> Maps = MyEntities.GetEntitiesInSphere(ref Sphere).OfType<MyVoxelBase>().ToList();
                if (Maps.Count == 0)
                    return true;

                var m = typeof(MyVoxelBase).GetMethod("PerformVoxelOperationSphere_Implementation", BindingFlags.Instance | BindingFlags.NonPublic);

                foreach (var voxelMap in Maps)
                {
                    if (voxelMap.MarkedForClose)
                        continue;



                    MyShapeSphere shape = new MyShapeSphere();
                    shape.Center = Center;
                    shape.Radius = Radius;

                    MyVoxelGenerator.RevertShape(voxelMap, shape);

                    if (UpdatePlayers)
                        Events.RaiseEvent<MyVoxelBase, Vector3D, float, byte, OperationType>(voxelMap.RootVoxel, m, shape.Center, shape.Radius, 0, MyVoxelBase.OperationType.Revert);

                }
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Voxel reset failed!");
                return false;
            }
        }





        private static LockToken PinDelete()
        {
            /* 
             * If SEWorldGenerator Plugin is used or Procedural Seed is 0 there 
             * will be no GeneratorInstance. In this case this Method will NRE.
             * 
             * However we still would like to delete stuff. So just return null. 
             * Because when there is no GeneratorInstance we dont have to tell it
             * entities are being deleted.
             */
            if (GeneratorInstance == null)
                return null;

            return new LockToken();
        }

        private class LockToken : IDisposable
        {
            public LockToken()
            {
                _deletingSet(GeneratorInstance, true);
            }

            public void Dispose()
            {
                _deletingSet(GeneratorInstance, false);
            }
        }
    }
}
