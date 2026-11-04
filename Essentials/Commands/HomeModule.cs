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
            if (!TryGetAccount("Ошибка загрузки данных. Дом не установлен", out var Account, out var Rank))
                return;

            if (Account.Homes.ContainsKey(homeName)) {
                Account.Homes[homeName] = Context.Player.GetPosition();
                PlayerAccounts.SaveAccountData();
                Context.Respond("Дом перенесён на ваше текущее место!");
                return;
            }

            if (Account.Homes.Count >= Rank.MaxHomes ) {
                Context.Respond("У вас уже максимальное число домов!");
                return;
            }

            Account.Homes.Add(homeName, Context.Player.GetPosition());
            PlayerAccounts.SaveAccountData();
            Context.Respond("Дом добавлен!");
        }

        [Command("del", "Удаляет ваш дом с указанным именем.")]
        [Permission(MyPromoteLevel.None)]
        public void delHome(string homeName) {
            if (!EssentialsPlugin.Instance.Config.EnableHomes) {
                Context.Respond("Дома на этом сервере отключены!");
                return;
            }
            if (!TryGetAccount("Ошибка загрузки данных. Дом не удалён!", out var Account, out _))
                return;

            if (Account.Homes.Remove(homeName)) {
                PlayerAccounts.SaveAccountData();
                Context.Respond("Дом удалён!");
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
            if (!TryGetAccount("Ошибка загрузки данных.", out var Account, out _))
                return;

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
            if (!TryGetAccount("Ошибка загрузки данных.", out var Account, out _))
                return;

            if (!Account.Homes.TryGetValue(homeName, out var targetPos)) {
                Context.Respond("Такого дома не существует!");
                return;
            }

            var character = Context.Player.Character;
            if (character == null || character.IsDead) {
                Context.Respond("Сначала возродитесь.");
                return;
            }

            if (Context.Player.Controller?.ControlledEntity?.Entity != character) {
                Context.Respond("Нельзя использовать !home, управляя гридом");
                return;
            }

            character.SetPosition(targetPos);
            character.Physics?.SetSpeeds(Vector3.Zero, Vector3.Zero);
            Context.Respond($"Вы перемещены в '{homeName}'");
        }

        private bool TryGetAccount(string error, out PlayerAccountModule.PlayerAccountData account, out RanksAndPermissionsModule.RankData rank) {
            account = null;
            rank = null;
            if (Context.Player == null) {
                Context.Respond("Команда доступна только игрокам.");
                return false;
            }
            account = PlayerAccounts.GetAccount(Context.Player.SteamUserId);
            rank = account == null ? null : RanksAndPermissionsModule.GetRankData(account.Rank);
            if (rank == null) {
                Context.Respond(error);
                return false;
            }
            return true;
        }
    }
}
