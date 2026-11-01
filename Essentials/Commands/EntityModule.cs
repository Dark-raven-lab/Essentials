using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Utils;
using Torch.API.Managers;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Network;
using VRage.Replication;
using VRage.Utils;

namespace Essentials
{
    [Category("entities")]
    public class EntityModule : CommandModule
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

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

        private const string EntityHelp = "\nСущность указывается по точному имени или ID. Имена с пробелами берите в кавычки. Если под имя подходит несколько сущностей, команда ничего не делает и выводит их ID.";
        private const string PlayerHelp = "\nИгрок указывается по нику, SteamID, ID личности или ID персонажа. Ник с пробелами берите в кавычки.";

        private static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(1);

        private static Dictionary<ulong, DateTime> _commandtimeout = new Dictionary<ulong, DateTime>();

        private string Admin => Context.Player?.DisplayName ?? "console";

        [Command("refresh", "Заново синхронизирует все сущности для игрока, вызвавшего команду.", "Заново отправляет вам все сущности, загруженные вокруг вас. Помогает, если грид или персонаж отображаются неправильно. Доступна раз в минуту.")]
        [Permission(MyPromoteLevel.None)]
        public void Refresh()
        {
            if (Context.Player == null)
                return;

            var steamid = Context.Player.SteamUserId;
            if (_commandtimeout.TryGetValue(steamid, out DateTime lastcommand))
            {
                var left = RefreshCooldown - (DateTime.Now - lastcommand);
                if (left > TimeSpan.Zero)
                {
                    Context.Respond($"Перезарядка: команду можно использовать снова через {Math.Ceiling(left.TotalSeconds)} сек.");
                    return;
                }
            }

            var playerEndpoint = new Endpoint(Context.Player.SteamUserId, 0);
            var replicationServer = (MyReplicationServer)MyMultiplayer.ReplicationLayer;
            var clientData = _clientStates.Invoke(replicationServer)[playerEndpoint];
            if (clientData == null)
            {
                Context.Respond("Не удалось выполнить синхронизацию, попробуйте позже.");
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

            _commandtimeout[steamid] = DateTime.Now;
            Context.Respond($"Принудительно синхронизировано сущностей: {replicableList.Count}.");
        }

        [Command("stop", "Останавливает сущность и всё, что с ней физически связано.", "Обнуляет скорость грида вместе с физически связанными гридами, включая пристыкованные. Для сидящего игрока останавливает его корабль." + EntityHelp + "\nПример: !entities stop \"Мой корабль\"")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Stop(string entityName)
        {
            if (!TryFindOne(entityName, out var entity))
                return;

            var top = entity.GetTopMostParent();
            if (top.Physics == null)
            {
                Context.Respond($"Сущность '{top.DisplayName}' нельзя остановить: у неё нет физики.");
                return;
            }

            if (top is MyCubeGrid grid)
            {
                foreach (var connected in Utilities.GetConnectedGrids(grid, GridLinkTypeEnum.Physical))
                    connected.Physics?.ClearSpeed();
            }
            else
            {
                top.Physics.ClearSpeed();
            }

            Context.Respond($"Сущность '{top.DisplayName}' остановлена.");
            Log.Info($"{Admin} stopped {top.DisplayName} ({top.EntityId})");
        }

        [Command("delete", "Удаляет грид или плавающий объект.", "Удаляет грид вместе с тем, что связано с ним роторами, поршнями и шарнирами, либо плавающий объект. Пристыкованные через коннектор гриды и шасси не затрагиваются. Персонажей, планеты, астероиды и отдельные блоки удалять нельзя." + EntityHelp + "\nПример: !entities delete 123456789")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Delete(string entityName)
        {
            if (!TryFindOne(entityName, out var entity))
                return;

            if (entity is MyCubeGrid grid)
            {
                var grids = Utilities.GetConnectedGrids(grid, GridLinkTypeEnum.Mechanical);
                var center = Utilities.FormatGridCenter(grid);
                foreach (var connected in grids)
                    connected.Close();

                Context.Respond($"Грид '{grid.DisplayName}' удалён, всего гридов: {grids.Count}.");
                Log.Info($"{Admin} deleted grid {grid.DisplayName} ({grid.EntityId}), position: {center}, grids in total: {grids.Count}");
            }
            else if (entity is MyFloatingObject)
            {
                entity.Close();
                Context.Respond($"Объект '{entity.DisplayName}' удалён.");
                Log.Info($"{Admin} deleted floating object {entity.DisplayName} ({entity.EntityId})");
            }
            else
            {
                Context.Respond("Удалять можно только гриды и плавающие объекты.");
            }
        }

        [Command("kill", "Убивает игрока.", "Убивает персонажа игрока, в том числе сидящего и находящегося офлайн, и объявляет об этом в общем чате." + PlayerHelp + "\nПример: !entities kill Иван")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Kill(string playerName)
        {
            if (!TryFindCharacter(playerName, out var character))
                return;

            if (character.IsDead)
            {
                Context.Respond($"Персонаж '{character.DisplayName}' уже мёртв.");
                return;
            }

            if (character.StatComp?.Health == null || !character.CharacterCanDie)
            {
                Context.Respond($"Персонажа '{character.DisplayName}' нельзя убить.");
                return;
            }

            //the character's own damage is blocked by a safe zone, and bots can share an identity
            var health = character.StatComp.Health.Value;
            character.StatComp.DoDamage(health, new MyDamageInformation(false, health, MyStringHash.GetOrCompute("Script"), 0));

            Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf
                ($"{character.DisplayName} убит администратором");

            Log.Info($"{Admin} killed {character.DisplayName} ({character.EntityId})");
        }

        [Command("find", "Ищет сущности с указанным текстом в названии.", "Ищет сущности по части названия без учёта регистра, включая сидящих персонажей. Для каждой выводит имя, ID и тип, а для грида ещё число блоков и владельца.\nПример: !entities find база")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            var sb = new StringBuilder();
            foreach (var entity in Utilities.GetSearchableEntities())
            {
                if (NameContains(Utilities.GetName(entity), name) || NameContains(entity.DisplayName, name))
                    sb.AppendLine(Describe(entity));
            }

            Context.Respond(sb.Length > 0 ? "Найденные сущности:\n" + sb : "Ничего не найдено.");
        }

        [Command("poweroff", "Выключает питание грида.", "Выключает все источники питания (реакторы, батареи, солнечные панели, водородные двигатели, ветрогенераторы) грида и всего, что связано с ним роторами, поршнями и шарнирами." + EntityHelp + "\nПример: !entities poweroff \"Мой корабль\"")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void PowerOff(string name)
        {
            SetPower(name, false);
        }

        [Command("poweron", "Включает питание грида.", "Включает все источники питания (реакторы, батареи, солнечные панели, водородные двигатели, ветрогенераторы) грида и всего, что связано с ним роторами, поршнями и шарнирами. Включается всё, в том числе то, что владелец выключил сам." + EntityHelp + "\nПример: !entities poweron \"Мой корабль\"")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void PowerOn(string name)
        {
            SetPower(name, true);
        }

        private void SetPower(string name, bool enabled)
        {
            if (!TryFindOne(name, out var entity))
                return;

            if (!(entity is MyCubeGrid grid))
            {
                Context.Respond("Команда работает только на гридах.");
                return;
            }

            var blocks = Utilities.GetConnectedGrids(grid, GridLinkTypeEnum.Mechanical)
                .SelectMany(g => g.GetFatBlocks().OfType<IMyPowerProducer>())
                .Where(b => b.Enabled != enabled)
                .ToList();

            foreach (var block in blocks)
                block.Enabled = enabled;

            Context.Respond($"Питание грида '{grid.DisplayName}' {(enabled ? "включено" : "выключено")}, изменено блоков: {blocks.Count}.");
            Log.Info($"{Admin} turned power {(enabled ? "on" : "off")} on {grid.DisplayName} ({grid.EntityId}), blocks changed: {blocks.Count}");
        }

        [Command("eject", "Высаживает игрока из блока, в котором он сидит, или всех, если указать 'all'", "Высаживает игрока из кокпита, кресла или криокамеры и отключает от пульта дистанционного управления. Вариант 'all' действует на всех, включая офлайн-игроков, спящих в криокамерах, и ботов." + PlayerHelp + "\nПримеры: !entities eject Иван или !entities eject all")]
        public void Eject(string playerName)
        {
            if (playerName.ToLower() == "all")
            {
                EjectAllPlayers();
            }
            else
            {
                EjectSinglePlayer(playerName);
            }
        }

        private void EjectAllPlayers()
        {
            var grids = MyEntities.GetEntities().OfType<MyCubeGrid>().ToList();

            //first the remote controls, because they return the control to the cockpit their user sits in
            var released = UseAllOccupied<MyRemoteControl>(grids);
            var ejected = UseAllOccupied<MyCockpit>(grids);

            Context.Respond($"Высажено: {ejected}, отключено от пультов: {released}.");
            Log.Info($"{Admin} ejected everyone: {ejected} seated, {released} from remote controls");
        }

        private static int UseAllOccupied<T>(List<MyCubeGrid> grids) where T : MyShipController
        {
            var occupied = grids.SelectMany(g => g.GetFatBlocks<T>()).Where(c => c.Pilot != null).ToList();
            foreach (var controller in occupied)
                controller.Use();

            return occupied.Count;
        }

        private void EjectSinglePlayer(string playerName)
        {
            if (!TryFindCharacter(playerName, out var character))
                return;

            //first the remote control, because it returns the control to the cockpit the player sits in
            var remote = Utilities.GetOnlinePlayers().FirstOrDefault(p => p.Character == character)?.Controller?.ControlledEntity as MyRemoteControl;
            var cockpit = character.Parent as MyCockpit;

            if (remote == null && cockpit == null)
            {
                Context.Respond($"Игрок '{character.DisplayName}' нигде не сидит.");
                return;
            }

            remote?.Use();
            cockpit?.Use();

            Context.Respond($"Игрок '{character.DisplayName}' {(cockpit != null ? "высажен" : "отключён от пульта")}.");
            Log.Info($"{Admin} ejected {character.DisplayName} ({character.EntityId})");
        }

        private bool TryFindOne(string nameOrId, out MyEntity entity)
        {
            var found = Utilities.FindEntities(nameOrId);
            if (found.Count == 1)
            {
                entity = found[0];
                return true;
            }

            entity = null;
            Context.Respond(found.Count == 0
                ? $"Сущность '{nameOrId}' не найдена."
                : $"Под '{nameOrId}' подходит несколько сущностей, укажите ID:\n{string.Join("\n", found.Select(Describe))}");
            return false;
        }

        /// <summary>
        /// Players can share a name, and the one online comes first.
        /// </summary>
        private bool TryFindCharacter(string nameOrId, out MyCharacter character)
        {
            character = Utilities.FindEntities(nameOrId).OfType<MyCharacter>().FirstOrDefault();
            if (character != null)
                return true;

            Context.Respond($"Игрок '{nameOrId}' не найден, или у него нет персонажа.");
            return false;
        }

        //DisplayName can be null
        private static bool NameContains(string entityName, string text)
        {
            return entityName?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false;
        }

        private static string Describe(MyEntity entity)
        {
            var description = $"{Utilities.GetName(entity)} ({entity.EntityId}), ";

            switch (entity)
            {
                case MyCubeGrid grid:
                    var owner = MySession.Static.Players.TryGetIdentity(Utils.Ownership.GetOwner(grid))?.DisplayName ?? "нет";
                    return description + $"грид, блоков: {grid.BlocksCount}, владелец: {owner}";
                case MyCharacter character:
                    return description + (character.IsDead ? "мёртвый персонаж" : "персонаж");
                case MyPlanet _:
                    return description + "планета";
                case IMyVoxelBase _:
                    return description + "астероид";
                case MyFloatingObject _:
                    return description + "плавающий объект";
                default:
                    return description + entity.GetType().Name;
            }
        }
    }
}
