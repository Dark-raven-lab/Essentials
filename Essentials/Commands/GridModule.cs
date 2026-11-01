using System.IO;
using System.Linq;
using System.Text;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;
using System.Collections.Concurrent;
using VRage.Groups;
using VRage.ObjectBuilders.Private;

namespace Essentials
{
    [Category("grids")]
    public class GridModule : CommandModule
    {
        [Command("setowner", "Передаёт грид указанному игроку или ID.", "Использование: setowner <grid> <newowner>")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void SetOwner(string gridName, string playerName)
        {
            var firstArg = Context.Args.FirstOrDefault();
            Utilities.TryGetEntityByNameOrId(gridName, out IMyEntity entity);

            if (!(entity is IMyCubeGrid grid))
            {
                Context.Respond($"Грид {gridName} не найден.");
                return;
            }

            var secondArg = Context.Args.ElementAtOrDefault(1);
            long identityId;
            if (!long.TryParse(playerName, out identityId))
            {
                var player = Context.Torch.CurrentSession?.Managers?.GetManager<IMultiplayerManagerBase>().GetPlayerByName(playerName);
                if (player == null)
                {
                    Context.Respond($"Игрок {playerName} не найден.");
                    return;
                }
                identityId = player.IdentityId;
            }

            grid.ChangeGridOwnership(identityId, MyOwnershipShareModeEnum.Faction);
            Context.Respond($"Грид {grid.DisplayName} передан владельцу {identityId}");

            /*
            grid.GetBlocks(new List<IMySlimBlock>(), block =>
            {
                var cubeBlock = block.FatBlock as MyCubeBlock;
                var ownerComp = cubeBlock?.Components.Get<MyEntityOwnershipComponent>();
                if (ownerComp == null)
                    return false;

                cubeBlock?.ChangeOwner(0, MyOwnershipShareModeEnum.All);
                cubeBlock?.ChangeOwner(identityId, ownerComp.ShareMode);
                return false;
            });*/
        }

        [Command("ejectall", "Высаживает всех игроков из указанного грида.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Eject(string gridName = null) 
        {
            ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> gridGroups;

            if (gridName == null) 
            {
                if(Context.Player == null) 
                {
                    Context.Respond("Из консоли нужно всегда указывать имя грида!");
                    return;
                }

                IMyCharacter character = Context.Player.Character;

                if (character == null) 
                {
                    Context.Respond("Без имени грида нужно находиться в персонаже!");
                    return;
                }

                gridGroups = GridFinder.FindLookAtGridGroupMechanical(character);

                if (gridGroups.Count == 0) 
                {
                    Context.Respond("В прямой видимости нет грида! Не используйте режим наблюдателя!");
                    return;
                }
            } 
            else 
            {
                gridGroups = GridFinder.FindGridGroupMechanical(gridName);

                if (gridGroups.Count == 0) 
                {
                    Context.Respond($"Грид с именем '{gridName}' не найден!");
                    return;
                }

                if (gridGroups.Count > 1) 
                {
                    Context.Respond($"Гридов с именем '{gridName}' несколько, поэтому во избежание ошибок команда не выполнена!");
                    return;
                }
            }

            var group = gridGroups.First();
            int ejectedPlayersCount = 0;

            foreach(var node in group.Nodes) 
            {
                MyCubeGrid grid = node.NodeData;

                foreach(var fatBlock in grid.GetFatBlocks()) 
                {
                    if (!(fatBlock is MyShipController shipController))
                        continue;

                    if (shipController.Pilot != null) 
                    {
                        shipController.Use();
                        ejectedPlayersCount++;
                    }
                }
            }

            Context.Respond($"Высажено игроков: {ejectedPlayersCount}.");
        }

        [Command("static large", "Делает все большие гриды статичными.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void StaticLarge()
        {
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(g => g.GridSizeEnum == MyCubeSize.Large).Where(x => x.Projector == null))
                grid.OnConvertedToStationRequest(); //Keen why do you do this to me?
        }

        [Command("stopall", "Останавливает все движущиеся гриды.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void StopAll()
        {
                foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
                {
                    grid.Physics.ClearSpeed();
                }
        }

        [Command("list", "Показывает гриды, которыми вы владеете хотя бы на 50%. Координаты показываются, если админ сервера включил эту настройку.")]
        [Permission(MyPromoteLevel.None)]
        public void List()
        {
            var id = Context.Player?.IdentityId ?? 0;
            StringBuilder sb = new StringBuilder();

            foreach (var entity in MyEntities.GetEntities())
            {
                var grid = entity as MyCubeGrid;
                if (grid == null || grid.Projector != null)
                    continue;

                if (grid.BigOwners.Contains(id))
                {

                    sb.AppendLine($"{grid.DisplayName} - {grid.GridSizeEnum} - блоков: {grid.BlocksCount} - Координаты: {(EssentialsPlugin.Instance.Config.UtilityShowPosition ? grid.PositionComp.GetPosition().ToString() : "неизвестно")}");
                    if (EssentialsPlugin.Instance.Config.MarkerShowPosition)
                    {
                        var gridGPS = MyAPIGateway.Session?.GPS.Create(grid.DisplayName, ($"{grid.DisplayName} - {grid.GridSizeEnum} - блоков: {grid.BlocksCount}"), grid.PositionComp.GetPosition(), true);

                        MyAPIGateway.Session?.GPS.AddGps(Context.Player.IdentityId, gridGPS);
                    }
                }
            }

            ModCommunication.SendMessageTo(new DialogMessage("Список гридов", $"Корабли и станции игрока {Context.Player.DisplayName}", sb.ToString()), Context.Player.SteamUserId);
        }

        private readonly string ExportPath = "ExportedGrids\\{0}.xml";

        [Command("export", "Сохраняет указанный грид в файл с указанным именем.")]
        public void Export(string gridName, string exportName)
        {
            Directory.CreateDirectory("ExportedGrids");
            if (!Utilities.TryGetEntityByNameOrId(gridName, out var ent) || !(ent is IMyCubeGrid))
            {
                Context.Respond("Грид не найден.");
                return;
            }

            var path = string.Format(ExportPath, exportName);
            if (File.Exists(path))
            {
                Context.Respond("Файл экспорта уже существует.");
                return;
            }

            MyObjectBuilderSerializerKeen.SerializeXML(path, false, ent.GetObjectBuilder());
            Context.Respond($"Грид сохранён в {path}");
        }
        
        [Command("import", "Загружает грид из файла и создаёт его рядом с указанной сущностью или игроком.")]
        public void Import(string gridName, string targetName = null)
        {
            Directory.CreateDirectory("ExportedGrids");
            if (targetName == null)
            {
                if (Context.Player == null)
                {
                    Context.Respond("Нужно указать целевую сущность.");
                    return;   
                }

                targetName = Context.Player.Controller.ControlledEntity.Entity.DisplayName;
            }

            if (!Utilities.TryGetEntityByNameOrId(targetName, out var ent))
            {
                Context.Respond("Целевая сущность не найдена.");
                return;
            }
            
            var path = string.Format(ExportPath, gridName);
            if (!File.Exists(path))
            {
                Context.Respond("Файл не существует.");
                return;
            }

            if (MyObjectBuilderSerializerKeen.DeserializeXML(path, out MyObjectBuilder_CubeGrid grid))
            {
                Context.Respond($"Загрузка грида из {path}");
                MyEntities.RemapObjectBuilder(grid);
                var pos = MyEntities.FindFreePlace(ent.GetPosition(), grid.CalculateBoundingSphere().Radius);
                if (pos == null)
                {
                    Context.Respond("Нет свободного места.");
                    return;
                }

                var x = grid.PositionAndOrientation ?? new MyPositionAndOrientation();
                x.Position = pos.Value;
                grid.PositionAndOrientation = x;
                MyEntities.CreateFromObjectBuilderParallel(grid, true);
            }
        }
    }
}
