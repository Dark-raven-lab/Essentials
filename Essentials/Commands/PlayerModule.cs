using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Managers;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Torch.API.Managers;

namespace Essentials
{
    public class PlayerModule : CommandModule
    {
        private EssentialsPlugin Plugin => (EssentialsPlugin)Context.Plugin;

        [Command("say", "Отправляет сообщение от имени сервера.")]
        public void Say(string message)
        {
            Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf(Context.RawArgs);
        }

        [Command("tp", "Перемещает одну сущность к другой.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Teleport(string entityToMove, string destination)
        {

            IMyEntity destEntity;
            if (string.IsNullOrEmpty(destination))
                destEntity = Context.Player?.Controller.ControlledEntity.Entity;
            else
                Utilities.TryGetEntityByNameOrId(destination, out destEntity);

            if (destEntity == null)
            {
                Context.Respond("Сущность назначения не найдена");
                return;
            }

            IMyEntity targetEntity;
            if (string.IsNullOrEmpty(entityToMove))
                targetEntity = Context.Player?.Controller.ControlledEntity.Entity;
            else
                Utilities.TryGetEntityByNameOrId(entityToMove, out targetEntity);

            if (targetEntity == null)
            {
                Context.Respond("Целевая сущность не найдена.");
                return;
            }

            var targetPos = MyEntities.FindFreePlace(destEntity.GetPosition(), (float)targetEntity.WorldAABB.Extents.Max());
            if (targetPos == null)
            {
                Context.Respond("Нет свободного места для перемещения.");
                return;
            }

            targetEntity.SetPosition(targetPos.Value);
        }

        [Command("tpto", "Перемещает к другой сущности.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void TeleportTo(string destination, string entityToMove = null)
        {
            Teleport(entityToMove, destination);
        }

        [Command("tphere", "Перемещает другую сущность к вам.")]
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
