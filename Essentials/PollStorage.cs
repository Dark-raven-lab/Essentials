using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using Torch.API.Managers;
using VRage.Game;

namespace Essentials
{
    /// <summary>
    /// Multi-day yes/no polls of players. Unlike !vote they run no command and are kept in a file over restarts.
    /// Used only from the game thread: commands, Update and the join callback all run there.
    /// </summary>
    public sealed class PollStorage
    {
        public sealed class Poll
        {
            public int Id;
            public string Question;
            public DateTime StartUtc;
            public DateTime EndUtc;
            public bool Finished;

            /// <summary>Steam ID to the answer, true is yes. A player's new vote replaces the old one.</summary>
            public Dictionary<ulong, bool> Votes = new Dictionary<ulong, bool>();

            [JsonIgnore] public int Yes => Votes.Count(v => v.Value);
            [JsonIgnore] public int No => Votes.Count(v => !v.Value);
        }

        private sealed class FileData
        {
            public int NextId = 1;
            public List<Poll> Polls = new List<Poll>();
        }

        private static readonly Logger Log = LogManager.GetLogger("Essentials");

        private const string VoteHint = "Голосуйте: !poll yes <номер> или !poll no <номер>";

        private readonly string _filePath;
        private FileData _data = new FileData();
        private bool _running;
        private DateTime _nextEndCheck;

        private static IChatManagerServer ChatManager => EssentialsPlugin.Instance?.Torch?.CurrentSession?.Managers?.GetManager<IChatManagerServer>();

        public PollStorage(string filePath)
        {
            _filePath = filePath;
        }

        public IEnumerable<Poll> Active => _data.Polls.Where(p => !p.Finished);
        public IEnumerable<Poll> Finished => _data.Polls.Where(p => p.Finished);

        /// <summary>
        /// Reads the file. The polls that ended while the server was off are finished on the next update.
        /// </summary>
        public void Start()
        {
            Read();
            Log.Info($"Loaded {_data.Polls.Count} polls");
            _running = true;
        }

        public void Stop()
        {
            _running = false;
        }

        /// <summary>
        /// Called on the game thread every tick, finishes the ended polls every few seconds.
        /// </summary>
        public void Update()
        {
            var now = DateTime.UtcNow;
            if (!_running || now < _nextEndCheck)
                return;

            _nextEndCheck = now.AddSeconds(5);
            foreach (var poll in Active.Where(p => p.EndUtc <= now).ToList())
                Finish(poll);
        }

        public Poll Get(int id) => _data.Polls.FirstOrDefault(p => p.Id == id);

        public Poll Create(string question, TimeSpan duration)
        {
            var now = DateTime.UtcNow;
            var poll = new Poll
            {
                Id = _data.NextId++,
                Question = question,
                StartUtc = now,
                EndUtc = now + duration,
            };
            _data.Polls.Add(poll);
            Save();
            Broadcast($"Новый опрос {Describe(poll)}. Голосуйте: !poll yes {poll.Id} или !poll no {poll.Id}");
            return poll;
        }

        /// <returns>The previous answer of the player, null if the player votes for the first time.</returns>
        public bool? SetVote(Poll poll, ulong steamId, bool yes)
        {
            bool? previous = poll.Votes.TryGetValue(steamId, out var old) ? old : (bool?)null;
            poll.Votes[steamId] = yes;
            Save();
            return previous;
        }

        /// <summary>
        /// Ends the poll now if its time has not run out yet, the result stays for viewing.
        /// </summary>
        public void Finish(Poll poll)
        {
            var now = DateTime.UtcNow;
            if (poll.EndUtc > now)
                poll.EndUtc = now;
            poll.Finished = true;
            Save();
            Broadcast($"Опрос {Describe(poll)}");
        }

        public bool Delete(int id)
        {
            if (_data.Polls.RemoveAll(p => p.Id == id) == 0)
                return false;
            Save();
            return true;
        }

        /// <summary>
        /// Tells a player who has just spawned about the running polls they have not voted in.
        /// </summary>
        public void RemindOnJoin(ulong steamId)
        {
            if (!_running)
                return;

            var pending = Active.Where(p => !p.Votes.ContainsKey(steamId)).ToList();
            if (pending.Count == 0)
                return;

            var lines = pending.Select(p => $"#{p.Id} «{p.Question}», осталось {FormatRemaining(p)}");
            Send($"Идут опросы, в которых вы ещё не голосовали:\n{string.Join("\n", lines)}\n{VoteHint}", steamId);
        }

        /// <summary>
        /// Posts the running polls to the chat of everyone, says nothing when there are none.
        /// </summary>
        /// <returns>The number of running polls.</returns>
        public int AnnounceActive()
        {
            var active = Active.ToList();
            if (active.Count == 0)
                return 0;

            Broadcast($"Идут опросы:\n{string.Join("\n", active.Select(Describe))}\n{VoteHint}");
            return active.Count;
        }

        public static string Describe(Poll poll)
        {
            var state = poll.Finished ? "завершён" : $"осталось {FormatRemaining(poll)}";
            return $"#{poll.Id} «{poll.Question}»: за {poll.Yes}, против {poll.No}, {state}";
        }

        public static string AnswerText(bool yes) => yes ? "за" : "против";

        public static string FormatRemaining(Poll poll)
        {
            //rounded up: the last minute shows as 1 min and not as 0
            var minutes = Math.Max(1, (int)Math.Ceiling((poll.EndUtc - DateTime.UtcNow).TotalMinutes));
            if (minutes >= 24 * 60)
                return $"{minutes / (24 * 60)} д. {minutes / 60 % 24} ч.";
            if (minutes >= 60)
                return $"{minutes / 60} ч. {minutes % 60} мин.";
            return $"{minutes} мин.";
        }

        private static void Broadcast(string message)
        {
            ChatManager?.SendMessageAsOther("Опрос", message, MyFontEnum.Blue);
        }

        private static void Send(string message, ulong steamId)
        {
            //a target of 0 would send the message to everyone
            if (steamId != 0)
                ChatManager?.SendMessageAsOther("Опрос", message, MyFontEnum.Blue, steamId);
        }

        private void Read()
        {
            _data = new FileData();
            if (!File.Exists(_filePath))
                return;

            try
            {
                _data = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(_filePath)) ?? new FileData();
                _data.Polls.RemoveAll(p => p == null);
                //a hand-edited file could hold a lower NextId, and ids must not repeat
                _data.NextId = Math.Max(_data.NextId, _data.Polls.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1);
            }
            catch (Exception e)
            {
                //kept aside, the next save would otherwise overwrite the polls with an empty list
                var broken = _filePath + ".broken";
                Log.Error(e, $"Failed to read {_filePath}, moved it to {broken}");
                _data = new FileData();
                try
                {
                    File.Copy(_filePath, broken, true);
                    File.Delete(_filePath);
                }
                catch (Exception moveError)
                {
                    Log.Error(moveError, $"Failed to move {_filePath}");
                }
            }
        }

        private void Save()
        {
            try
            {
                //written next to the file first, so a crash during the write leaves the old file whole
                var temp = _filePath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_data, Formatting.Indented));
                if (File.Exists(_filePath))
                    File.Replace(temp, _filePath, null);
                else
                    File.Move(temp, _filePath);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Failed to write {_filePath}");
            }
        }
    }
}
