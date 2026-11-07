using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Sandbox;
using Sandbox.Game.World;
using Torch.API.Session;

namespace Essentials
{
    /// <summary>
    /// Changes of the ranks and of the ranks of players, shared by the !pr commands and the plugin page. While the world
    /// is loaded they run on the game thread (the page passes them through <see cref="Run"/>); while it is not, only the
    /// files are changed, so what needs the game (promote levels, reserved slots) is refused then.
    /// Each returns the answer for the admin.
    /// </summary>
    public static class RankOperations
    {
        /// <summary>The values of RankData.KeenLevelRank.</summary>
        public enum Level
        {
            None,
            Scripter,
            Moderator,
            SpaceMaster,
            Admin
        }

        private const string NeedsServer = "Это меняется только при запущенном сервере.";

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static readonly RanksAndPermissionsModule Ranks = new RanksAndPermissionsModule();
        private static readonly PlayerAccountModule Accounts = new PlayerAccountModule();

        /// <summary>
        /// Raised on the thread that saved the ranks or the accounts, and when the world loads or unloads.
        /// </summary>
        public static event Action Changed;

        public static void RaiseChanged() => Changed?.Invoke();

        /// <summary>The game is running, so promote levels and reserved slots can be changed.</summary>
        public static bool IsLive => EssentialsPlugin.Instance?.Torch?.CurrentSession?.State == TorchSessionState.Loaded;

        /// <summary>
        /// Runs a change made outside the game thread: queued to the game while it runs, at once while it does not.
        /// </summary>
        public static void Run(Action action)
        {
            void Safe()
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Rank change failed");
                }
            }

            if (IsLive)
                EssentialsPlugin.Instance.Torch.InvokeAsync(Safe);
            else
                Safe();
        }

        public static string NotFound(string rankName) => $"Ранга '{rankName}' не существует!";

        public static List<PlayerAccountModule.PlayerAccountData> Members(string rankName)
        {
            return PlayerAccountModule.PlayersAccounts.Where(a => a.Rank == rankName).ToList();
        }

        public static string CreateRank(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
                return "Укажите имя ранга.";
            return Ranks.GenerateRank(name) ? $"Ранг '{name}' создан!" : $"Ранг '{name}' уже существует!";
        }

        public static string DeleteRank(string name)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);
            var defaultRank = EssentialsPlugin.Instance.Config.DefaultRank;
            if (rank.RankName == defaultRank)
                return "Нельзя удалить ранг по умолчанию. Сначала задайте другой ранг по умолчанию.";

            RanksAndPermissionsModule.Ranks.Remove(rank);
            foreach (var other in RanksAndPermissionsModule.Ranks)
                other.Inherits.RemoveAll(n => n == rank.RankName);
            Ranks.SaveRankData();

            //ValidateRanks does this reassignment only on load. The reserved list is touched only when the flag differs, so slots given with !admin reserve survive
            var defaultData = Ranks.GetRankData(defaultRank);
            var syncReserved = defaultData != null && defaultData.ReservedSlot != rank.ReservedSlot;
            var members = Members(rank.RankName);
            var reservedChanged = false;
            foreach (var account in members)
            {
                account.Rank = defaultRank;
                if (syncReserved && SetReserved(account.SteamID, defaultData.ReservedSlot))
                    reservedChanged = true;
            }
            if (members.Count > 0)
                Accounts.SaveAccountData();
            if (reservedChanged)
                MySandboxGame.ConfigDedicated.Save();
            Ranks.RebuildAllInheritedRanks();

            return $"Ранг '{rank.RankName}' удалён. Игроков переведено на ранг '{defaultRank}': {members.Count}.";
        }

        public static string RenameRank(string oldName, string newName)
        {
            newName = newName?.Trim();
            var rank = Ranks.GetRankData(oldName);
            if (rank == null)
                return NotFound(oldName);
            if (string.IsNullOrEmpty(newName))
                return "Укажите новое имя ранга.";
            if (Ranks.GetRankData(newName) != null)
                return $"Ранг '{newName}' уже существует!";

            rank.RankName = newName;
            foreach (var other in RanksAndPermissionsModule.Ranks)
                for (var i = 0; i < other.Inherits.Count; i++)
                    if (other.Inherits[i] == oldName)
                        other.Inherits[i] = newName;
            Ranks.SaveRankData();

            var members = Members(oldName);
            foreach (var account in members)
                account.Rank = newName;
            if (members.Count > 0)
                Accounts.SaveAccountData();

            var config = EssentialsPlugin.Instance.Config;
            if (config.DefaultRank == oldName)
            {
                config.DefaultRank = newName;
                EssentialsPlugin.Instance.Save();
            }

            return $"Ранг '{oldName}' переименован в '{newName}'. Игроков с этим рангом: {members.Count}.";
        }

        public static string SetMaxHomes(string name, int value)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);
            if (value < 0)
                return "Число домов не может быть отрицательным.";

            rank.MaxHomes = value;
            Ranks.SaveRankData();
            return $"Игроки с рангом '{rank.RankName}' теперь могут установить домов: {value}";
        }

        public static string SetReservedSlot(string name, bool reserved)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);
            if (!IsLive)
                return NeedsServer;

            rank.ReservedSlot = reserved;
            Ranks.SaveRankData();

            var changed = Members(rank.RankName).Count(a => SetReserved(a.SteamID, reserved));
            if (changed > 0)
                MySandboxGame.ConfigDedicated.Save();

            return $"Резервный слот у ранга '{rank.RankName}' {(reserved ? "включён" : "выключен")}. Изменён список резерва для игроков: {changed}.";
        }

        /// <summary>
        /// Sets the Keen promote level of the rank and gives it to every player of the rank, as !pr setrank does for one.
        /// </summary>
        public static string SetLevel(string name, string level)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);
            //Enum.TryParse would take a number too
            var levels = Enum.GetNames(typeof(Level));
            var match = levels.FirstOrDefault(n => string.Equals(n, level, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return $"Уровень должен быть одним из: {string.Join(", ", levels)}.";
            if (!IsLive)
                return NeedsServer;

            rank.KeenLevelRank = match;
            Ranks.SaveRankData();

            var promote = Ranks.ParseMyPromoteLevel(rank.KeenLevelRank);
            var members = Members(rank.RankName).Where(a => a.SteamID != 0).ToList();
            foreach (var account in members)
                MySession.Static.SetUserPromoteLevel(account.SteamID, promote);

            return $"Уровень прав ранга '{rank.RankName}': {rank.KeenLevelRank}. Применён к игрокам ранга: {members.Count}.";
        }

        public static string SetPrefix(string name, string prefix)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);

            rank.Prefix = prefix?.Trim() ?? "";
            Ranks.SaveRankData();
            return rank.Prefix.Length == 0 ? $"Префикс ранга '{rank.RankName}' убран." : $"Префикс ранга '{rank.RankName}': {rank.Prefix}";
        }

        public static string SetShowPrefix(string name, bool show)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);

            rank.DisplayPrefix = show;
            Ranks.SaveRankData();
            return $"Префикс ранга '{rank.RankName}' {(show ? "показывается" : "не показывается")} в чате.";
        }

        public static string SetPermissions(string name, IEnumerable<string> allowed, IEnumerable<string> disallowed)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);

            rank.Permissions.Allowed = allowed.ToList();
            rank.Permissions.Disallowed = disallowed.ToList();
            Ranks.SaveRankData();
            return $"Права ранга '{rank.RankName}' сохранены.";
        }

        public static string SetInherits(string name, IEnumerable<string> inherits)
        {
            var rank = Ranks.GetRankData(name);
            if (rank == null)
                return NotFound(name);

            var list = inherits.Distinct().ToList();
            var unknown = list.Where(n => Ranks.GetRankData(n) == null).ToList();
            if (unknown.Count > 0)
                return $"Таких рангов нет: {string.Join(", ", unknown)}.";
            if (list.Contains(rank.RankName))
                return "Ранг не может наследовать сам себя.";

            rank.Inherits = list;
            Ranks.SaveRankData();
            Ranks.RebuildAllInheritedRanks();
            return $"Наследование ранга '{rank.RankName}' сохранено.";
        }

        public static string SetPlayerRank(ulong steamId, string rankName)
        {
            var account = PlayerAccountModule.PlayersAccounts.FirstOrDefault(a => a.SteamID == steamId);
            var rank = Ranks.GetRankData(rankName);
            if (account == null || rank == null)
                return "Игрок или ранг не найден.";
            return SetPlayerRank(account, rank);
        }

        /// <summary>
        /// Gives the player the rank together with its promote level and reserved slot.
        /// </summary>
        public static string SetPlayerRank(PlayerAccountModule.PlayerAccountData account, RanksAndPermissionsModule.RankData rank)
        {
            if (!IsLive)
                return NeedsServer;

            account.Rank = rank.RankName;
            Accounts.SaveAccountData();
            if (SetReserved(account.SteamID, rank.ReservedSlot))
                MySandboxGame.ConfigDedicated.Save();
            MySession.Static.SetUserPromoteLevel(account.SteamID, Ranks.ParseMyPromoteLevel(rank.KeenLevelRank));
            Ranks.RebuildInheritedRanks(account.SteamID);
            Log.Info($"Игроку {account.Player} задан ранг {rank.RankName}");
            return $"Игроку {account.Player} задан ранг {rank.RankName}";
        }

        //without the game there is no server config to change, a rank deleted then leaves the slots as they are
        private static bool SetReserved(ulong steamId, bool reserved)
        {
            if (steamId == 0 || !IsLive)
                return false;
            var list = MySandboxGame.ConfigDedicated.Reserved;
            if (list.Contains(steamId) == reserved)
                return false;
            if (reserved)
                list.Add(steamId);
            else
                list.Remove(steamId);
            return true;
        }
    }
}
