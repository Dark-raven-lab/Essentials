using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;

namespace Essentials.Commands
{
    [Category("econ")]
    public class EcoModule : CommandModule {

        [Command("give", "Добавляет кредиты на счёт игрока. Укажите '*', чтобы применить ко всем игрокам")]
        [Permission(MyPromoteLevel.Admin)]
        public void EcoGive(string player, long amount, bool onlyOnline = false, bool excludeNpcs = true) {

            if(!TryFindPlayerIdentities(player, onlyOnline, excludeNpcs, out List<long> foundIdentities)) {
                Context.Respond("Игрок не найден!");
                return;
            }

            int changedIdentities = 0;

            foreach (long identityId in foundIdentities) {

                ChangeBalance(identityId, amount);

                ulong steamId = Utilities.GetSteamId(identityId);

                ModCommunication.SendMessageTo(new NotificationMessage($"На ваш счёт зачислено кредитов: {amount:#,##0}", 10000, "Blue"), steamId);

                changedIdentities++;
            }

            Context.Respond($"Кредитов выдано: {amount:#,##0}, счетов: {changedIdentities:#,##0}");
        }

        [Command("take", "Снимает кредиты со счёта игрока. Укажите '*', чтобы применить ко всем игрокам")]
        [Permission(MyPromoteLevel.Admin)]
        public void EcoTake(string player, long amount, bool onlyOnline = false, bool excludeNpcs = true) {

            if (!TryFindPlayerIdentities(player, onlyOnline, excludeNpcs, out List<long> foundIdentities)) {
                Context.Respond("Игрок не найден!");
                return;
            }

            int changedIdentities = 0;

            foreach (long identityId in foundIdentities) {

                ChangeBalance(identityId, -amount);

                ulong steamId = Utilities.GetSteamId(identityId);

                ModCommunication.SendMessageTo(new NotificationMessage($"С вашего счёта списано кредитов: {amount:#,##0}", 10000, "Blue"), steamId);

                changedIdentities++;
            }

            Context.Respond($"Кредитов списано: {amount:#,##0}, счетов: {changedIdentities:#,##0}");
        }

        [Command("set", "Задаёт баланс счёта игрока. Укажите '*', чтобы применить ко всем игрокам")]
        [Permission(MyPromoteLevel.Admin)]
        public void EcoSet(string player, long amount, bool onlyOnline = false, bool excludeNpcs = true) {

            if (!TryFindPlayerIdentities(player, onlyOnline, excludeNpcs, out List<long> foundIdentities)) {
                Context.Respond("Игрок не найден!");
                return;
            }

            int changedIdentities = 0;

            foreach (long identityId in foundIdentities) {

                long balance = MyBankingSystem.GetBalance(identityId);

                ChangeBalance(identityId, -(balance - amount));

                ulong steamId = Utilities.GetSteamId(identityId);

                ModCommunication.SendMessageTo(new NotificationMessage($"Ваш баланс установлен, кредитов: {amount:#,##0}!", 10000, "Blue"), steamId);

                changedIdentities++;
            }

            Context.Respond($"Установлен баланс {amount:#,##0}, счетов: {changedIdentities:#,##0}");
        }

        [Command("reset", "Сбрасывает счёт игрока до 10 000 кредитов. Укажите '*', чтобы применить ко всем игрокам")]
        [Permission(MyPromoteLevel.Admin)]
        public void EcoReset(string player, bool onlyOnline = false, bool excludeNpcs = true) {
            EcoSet(player, 10_000, onlyOnline, excludeNpcs);
        }

        [Command("top", "Показывает балансы игроков сервера по убыванию")]
        [Permission(MyPromoteLevel.None)]
        public void EcoTop(bool onlyOnline = false, bool excludeNpcs = true) {

            TryFindPlayerIdentities("*", onlyOnline, excludeNpcs, out List<long> foundIdentities);

            var players = MySession.Static.Players;

            Dictionary<MyIdentity, long> balances = new Dictionary<MyIdentity, long>();
            foreach (long identityId in foundIdentities) {

                var identity = players.TryGetIdentity(identityId);
                long balance = MyBankingSystem.GetBalance(identityId);

                balances[identity] = balance;
            }

            StringBuilder ecodata = new StringBuilder();
            ecodata.AppendLine("Сводка балансов на сервере");

            var sorted = balances.OrderByDescending(x => x.Value).ThenBy(x => x.Key.DisplayName);
            foreach (var value in sorted) {

                var identity = value.Key;

                ecodata.AppendLine($"Игрок: {identity.DisplayName} - Баланс: {value.Value:#,##0}");
            }

            if (Context.Player == null) {
                Context.Respond(ecodata.ToString());
                return;
            }

            ModCommunication.SendMessageTo(new DialogMessage("Общий список балансов", "Игроки и их балансы в кредитах", ecodata.ToString()), Context.Player.SteamUserId);
        }

        [Command("check", "Показывает баланс указанного игрока")]
        [Permission(MyPromoteLevel.None)]
        public void EcoCheck(string Player) {

            var p = Utilities.GetIdentityByNameOrIds(Player);
            if (p == null) {
                Context.Respond("Игрок не найден!");
                return;
            }

            long balance = MyBankingSystem.GetBalance(p.IdentityId);

            Context.Respond($"Баланс игрока {p.DisplayName}, кредитов: {balance:#,##0}");
        }

        [Command("pay", "Переводит кредиты со своего счёта игроку в сети.")]
        [Permission(MyPromoteLevel.None)]
        public void EcoPay(string Player, long amount) {
            if (amount <= 0) {
                Context.Respond("Сумма не может быть отрицательной");
                return;
            }
            if (Context.Player == null) {
                Context.Respond("Команда недоступна из консоли");
                return;
            }
            
            /* We are purposely keeping the online check in this method. Otherwise it could cause confusion with players. */
            var p = Utilities.GetPlayerByNameOrId(Player);
            if (p == null) {
                Context.Respond("Игрок не в сети или не найден!");
                return;
            }

            var fromIdentitiyId = Context.Player.Identity.IdentityId;
            var toIdentitiyId = p.Identity.IdentityId;

            if (fromIdentitiyId == toIdentitiyId) {
                Context.Respond("Нельзя перевести кредиты самому себе!");
                return;
            }

            var finalFromBalance = MyBankingSystem.GetBalance(fromIdentitiyId) - amount;
            var finalToBalance = MyBankingSystem.GetBalance(toIdentitiyId) + amount;

            if(finalFromBalance < 0) {
                Context.Respond($"Не хватает кредитов: {-finalFromBalance:#,##0}!");
                return;
            }

            MyBankingSystem.RequestTransfer_BroadcastToClients(Context.Player.Identity.IdentityId, p.Identity.IdentityId, amount, finalFromBalance, finalToBalance);
            ModCommunication.SendMessageTo(new NotificationMessage($"Вы получили кредиты от {Context.Player.DisplayName}: {amount:#,##0}!", 10000, "Blue"), p.SteamUserId);
            ModCommunication.SendMessageTo(new NotificationMessage($"Вы отправили кредиты игроку {p.DisplayName}: {amount:#,##0}!", 10000, "Blue"), Context.Player.SteamUserId);
        }

        /// <summary>
        /// This method changes the balance of the given identity by the passed amount. 
        /// If the amount is positive the player receives credits. If it is negative, the player loses credits.
        /// 
        /// If the amount taken from a users account is greater than the amount the user has, the accounts balance is set to 0, since negative balances are not possible.
        /// </summary>
        private void ChangeBalance(long identityId, long amount) {

            long balance = MyBankingSystem.GetBalance(identityId);

            if (balance + amount < 0)
                amount = -balance;

            MyBankingSystem.ChangeBalance(identityId, amount);
        }

        /// <summary>
        /// This method take all identities of the server and assembles a list with the respective identityIDs. 
        /// It is possible to filter this list to not contain NPCs or only contain players that are currently online.
        /// 
        /// Returns true if the given playerName exists and could be found, or the playerName is "*" false otherwise. 
        /// Even if true is returned the list of foundIdentities can be empty if the online or npc filters got applied.
        /// </summary>
        private bool TryFindPlayerIdentities(string playerName, bool onlyOnline, bool excludeNpcs, out List<long> foundIdentities) {

            var relevantIdentities = new List<long>();
            var players = MySession.Static.Players;

            if (playerName != "*") {

                var identity = Utilities.GetIdentityByNameOrIds(playerName);

                if (identity == null) {
                    foundIdentities = relevantIdentities;
                    return false;
                }

                relevantIdentities.Add(identity.IdentityId);

            } else {

                relevantIdentities.AddRange(players.GetAllIdentities()
                   .Select(identity => identity.IdentityId));
            }

            IEnumerable<long> identitiesToCheck = relevantIdentities;

            if (onlyOnline)
                identitiesToCheck = identitiesToCheck.Where(identityId => players.IsPlayerOnline(identityId));

            if (excludeNpcs)
                identitiesToCheck = identitiesToCheck.Where(identityId => !players.IdentityIsNpc(identityId));

            foundIdentities = identitiesToCheck.ToList();

            return true;
        }
    }
}
