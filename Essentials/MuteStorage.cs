using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NLog;
using Sandbox.Game.World;
using Torch.API.Managers;
using VRageMath;

namespace Essentials
{
    /// <summary>
    /// The chat mutes of Torch only live in memory. Keeps them in a file over restarts and ends the timed ones.
    /// A line of the file is the Steam ID, then the expiry date if the mute has one.
    /// </summary>
    public sealed class MuteStorage
    {
        private static readonly Logger Log = LogManager.GetLogger("Essentials");

        readonly Dictionary<ulong, DateTime?> _mutes = new Dictionary<ulong, DateTime?>();
        readonly string _filePath;
        private bool _running;
        private DateTime _nextExpiryCheck;

        private static IChatManagerServer ChatManager => EssentialsPlugin.Instance.Torch.CurrentSession.Managers.GetManager<IChatManagerServer>();

        public MuteStorage(string filePath)
        {
            _filePath = filePath;
        }

        /// <summary>
        /// Mutes everyone from the file. The ones that expired while the server was off end on the next update.
        /// </summary>
        public void Start()
        {
            lock (_mutes)
            {
                Read();
                foreach (var steamId in _mutes.Keys)
                    ChatManager.MuteUser(steamId);
                Log.Info($"Restored {_mutes.Count} chat mutes");
            }

            _running = true;
        }

        public void Stop()
        {
            _running = false;
        }

        /// <summary>
        /// Called on the game thread every tick, ends the expired mutes once a second.
        /// </summary>
        public void Update()
        {
            var now = DateTime.Now;
            if (!_running || now < _nextExpiryCheck)
                return;

            _nextExpiryCheck = now.AddSeconds(1);
            RemoveExpired(now);
        }

        /// <param name="expiry">Null for no time limit.</param>
        /// <returns>False if the user was already muted and only the expiry changed.</returns>
        public bool Mute(ulong steamId, DateTime? expiry)
        {
            bool added;
            lock (_mutes)
            {
                added = !_mutes.ContainsKey(steamId);
                _mutes[steamId] = expiry;
                Save();
            }

            ChatManager.MuteUser(steamId);
            return added;
        }

        /// <returns>False if the user was not muted.</returns>
        public bool Unmute(ulong steamId)
        {
            bool removed;
            lock (_mutes)
            {
                removed = _mutes.Remove(steamId);
                if (removed)
                    Save();
            }

            //not only the file: another plugin could have muted the user
            return ChatManager.UnmuteUser(steamId) || removed;
        }

        /// <returns>Null if the mute has no time limit or was not made through this storage.</returns>
        public DateTime? GetExpiry(ulong steamId)
        {
            lock (_mutes)
                return _mutes.TryGetValue(steamId, out var expiry) ? expiry : null;
        }

        /// <summary>
        /// Torch drops the messages of a muted user without a word, the user still sees them in their own chat.
        /// </summary>
        public void NotifyIfMuted(ulong steamId)
        {
            var chatManager = ChatManager;

            //the target of the message is the identity, and without one it would go to everyone
            if (chatManager.MutedUsers.Contains(steamId) && MySession.Static.Players.TryGetIdentityId(steamId) != 0)
                chatManager.SendMessageAsOther(null, GetMutedMessage(GetExpiry(steamId)), default(Color), steamId);
        }

        private static string GetMutedMessage(DateTime? expiry)
        {
            if (!expiry.HasValue)
                return "Вы замьючены за нарушения.";

            //rounded up: the last minute shows as 00:01 and not as 00:00
            var minutes = Math.Max(0, (int)Math.Ceiling((expiry.Value - DateTime.Now).TotalMinutes));
            return $"Вы замьючены за нарушения ещё {minutes / 60:00}:{minutes % 60:00} (ч:мин).";
        }

        private void RemoveExpired(DateTime now)
        {
            lock (_mutes)
            {
                var expired = _mutes.Where(m => m.Value <= now).Select(m => m.Key).ToList();
                if (expired.Count == 0)
                    return;

                foreach (var steamId in expired)
                {
                    _mutes.Remove(steamId);
                    ChatManager.UnmuteUser(steamId);
                }

                Save();
            }
        }

        private void Read()
        {
            _mutes.Clear();
            if (!File.Exists(_filePath))
                return;

            try
            {
                foreach (var line in File.ReadAllLines(_filePath))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1 && ulong.TryParse(parts[0], out var permanent))
                        _mutes[permanent] = null;
                    else if (parts.Length == 2 && ulong.TryParse(parts[0], out var timed) && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry))
                        _mutes[timed] = expiry;
                    else if (parts.Length > 0)
                        Log.Warn($"Skipped a wrong line in {_filePath}: {line}");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, $"Failed to read {_filePath}");
            }
        }

        private void Save()
        {
            try
            {
                File.WriteAllLines(_filePath, _mutes.Select(m => m.Value.HasValue ? $"{m.Key} {m.Value.Value.ToString("o", CultureInfo.InvariantCulture)}" : $"{m.Key}"));
            }
            catch (Exception e)
            {
                Log.Error(e, $"Failed to write {_filePath}");
            }
        }
    }
}
