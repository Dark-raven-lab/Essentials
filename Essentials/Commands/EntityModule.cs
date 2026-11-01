using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Utils;
using Torch.API.Managers;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using VRage.Network;
using VRage.Replication;
using Sandbox.Game;
using VRage.Game.ModAPI.Interfaces;

namespace Essentials
{
    [Category("entities")]
    public class EntityModule : CommandModule
    {
#pragma warning disable 649
        [ReflectedGetter(Name = "m_clientStates")]
        private static Func<MyReplicationServer, IDictionary> _clientStates;

        private const string CLIENT_DATA_TYPE_NAME = "VRage.Network.MyClient, VRage";
        [ReflectedGetter(TypeName = CLIENT_DATA_TYPE_NAME, Name = "Replicables")]
        private static Func<object, MyConcurrentDictionary<IMyReplicable, MyReplicableClientData>> _replicables;

        [ReflectedMethod(Name = "RemoveForClient", OverrideTypeNames = new[] { null, CLIENT_DATA_TYPE_NAME, null })]
        private static Action<MyReplicationServer, IMyReplicable, object, bool> _removeForClient;

        [ReflectedMethod(Name = "ForceReplicable")]
        private static Action<MyReplicationServer, IMyReplicable, Endpoint> _forceReplicable;
#pragma warning restore 649

        private static Dictionary<ulong, DateTime> _commandtimeout = new Dictionary<ulong, DateTime>();

        [Command("refresh", "Заново синхронизирует все сущности для игрока, вызвавшего команду.")]
        [Permission(MyPromoteLevel.None)]
        public void Refresh()
        {
            if (Context.Player == null)
                return;

            var steamid = Context.Player.SteamUserId;
            if (_commandtimeout.TryGetValue(steamid, out DateTime lastcommand))
            {
                TimeSpan difference = DateTime.Now - lastcommand;
                if (difference.TotalMinutes < 1)
                {
                    Context.Respond($"Перезарядка: команду можно использовать снова через {difference.TotalSeconds:N0} сек.");
                    return;
                }
                else
                {
                    _commandtimeout[steamid] = DateTime.Now;
                }
            }
            else
            {
                _commandtimeout.Add(steamid, DateTime.Now);
            }

            var playerEndpoint = new Endpoint(Context.Player.SteamUserId, 0);
            var replicationServer = (MyReplicationServer)MyMultiplayer.ReplicationLayer;
            var clientDataDict = _clientStates.Invoke(replicationServer);
            object clientData;
            try
            {
                clientData = clientDataDict[playerEndpoint];
            }
            catch
            {
                return;
            }

            var clientReplicables = _replicables.Invoke(clientData);

            var replicableList = new List<IMyReplicable>(clientReplicables.Count);
            foreach (var pair in clientReplicables)
                replicableList.Add(pair.Key);

            foreach (var replicable in replicableList)
            {
                _removeForClient.Invoke(replicationServer, replicable, clientData, true);
                _forceReplicable.Invoke(replicationServer, replicable, playerEndpoint);
            }

            Context.Respond($"Принудительно синхронизировано сущностей: {replicableList.Count}.");
        }

        [Command("stop", "Останавливает сущность")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Stop(string entityName)
        {
            if (!Utilities.TryGetEntityByNameOrId(entityName, out IMyEntity entity))
            {
                Context.Respond($"Сущность '{entityName}' не найдена.");
                return;
            }

            entity.Physics?.ClearSpeed();
            Context.Respond($"Сущность '{entity.DisplayName}' остановлена");
        }

        [Command("delete", "Удаляет сущность.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Delete(string entityName)
        {
            var name = entityName;
            if (string.IsNullOrEmpty(name))
                return;

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Сущность '{name}' не найдена.");
                return;
            }

            if (entity is IMyCharacter)
            {
                Context.Respond("Нельзя удалять персонажей.");
                return;
            }

            entity.Close();
            Context.Respond($"Сущность '{entity.DisplayName}' удалена");
        }

        [Command("kill", "Убивает игрока.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Kill(string playerName)
        {
            /* 
             * First we try killing the player when hes online. This is easy and fast 
             * and can also kill the player while being seated. 
             */
            var player = Utilities.GetPlayerByNameOrId(playerName);
            if (player != null) 
            {
                MyVisualScriptLogicProvider.SetPlayersHealth(player.IdentityId, 0);

                Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf
                    ($"{player.DisplayName} убит администратором");

                return;
            }

            /* 
             * If we could not find the player there is a chance he is offline, in that case we try inflicting
             * damage to the character as the VST will not help us with offline characters. 
             */
            if (!Utilities.TryGetEntityByNameOrId(playerName, out IMyEntity entity)) {
                Context.Respond($"Сущность '{playerName}' не найдена.");
                return;
            }

            if (entity is IMyCharacter) 
            {
                var destroyable = entity as IMyDestroyableObject;

                destroyable.DoDamage(1000f, MyDamageType.Radioactivity, true);

                Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf
                    ($"{entity.DisplayName} убит администратором");
            }
        }

        [Command("find", "Ищет сущности с указанным текстом в названии.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            var sb = new StringBuilder("Найденные сущности:\n");
            foreach (var entity in MyEntities.GetEntities())
            {
                if (entity is IMyVoxelBase voxel && (voxel.StorageName?.Contains(name, StringComparison.CurrentCultureIgnoreCase) ?? false))
                    sb.AppendLine($"{voxel.StorageName} ({entity.EntityId})");
                else if (entity?.DisplayName?.Contains(name, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    //This can be null??? :keen:
                    sb.AppendLine($"{entity.DisplayName} ({entity.EntityId})");
            }

            Context.Respond(sb.ToString());
        }

        [Command("poweroff", "Выключает питание сущностей с указанным текстом в названии.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void PowerOff(string name)
        {

            if (string.IsNullOrEmpty(name))
                return;

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Сущность '{name}' не найдена.");
                return;
            }

            if (entity is IMyCharacter)
            {
                Context.Respond("Команда не работает на персонажах.");
                return;
            }
            IMyCubeGrid grid = entity as MyCubeGrid;
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, f => f.FatBlock != null && f.FatBlock is IMyFunctionalBlock
            && (f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_Reactor) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_BatteryBlock) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_SolarPanel)));
            var list = blocks.Select(f => (IMyFunctionalBlock)f.FatBlock).Where(f => f.Enabled).ToArray();
            foreach (var item in list)
            {
                item.Enabled = false;
            }
            Context.Respond($"Питание сущности '{entity.DisplayName}' выключено");

        }

        [Command("poweron", "Включает питание сущностей с указанным текстом в названии.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void PowerOn(string name)
        {

            if (string.IsNullOrEmpty(name))
                return;

            if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity entity))
            {
                Context.Respond($"Сущность '{name}' не найдена.");
                return;
            }

            if (entity is IMyCharacter)
            {
                Context.Respond("Команда не работает на персонажах.");
                return;
            }
            IMyCubeGrid grid = entity as MyCubeGrid;
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, f => f.FatBlock != null && f.FatBlock is IMyFunctionalBlock
            && (f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_Reactor) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_BatteryBlock) ||
            f.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_SolarPanel)));
            var list = blocks.Select(f => (IMyFunctionalBlock)f.FatBlock).Where(f => !f.Enabled).ToArray();
            foreach (var item in list)
            {
                item.Enabled = true;
            }
            Context.Respond($"Питание сущности '{entity.DisplayName}' включено");

        }

        [Command("eject", "Высаживает игрока из блока, в котором он сидит, или всех игроков сервера, если указать 'all'")]
        public void Eject(string playerName) {

            if (playerName.ToLower() == "all") 
            {
                EjectAllPlayers();
            }
            else 
            {
                EjectSinglePlayer(playerName);
            }
        }

        private void EjectAllPlayers() {

            int ejectedPlayersCount = 0;

            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().ToList()) 
            {
                foreach (var controller in grid.GetFatBlocks<MyShipController>()) 
                {
                    if (controller.Pilot != null) 
                    {
                        controller.Use();
                        ejectedPlayersCount++;
                    }
                }
            }

            Context.Respond($"Высажено игроков: {ejectedPlayersCount}.");
        }

        private void EjectSinglePlayer(string playerName) {

            /* We check first if the player is among the online players before looping over all grids for nothing. */
            var player = Utilities.GetPlayerByNameOrId(playerName);
            if (player != null) 
            {
                /* If he is online we check if he is currently seated. If he is eject him. */
                if (player?.Controller.ControlledEntity is MyCockpit controller) 
                {
                    controller.Use();
                    Context.Respond($"Игрок '{playerName}' высажен.");
                } 
                else 
                {
                    Context.Respond("Игрок никуда не сел.");
                }

                return;
            }

            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().ToList()) 
            {
                foreach (var controller in grid.GetFatBlocks<MyShipController>()) 
                {
                    var pilot = controller.Pilot;

                    if (pilot != null && pilot.DisplayName == playerName) 
                    {
                        controller.Use();

                        Context.Respond($"Игрок '{playerName}' высажен.");

                        /* We found our player. so no need to continue looking */
                        return;
                    }
                }
            }

            Context.Respond("Офлайн-игрок не найден или никуда не сел.");
        }
    }
}
