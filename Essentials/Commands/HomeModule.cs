using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.World;
using Torch;
using VRage;
using Torch.API.Managers;
using VRageMath;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Managers.ChatManager;
using Torch.Mod;
using Sandbox.Game.Entities;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;
using VRageRender.Utils;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;

namespace Essentials.Commands {
    [Category("home")]
    public class HomeModule : CommandModule {
        RanksAndPermissionsModule RanksAndPermissionsModule = new RanksAndPermissionsModule();
        PlayerAccountModule PlayerAccounts = new PlayerAccountModule();

        [Command("add", "Сохраняет ваше текущее место как дом с указанным именем.")]
        [Permission(MyPromoteLevel.None)]
        public void addHome(string homeName) {
            if (!EssentialsPlugin.Instance.Config.EnableHomes) {
                Context.Respond("Дома на этом сервере отключены!");
                return;
            }
            var Account = PlayerAccounts.GetAccount(Context.Player.SteamUserId);
            var Rank = RanksAndPermissionsModule.GetRankData(Account.Rank);

            if (Rank == null || Account == null) {
                Context.Respond("Ошибка загрузки данных. Дом не установлен");
                return;
            }

            if (Account.Homes.Count >= Rank.MaxHomes ) {
                Context.Respond("У вас уже максимальное число домов!");
                return;
            }

            Account.Homes.Add(homeName, Context.Player.GetPosition());
            PlayerAccounts.UpdatePlayerAccount(Account);
            Context.Respond("Дом добавлен!");
        }

        [Command("del", "Удаляет ваш дом с указанным именем.")]
        [Permission(MyPromoteLevel.None)]
        public void delHome(string homeName) {
            if (!EssentialsPlugin.Instance.Config.EnableHomes) {
                Context.Respond("Дома на этом сервере отключены!");
                return;
            }
            var Account = PlayerAccounts.GetAccount(Context.Player.SteamUserId);
            var Rank = RanksAndPermissionsModule.GetRankData(Account.Rank);

            if (Rank == null || Account == null) {
                Context.Respond("Ошибка загрузки данных. Дом не удалён!");
                return;
            }

            if (Account.Homes.ContainsKey(homeName)) {
                Account.Homes.Remove(homeName);
                Context.Respond("Дом удалён!");
                PlayerAccounts.UpdatePlayerAccount(Account);
                return;
            }

            Context.Respond("Такого дома не существует!");
        }

        [Command("list", "Показывает ваши дома.")]
        [Permission(MyPromoteLevel.None)]
        public void ListHomes() {
            if (!EssentialsPlugin.Instance.Config.EnableHomes) {
                Context.Respond("Дома на этом сервере отключены!");
                return;
            }
            var Account = PlayerAccounts.GetAccount(Context.Player.SteamUserId);
            var Rank = RanksAndPermissionsModule.GetRankData(Account.Rank);

            if (Rank == null || Account == null) {
                Context.Respond("Ошибка загрузки данных. Дом не удалён!");
                return;
            }

            if (Account.Homes.Count == 0) {
                Context.Respond("У вас нет домов!");
                return;
            }

            StringBuilder sb = new StringBuilder();

            sb.Append("Список домов: ");
            foreach(var homes in Account.Homes) {
                sb.Append($"'{homes.Key}', ");
            }
            sb.TrimEnd(2);
            Context.Respond(sb.ToString());
        }

        [Command("goto", "Перемещает вас к дому с указанным именем. Не работает из кокпита.")]
        [Permission(MyPromoteLevel.None)]
        public void GotoHome(string homeName) {
            if (!EssentialsPlugin.Instance.Config.EnableHomes) {
                Context.Respond("Дома на этом сервере отключены!");
                return;
            }
            var Account = PlayerAccounts.GetAccount(Context.Player.SteamUserId);
            var Rank = RanksAndPermissionsModule.GetRankData(Account.Rank);

            if (Rank == null || Account == null) {
                Context.Respond("Ошибка загрузки данных.");
                return;
            }

            Vector3D targetPos = Account.Homes[homeName];

            var targetEntity = Context.Player?.Controller.ControlledEntity.Entity;
            if (Context.Player?.Controller.ControlledEntity is MyCockpit controller) {
                Context.Respond("Нельзя использовать !home, управляя гридом");
                return;
            }

            var player = MySession.Static.Players.GetOnlinePlayers().Where(i => i.Identity.IdentityId == Context.Player.Identity.IdentityId).First();

            targetEntity.SetPosition(targetPos);

            for (int i = 0; i != 5000; i++) {
                Context.Player.Character.Physics.SetSpeeds(Vector3.Zero, Vector3.Zero);
            }
            Context.Respond($"Вы перемещены в '{homeName}'");
        }
    }
}
