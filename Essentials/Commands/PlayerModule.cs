using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Managers;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Torch.API.Managers;

namespace Essentials
{
    public class PlayerModule : CommandModule
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private EssentialsPlugin Plugin => (EssentialsPlugin)Context.Plugin;

        [Command("say", "Отправляет сообщение от имени сервера.")]
        public void Say(string message)
        {
            Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf(Context.RawArgs);
        }

        private const string TeleportHelp = "\nСущность указывается по нику игрока, имени грида или ID. Имена с пробелами берите в кавычки."
            + "\nГрид перемещается вместе со всем, что с ним физически связано, а сидящий игрок вместе со своим кораблём."
            + "\nСтатичные и закреплённые гриды, планеты и астероиды перемещать нельзя. К ним перемещать можно."
            + "\nМесто выбирается снаружи цели со стороны перемещаемого объекта, а на планете на поверхности. Если там занято, точка рядом выбирается случайно."
            + "\nСкорость после перемещения обнуляется.";

        [Command("tp", "Перемещает грид или персонажа к другой сущности.", "Перемещает грид или персонажа к другой сущности." + TeleportHelp + "\nПример: !tp Иван \"Моя база\"")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Teleport(string entityToMove, string destination)
        {
            if (!TryFindEntityOrOwn(destination, "Сущность назначения не найдена", out var destEntity)
                || !TryFindEntityOrOwn(entityToMove, "Целевая сущность не найдена.", out var targetEntity))
                return;

            var grid = targetEntity as MyCubeGrid;
            if (grid == null && !(targetEntity is MyCharacter))
            {
                Context.Respond("Перемещать можно только гриды и персонажей.");
                return;
            }

            if (grid != null && !grid.CanBeTeleported(null, out _))
            {
                Context.Respond("Нельзя переместить: грид или связанная с ним конструкция статична или закреплена.");
                return;
            }

            //the game moves a grid together with everything physically connected to it
            var moved = grid != null ? MyCubeGridGroups.Static.Physical.GetGroup(grid).Nodes.Select(n => (IMyEntity)n.NodeData).ToList() : new List<IMyEntity> { targetEntity };

            if (moved.Contains(destEntity))
            {
                Context.Respond("Нельзя переместить сущность к самой себе.");
                return;
            }

            var box = GetBox(targetEntity);
            var radius = box.Extents.Max();

            var freePlace = MyEntities.FindFreePlace(GetPlaceNextTo(destEntity, box.Center, radius), (float)radius, allowSafezones: true);
            if (freePlace == null || !freePlace.Value.IsValid())
            {
                Context.Respond("Нет свободного места для перемещения.");
                return;
            }

            //the free place is for the center of what is moved, its position can be far from that
            var targetPos = freePlace.Value + targetEntity.GetPosition() - box.Center;

            if (grid != null)
            {
                var matrix = grid.WorldMatrix;
                matrix.Translation = targetPos;
                grid.Teleport(matrix);
            }
            else
            {
                targetEntity.SetPosition(targetPos);
            }

            //the game's teleport keeps the speed
            foreach (var entity in moved)
                entity.Physics?.ClearSpeed();

            var destName = destEntity.DisplayName ?? destEntity.EntityId.ToString();
            Context.Respond($"Сущность '{targetEntity.DisplayName}' перемещена к '{destName}'.");
            Log.Info($"{Context.Player?.DisplayName ?? "console"} teleported {targetEntity.DisplayName} ({targetEntity.EntityId}) to {destName} ({destEntity.EntityId}) at {targetPos}");
        }

        /// <summary>
        /// The position of a planet or an asteroid is its center and of a grid it can be anywhere in it, so the search starts outside.
        /// From the exact center of a planet the game's search would give a NaN.
        /// </summary>
        private static Vector3D GetPlaceNextTo(IMyEntity destination, Vector3D from, double radius)
        {
            if (destination is MyPlanet planet)
            {
                var surface = planet.GetClosestSurfacePointGlobal(ref from);
                return surface + Vector3D.Normalize(surface - planet.PositionComp.GetPosition()) * radius;
            }

            var box = GetBox(destination);
            var direction = from - box.Center;
            direction = direction.LengthSquared() > 0 ? Vector3D.Normalize(direction) : Vector3D.Up;
            return box.Center + direction * (box.HalfExtents.Length() + radius);
        }

        private static BoundingBoxD GetBox(IMyEntity entity)
        {
            return entity is MyCubeGrid grid ? grid.GetPhysicalGroupAABB() : entity.WorldAABB;
        }

        /// <param name="entity">What can really be moved: for a seated player or a block it's the grid.</param>
        private bool TryFindEntityOrOwn(string nameOrId, string notFoundMessage, out IMyEntity entity)
        {
            entity = FindEntityOrOwn(nameOrId);

            if (entity is MyCharacter character && character.IsDead)
            {
                Context.Respond($"Персонаж '{character.DisplayName}' мёртв.");
                return false;
            }

            entity = entity?.GetTopMostParent();
            if (entity == null || IsProjection(entity))
            {
                Context.Respond(notFoundMessage);
                return false;
            }

            return true;
        }

        private IMyEntity FindEntityOrOwn(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId))
                return Context.Player?.Controller?.ControlledEntity?.Entity;

            //the character of a seated player is not in the list of entities that is searched by name
            var player = Utilities.GetOnlinePlayers().FirstOrDefault(p => p.DisplayName == nameOrId);
            if (player?.Character != null)
                return player.Character;

            //a projection can have the same name as the real grid
            Utilities.TryGetEntityByNameOrId(nameOrId, e => !IsProjection(e), out var entity);
            return entity;
        }

        private static bool IsProjection(IMyEntity entity)
        {
            return entity is MyCubeGrid grid && (grid.IsPreview || grid.Physics == null);
        }

        [Command("tpto", "Перемещает вас или указанный грид либо персонажа к другой сущности.", "Перемещает вас к указанной сущности. Если вы сидите в кокпите, перемещается весь корабль. Вторым аргументом можно указать, кого переместить вместо вас." + TeleportHelp + "\nПримеры: !tpto Иван или !tpto \"Моя база\" Пётр")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void TeleportTo(string destination, string entityToMove = null)
        {
            Teleport(entityToMove, destination);
        }

        [Command("tphere", "Перемещает грид или персонажа к вам или к указанной сущности.", "Перемещает указанный грид или персонажа к вам. Вторым аргументом можно указать другую цель." + TeleportHelp + "\nПримеры: !tphere Иван или !tphere Иван \"Моя база\"")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void TeleportHere(string entityToMove, string destination = null) 
        {
            Teleport(entityToMove, destination);
        }

        [Command("w", "Отправляет личное сообщение другому игроку.")]
        [Permission(MyPromoteLevel.None)]
        public void Whisper(string playerName)
        {
            if (Context.Args.Count < 1)
                return;

            var msgIndex = Context.RawArgs.IndexOf(" ", playerName.Length);
            if (msgIndex == -1 || msgIndex > Context.RawArgs.Length - 1)
                return;

            var message = Context.RawArgs.Substring(msgIndex);
            var player = Context.Torch.CurrentSession?.Managers?.GetManager<IMultiplayerManagerBase>()?.GetPlayerByName(playerName);
            Console.WriteLine($"'{player?.DisplayName ?? "null"}'");

            if (player == null)
            {
                Context.Respond($"Игрок '{playerName}' не найден.");
                return;
            }

            Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsOther(message, Context.Player?.DisplayName ?? "Server", MyFontEnum.Red, player.SteamUserId);
        }

        [Command("kick", "Выкидывает игрока с сервера.")]
        [Permission(MyPromoteLevel.Moderator)]
        public void Kick(string playerName)
        {
            var player = Utilities.GetPlayerByNameOrId(playerName);
            if (player != null)
            {
                Context.Torch.CurrentSession?.Managers?.GetManager<IMultiplayerManagerServer>()?.KickPlayer(player.SteamUserId);
                Context.Respond($"Игрок '{player.DisplayName}' выкинут с сервера.");
            }
            else
            {
                Context.Respond("Игрок не найден.");
            }
        }

        [Command("ban", "Банит игрока на сервере.")]
        [Permission(MyPromoteLevel.Moderator)]
        public void Ban(string nameOrSteamId)
        {
            var man = Context.Torch.CurrentSession.Managers.GetManager<IMultiplayerManagerServer>();
            var isId = ulong.TryParse(nameOrSteamId, out var steamId);

            foreach (var identity in MySession.Static.Players.GetAllIdentities())
            {
                var id = MySession.Static.Players.TryGetSteamId(identity.IdentityId);
                if (id != 0 && (identity.DisplayName == nameOrSteamId || id == steamId))
                {
                    man.BanPlayer(id);
                    Context.Respond($"Игрок {identity.DisplayName} забанен. ({id})");
                    return;
                }
            }

            if (isId)
            {
                man.BanPlayer(steamId);
                Context.Respond($"SteamID {steamId} забанен.");
                return;
            }
            
            Context.Respond($"Игрок '{nameOrSteamId}' не найден.");
        }

        [Command("unban", "Снимает бан с игрока.")]
        [Permission(MyPromoteLevel.Moderator)]
        public void Unban(string nameOrSteamId)
        {
            var man = Context.Torch.CurrentSession.Managers.GetManager<IMultiplayerManagerServer>();
            var isId = ulong.TryParse(nameOrSteamId, out var steamId);

            foreach (var identity in MySession.Static.Players.GetAllIdentities())
            {
                var id = MySession.Static.Players.TryGetSteamId(identity.IdentityId);
                if (id != 0 && (identity.DisplayName == nameOrSteamId || id == steamId))
                {
                    man.BanPlayer(id, false);
                    Context.Respond($"С игрока {identity.DisplayName} снят бан. ({id})");
                    return;
                }
            }

            if (isId)
            {
                man.BanPlayer(steamId, false);
                Context.Respond($"С SteamID {steamId} снят бан.");
                return;
            }
            
            Context.Respond($"Игрок '{nameOrSteamId}' не найден.");
        }

        [Command("motd", "Показывает сообщение дня сервера.")]
        [Permission(MyPromoteLevel.None)]
        public void Motd()
        {
            Plugin.SendMotd((MyPlayer)Context.Player, false);
        }
    }
}
