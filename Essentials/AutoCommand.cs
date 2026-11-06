using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using NLog;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.Server.ViewModels.Entities;
using Torch.Commands;
using Torch.Server;
using Torch.Views;
using Sandbox.Game.World;
using Sandbox.Game.Entities;

namespace Essentials
{
    public class AutoCommand : ViewModel
    {
        private TimeSpan _scheduledTime = TimeSpan.Zero;
        private static readonly Logger Log = LogManager.GetLogger("Essentials");
        private TimeSpan _interval = TimeSpan.Zero;
        private DateTime _nextRun = DateTime.MinValue;
        private DayOfWeek _day = DayOfWeek.All;
        private Trigger _trigger = Trigger.Disabled;
        private Gtl _comparer = Gtl.LessThan;
        private int _currentStep;
        private string _name;
        private float _triggerRatio;
        private double _triggerCount;
        private bool _isRunning;

        [XmlIgnore]
        public bool Completed { get; set; }

        [Display(Order = 3, Name = "Триггер", GroupName = "Основное", Description = "Когда запускать: Disabled — только вручную (!admin runauto), Timed — каждый интервал, Scheduled — в заданное время суток, OnStart — при старте сервера, PlayerCount, GridCount и SimSpeed — по условию, Vote — по итогам голосования !vote.")]
        public Trigger CommandTrigger
        {
            get => _trigger;
            set => SetValue(ref _trigger, value);
        }
        
        [Display(Order = 6, Name = "Сравнение", GroupName = "Основное", Description = "Для PlayerCount, GridCount и SimSpeed: LessThan — меньше порога, GreaterThan — больше, Equal — равно.")]
        public Gtl Compare
        {
            get => _comparer;
            set => SetValue(ref _comparer, value);
        }

        
        [Display(Order = 1, Name = "Имя", GroupName = "Основное", Description = "По этому имени автокоманду запускают командой !admin runauto, в том числе из других автокоманд, и голосованием !vote.")]
        public string Name
        {
            get => _name;
            set => SetValue(ref _name, value);
        }

        [Display(Order = 2, Name = "Интервал", GroupName = "Основное", Description = "Timed — период повтора, Scheduled — время суток запуска, Vote — длительность голосования, PlayerCount, GridCount и SimSpeed — пауза между проверками. Формат ЧЧ:ММ:СС, с днями д.ЧЧ:ММ:СС.")]
        public string Interval
        {
            get => _interval.ToString();
            set
            {
                _interval = TimeSpan.Parse(value);
                OnPropertyChanged();
                if (CommandTrigger == Trigger.Timed)
                {
                    _nextRun = DateTime.Now + _interval;
                }


                if (CommandTrigger == Trigger.Scheduled)
                {
                    _nextRun = DateTime.Now.Date + _interval;
                    if (_nextRun < DateTime.Now) _nextRun += TimeSpan.FromDays(1);
                }



            }
        }

        [Display(Order = 5, Name = "Доля", GroupName = "Основное", Description = "SimSpeed — порог скорости симуляции, Vote — доля голосов «за» от игроков онлайн. От 0 до 1, 0.5 — это 50%.")]
        public float TriggerRatio
        {
            get => _triggerRatio;
            set => SetValue(ref _triggerRatio, Math.Min(Math.Max(value, 0), 1));

        }
        
        [Display(Order = 4, Name = "Порог", GroupName = "Основное", Description = "GridCount и PlayerCount — число гридов или игроков, SimSpeed — сколько секунд условие должно держаться, Vote — через сколько секунд игрок может начать новое голосование.")]
        public double TriggerCount
        {
            get => _triggerCount;
            set => SetValue(ref _triggerCount, Math.Max(0, value));

        }

        [Display(Name = "День недели", GroupName = "Расписание", Description = "Для Scheduled: в какой день недели запускать в заданное время. All — каждый день.")]
        public DayOfWeek DayOfWeek
        {
            get => _day;
            set => SetValue(ref _day, value);
        }

        [Display(Order = 7, Name = "Шаги", GroupName = "Основное", Description = "Команды, которые выполняются по очереди, когда автокоманда срабатывает.")]
        public ObservableCollection<CommandStep> Steps { get; } = new ObservableCollection<CommandStep>();

        public AutoCommand()
        {
            Steps.CollectionChanged += (sender, args) => OnPropertyChanged();
        }

        public void Update()
        {
            if (DateTime.Now < _nextRun)
                return;

            switch (CommandTrigger)
            {
                case Trigger.GridCount:
                case Trigger.SimSpeed:
                case Trigger.PlayerCount:
                    RunNow();
                    _nextRun = DateTime.Now + _interval;
                    return;
                case Trigger.Scheduled when  DayOfWeek != DayOfWeek.All && DateTime.Now.DayOfWeek != (System.DayOfWeek)(int)DayOfWeek:
                    //adding one day because I can't be bothered to calculate exact interval
                    _nextRun += TimeSpan.FromDays(1);
                    return;
            }


            if (Steps.Count <= 0)
                return;

            var step = Steps[_currentStep];

            step.RunStep();
            _currentStep++;
            _nextRun += step.DelaySpan;

            if (_currentStep < Steps.Count) return;
            _currentStep = 0;
            _cTokenSource?.Dispose();
            _nextRun = _trigger == Trigger.Scheduled
                    ? DateTime.Now.Date + _interval + TimeSpan.FromDays(1)
                    : _nextRun = DateTime.Now + _interval;
        }



        public class CommandStep : ViewModel
        {
            internal TimeSpan DelaySpan;
            private string _command;

            [Display(Order = 1, Name = "Задержка", Description = "Пауза после этого шага перед следующим. Формат ЧЧ:ММ:СС.")]
            public string Delay
            {
                get => DelaySpan.ToString();
                set => SetValue(ref DelaySpan, TimeSpan.Parse(value));
            }

            [Display(Order = 2, Name = "Команда", Description = "Команда, которую выполняет сервер, например !say Рестарт через 5 минут.")]
            public string Command
            {
                get => _command;
                set => SetValue(ref _command, value);
            }

            public void RunStep()
            {
                if (((TorchServer)EssentialsPlugin.Instance.Torch).State != ServerState.Running)
                    return;

                if (string.IsNullOrEmpty(Command))
                    return;

                EssentialsPlugin.Instance.Torch.Invoke(() =>
                                                       {
                                                           var manager = EssentialsPlugin.Instance.Torch.CurrentSession.Managers.GetManager<CommandManager>();
                                                           manager?.HandleCommandFromServer(Command);
                                                       });
            }

            public override string ToString()
            {
                return Command;
            }
        }

        private CancellationTokenSource _cTokenSource;

        /// <summary>
        /// Runs the command and all steps immediately, in a new thread
        /// </summary>
        internal async void RunNow()
        {
            _cTokenSource = new CancellationTokenSource();
            var token = _cTokenSource.Token;
            _isRunning = true;
            var steps = new List<CommandStep>(Steps);
            await Task.Run(() =>
            {
                foreach (var step in steps)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    step.RunStep();
                    Thread.Sleep(step.DelaySpan);
                }
            }, token);
            
            _cTokenSource.Dispose();
            _isRunning = false;
        }

        internal void Cancel()
        {
            Log.Info($"Cancelling autocommand {_name}");
            _cTokenSource?.Cancel();
            _currentStep = 0;
            _nextRun = _trigger == Trigger.Scheduled
                ? DateTime.Now.Date + _interval + TimeSpan.FromDays(1)
                : _nextRun = DateTime.Now + _interval;
        }

        public override string ToString()
        {
            return $"{Name} : {_trigger.ToString()} : {Steps.Count}";
        }

        internal bool IsRunning()
        {
            return _currentStep > 0 || _isRunning;
        }
    }
    
    public enum Gtl
    {
        LessThan,
        GreaterThan,
        Equal
    }

    public enum Trigger
    {
        Disabled,
        GridCount,
        OnStart,
        PlayerCount,
        Scheduled,
        SimSpeed,
        Timed,
        Vote
    }

    public enum DayOfWeek
    {
        All = -1,
        Sunday = System.DayOfWeek.Sunday,
        Monday = System.DayOfWeek.Monday,
        Tuesday = System.DayOfWeek.Tuesday,
        Wednesday = System.DayOfWeek.Wednesday,
        Thursday = System.DayOfWeek.Thursday,
        Friday = System.DayOfWeek.Friday,
        Saturday = System.DayOfWeek.Saturday
    }
}
