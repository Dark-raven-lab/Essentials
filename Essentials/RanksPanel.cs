using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Newtonsoft.Json;
using Torch.Views;

namespace Essentials
{
    /// <summary>
    /// The ranks and their players, under the rank settings of the plugin page. The page shows copies of the data, taken
    /// on the game thread while the world runs, and every edit is a <see cref="RankOperations"/> call by name, so it never
    /// touches the lists the game reads. It is refreshed whenever the ranks or the accounts are saved, by the page or by
    /// the !pr commands, and when the world loads or unloads.
    /// </summary>
    public class RanksPanel : StackPanel
    {
        public const string TabName = "Ранги";

        private const string AllRanks = "Все ранги";

        private readonly ListBox _rankList = new ListBox { Width = 220, Height = 300, Margin = new Thickness(5) };
        private readonly TextBox _newRank = new TextBox { Width = 130, Margin = new Thickness(5, 0, 5, 0) };
        private readonly Grid _editor = ConfigControl.CreateGrid();
        private readonly TextBlock _offline = new TextBlock
        {
            Text = "Сервер не запущен: ранги можно настраивать, а резервный слот, уровень прав и ранги игроков меняются только при запущенном сервере.",
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(5)
        };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(5) };
        private readonly ComboBox _filter = new ComboBox { Width = 180, Margin = new Thickness(5, 0, 15, 0) };
        private readonly TextBox _search = new TextBox { Width = 200, Margin = new Thickness(5, 0, 15, 0) };
        private readonly TextBlock _shown = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        private readonly DataGrid _members = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 400,
            Margin = new Thickness(5)
        };
        private readonly DataGridComboBoxColumn _rankColumn = new DataGridComboBoxColumn { Header = "Ранг", Width = 200 };

        private Dictionary<string, RankView> _views = new Dictionary<string, RankView>();
        private string _selected;
        private string _ranksJson;
        private string _membersJson;
        private bool? _live;
        private volatile bool _refreshQueued;
        private volatile bool _force;

        public RanksPanel()
        {
            Children.Add(CreateRanksBox());
            Children.Add(CreateMembersBox());

            Loaded += (s, e) => RequestRefresh();
            RankOperations.Changed += () => RequestRefresh();
        }

        private GroupBox CreateRanksBox()
        {
            foreach (var property in typeof(RankView).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var display = property.GetCustomAttribute<DisplayAttribute>();
                if (display == null)
                    continue;
                //a text is applied when the field is left, a half-typed name would rename the rank at every key
                var editor = ConfigControl.AddSetting(_editor, property, display, UpdateSourceTrigger.LostFocus);
                if (property.GetCustomAttribute<NeedsServerAttribute>() != null)
                    editor.SetBinding(IsEnabledProperty, nameof(RankView.IsLive));
            }
            _editor.Visibility = Visibility.Collapsed;

            _rankList.SelectionChanged += (s, e) =>
            {
                var item = _rankList.SelectedItem as RankItem;
                if (item != null)
                    _selected = item.Name;
                _editor.DataContext = item != null && _views.TryGetValue(item.Name, out var view) ? view : null;
                _editor.Visibility = _editor.DataContext == null ? Visibility.Collapsed : Visibility.Visible;
            };

            var create = new Button { Content = "Создать", Padding = new Thickness(8, 1, 8, 1) };
            create.Click += (s, e) =>
            {
                var name = _newRank.Text.Trim();
                _selected = name;
                _newRank.Text = "";
                Execute(() => RankOperations.CreateRank(name));
            };
            var delete = new Button { Content = "Удалить выбранный", Margin = new Thickness(5), Padding = new Thickness(8, 1, 8, 1) };
            delete.Click += (s, e) =>
            {
                var name = (_rankList.SelectedItem as RankItem)?.Name;
                if (name == null)
                    return;
                //owned by the Torch window, so it does not open behind it
                if (MessageBox.Show(Window.GetWindow(this), $"Удалить ранг '{name}'? Его игроки перейдут на ранг по умолчанию.", "Essentials", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                Execute(() => RankOperations.DeleteRank(name));
            };

            var newRow = new StackPanel { Orientation = Orientation.Horizontal };
            newRow.Children.Add(_newRank);
            newRow.Children.Add(create);
            ConfigControl.SetToolTip(_newRank, "Имя нового ранга.");

            var left = new StackPanel();
            left.Children.Add(_rankList);
            left.Children.Add(newRow);
            left.Children.Add(delete);

            //own width scope: the labels column fits the rank fields, not the settings above
            var grid = new Grid();
            Grid.SetIsSharedSizeScope(grid, true);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_editor, 1);
            grid.Children.Add(left);
            grid.Children.Add(_editor);

            var panel = new StackPanel();
            panel.Children.Add(grid);
            panel.Children.Add(_offline);
            panel.Children.Add(_status);
            return CreateBox("Список рангов", panel);
        }

        private GroupBox CreateMembersBox()
        {
            _members.Columns.Add(new DataGridTextColumn { Header = "Ник", Binding = new Binding(nameof(MemberView.Name)) { Mode = BindingMode.OneWay }, IsReadOnly = true, Width = 250 });
            _members.Columns.Add(new DataGridTextColumn { Header = "SteamID", Binding = new Binding(nameof(MemberView.SteamId)) { Mode = BindingMode.OneWay }, IsReadOnly = true, Width = 160 });
            _rankColumn.SelectedItemBinding = new Binding(nameof(MemberView.Rank)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
            //the rank is applied once it is picked in the list, not only when the row is left
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new EventSetter(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) =>
            {
                if (((ComboBox)s).IsDropDownOpen)
                    Dispatcher.BeginInvoke(new Action(() => _members.CommitEdit(DataGridEditingUnit.Row, true)), DispatcherPriority.Background);
            })));
            _rankColumn.EditingElementStyle = style;
            _members.Columns.Add(_rankColumn);

            _filter.SelectionChanged += (s, e) => ApplyFilter();
            _search.TextChanged += (s, e) => ApplyFilter();

            var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
            filters.Children.Add(new TextBlock { Text = "Ранг:", VerticalAlignment = VerticalAlignment.Center });
            filters.Children.Add(_filter);
            filters.Children.Add(new TextBlock { Text = "Поиск:", VerticalAlignment = VerticalAlignment.Center });
            filters.Children.Add(_search);
            filters.Children.Add(_shown);
            ConfigControl.SetToolTip(_search, "Часть ника или SteamID.");

            var panel = new StackPanel();
            panel.Children.Add(filters);
            panel.Children.Add(_members);
            return CreateBox("Игроки", panel);
        }

        private static GroupBox CreateBox(string header, UIElement content)
        {
            return new GroupBox
            {
                Header = new TextBlock { Text = header, FontWeight = FontWeights.Bold },
                Content = content,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        /// <summary>
        /// Runs a change and shows its answer. The page is refreshed after it even when nothing was saved, so a refused
        /// value goes back to the stored one.
        /// </summary>
        private void Execute(Func<string> change)
        {
            RankOperations.Run(() =>
            {
                var message = change();
                Dispatcher.BeginInvoke(new Action(() => _status.Text = message));
                RequestRefresh(true);
            });
        }

        /// <summary>
        /// Takes the copies where the data is changed (the game thread while the world runs) and shows them on the page.
        /// Called from any thread; requests made before the copies are taken are served by the same copies.
        /// </summary>
        private void RequestRefresh(bool force = false)
        {
            if (force)
                _force = true;
            if (_refreshQueued)
                return;
            _refreshQueued = true;

            RankOperations.Run(() =>
            {
                string ranks, members;
                bool live;
                try
                {
                    ranks = JsonConvert.SerializeObject(RanksAndPermissionsModule.Ranks);
                    members = JsonConvert.SerializeObject(PlayerAccountModule.PlayersAccounts
                        .Select(a => new MemberData { Name = a.Player, SteamId = a.SteamID, Rank = a.Rank }));
                    live = RankOperations.IsLive;
                }
                finally
                {
                    _refreshQueued = false;
                }
                Dispatcher.BeginInvoke(new Action(() => Show(ranks, members, live)));
            });
        }

        private void Show(string ranksJson, string membersJson, bool live)
        {
            var force = _force;
            _force = false;
            if (!force && ranksJson == _ranksJson && membersJson == _membersJson && live == _live)
                return;
            _ranksJson = ranksJson;
            _membersJson = membersJson;
            _live = live;

            var ranks = JsonConvert.DeserializeObject<List<RanksAndPermissionsModule.RankData>>(ranksJson) ?? new List<RanksAndPermissionsModule.RankData>();
            var members = JsonConvert.DeserializeObject<List<MemberData>>(membersJson) ?? new List<MemberData>();
            //a hand-edited file can hold a name twice, the game uses the first one
            ranks = ranks.Where(r => r?.RankName != null).GroupBy(r => r.RankName).Select(g => g.First()).ToList();
            var names = ranks.Select(r => r.RankName).ToArray();

            _offline.Visibility = live ? Visibility.Collapsed : Visibility.Visible;

            _views = ranks.ToDictionary(r => r.RankName, r => new RankView(this, r, names, live));
            var items = ranks.Select(r => new RankItem(r.RankName, members.Count(m => m.Rank == r.RankName))).ToList();
            var selected = _selected;
            _rankList.ItemsSource = items;
            _rankList.SelectedItem = items.FirstOrDefault(i => i.Name == selected) ?? items.FirstOrDefault();

            var filter = _filter.SelectedItem as string;
            var filters = new[] { AllRanks }.Concat(names).ToList();
            _filter.ItemsSource = filters;
            _filter.SelectedItem = filters.Contains(filter) ? filter : AllRanks;

            //the list is replaced, so a row still being edited is finished first
            _members.CommitEdit(DataGridEditingUnit.Row, true);
            _rankColumn.ItemsSource = names;
            _rankColumn.IsReadOnly = !live;
            _members.ItemsSource = members.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).Select(m => new MemberView(this, m)).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (!(_members.ItemsSource is List<MemberView> all))
                return;

            var rank = _filter.SelectedItem as string;
            var text = _search.Text.Trim();
            var view = CollectionViewSource.GetDefaultView(all);
            view.Filter = o =>
            {
                var member = (MemberView)o;
                return (rank == null || rank == AllRanks || member.Rank == rank)
                       && (text.Length == 0
                           || (member.Name ?? "").IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0
                           || member.SteamId.ToString().Contains(text));
            };
            _shown.Text = $"Показано: {view.Cast<object>().Count()} из {all.Count}";
        }

        /// <summary>The fields the game must run for: the panel disables them while it does not.</summary>
        [AttributeUsage(AttributeTargets.Property)]
        private sealed class NeedsServerAttribute : Attribute
        {
        }

        internal sealed class MemberData
        {
            public string Name;
            public ulong SteamId;
            public string Rank;
        }

        private sealed class RankItem
        {
            public RankItem(string name, int members)
            {
                Name = name;
                Members = members;
            }

            public string Name { get; }
            public int Members { get; }

            public override string ToString() => $"{Name} ({Members})";
        }

        public sealed class MemberView
        {
            private readonly RanksPanel _panel;
            private readonly MemberData _data;

            internal MemberView(RanksPanel panel, MemberData data)
            {
                _panel = panel;
                _data = data;
            }

            public string Name => _data.Name;
            public ulong SteamId => _data.SteamId;

            public string Rank
            {
                get => _data.Rank;
                set
                {
                    if (value == null || value == _data.Rank)
                        return;
                    var steamId = _data.SteamId;
                    _panel.Execute(() => RankOperations.SetPlayerRank(steamId, value));
                }
            }
        }

        /// <summary>
        /// One rank in the editor. A setter sends the change and leaves the copy as it is, the refresh after the change
        /// brings the stored value.
        /// </summary>
        public sealed class RankView
        {
            private static readonly char[] Separators = { ',', '\r', '\n' };

            private readonly RanksPanel _panel;
            private readonly RanksAndPermissionsModule.RankData _data;
            private readonly string[] _names;

            internal RankView(RanksPanel panel, RanksAndPermissionsModule.RankData data, string[] names, bool live)
            {
                _panel = panel;
                _data = data;
                _names = names;
                IsLive = live;
            }

            public bool IsLive { get; }

            private string RankName => _data.RankName;

            [Display(Order = 1, Name = "Имя", Description = "Переименование меняет ранг и у игроков, и в наследовании других рангов, и в ранге по умолчанию.")]
            public string Name
            {
                get => _data.RankName;
                set
                {
                    value = value?.Trim();
                    if (value == RankName)
                        return;
                    if (string.IsNullOrEmpty(value))
                        throw Refuse("Укажите имя ранга.");
                    if (_names.Contains(value))
                        throw Refuse($"Ранг '{value}' уже существует.");
                    var old = RankName;
                    _panel._selected = value;
                    _panel.Execute(() => RankOperations.RenameRank(old, value));
                }
            }

            [Display(Order = 2, Name = "Префикс", Description = "Ставится перед ником в чате через пробел, например [VIP].")]
            public string Prefix
            {
                get => _data.Prefix;
                set
                {
                    value = value?.Trim() ?? "";
                    if (value != (_data.Prefix ?? "").Trim())
                        Send(name => RankOperations.SetPrefix(name, value));
                }
            }

            [Display(Order = 3, Name = "Показывать префикс", Description = "Показывать префикс ранга в чате.")]
            public bool DisplayPrefix
            {
                get => _data.DisplayPrefix;
                set
                {
                    if (value != _data.DisplayPrefix)
                        Send(name => RankOperations.SetShowPrefix(name, value));
                }
            }

            [Display(Order = 4, Name = "Домов", Description = "Сколько домов (!home) могут сохранить игроки ранга.")]
            public int MaxHomes
            {
                get => _data.MaxHomes;
                set
                {
                    if (value == _data.MaxHomes)
                        return;
                    if (value < 0)
                        throw Refuse("Число домов не может быть отрицательным.");
                    Send(name => RankOperations.SetMaxHomes(name, value));
                }
            }

            [Display(Order = 5, Name = "Бонус PCU", Description = "Сколько PCU добавляется к лимиту игроков ранга. Действует, когда в мире лимиты блоков «на игрока» и передача PCU выключена. При выключенных рангах снимается у всех. Без запущенного сервера игроки получат новый бонус при запуске.")]
            public int PcuBonus
            {
                get => _data.PcuBonus;
                set
                {
                    if (value == _data.PcuBonus)
                        return;
                    if (value < 0)
                        throw Refuse("Бонус PCU не может быть отрицательным.");
                    Send(name => RankOperations.SetPcuBonus(name, value));
                }
            }

            [NeedsServer]
            [Display(Order = 6, Name = "Резервный слот", Description = "Игроки ранга заходят на полный сервер. Сразу меняет список резерва для всех игроков ранга. Меняется только при запущенном сервере.")]
            public bool ReservedSlot
            {
                get => _data.ReservedSlot;
                set
                {
                    if (value != _data.ReservedSlot)
                        Send(name => RankOperations.SetReservedSlot(name, value));
                }
            }

            [NeedsServer]
            [Display(Order = 7, Name = "Уровень прав", Description = "Уровень прав Space Engineers. Сразу даётся всем игрокам ранга, а при смене ранга игроку — ему. Scripter сейчас даёт SpaceMaster. Меняется только при запущенном сервере.")]
            public RankOperations.Level Level
            {
                get => Enum.TryParse(_data.KeenLevelRank, out RankOperations.Level level) ? level : RankOperations.Level.None;
                set
                {
                    if (value != Level)
                        Send(name => RankOperations.SetLevel(name, value.ToString()));
                }
            }

            [Display(Order = 8, Name = "Наследует", Description = "Ранги через запятую, чьи права получает этот ранг. Свои права ранга главнее наследуемых.")]
            public string Inherits
            {
                get => Format(_data.Inherits);
                set
                {
                    var list = Parse(value, false);
                    if (list.SequenceEqual(_data.Inherits))
                        return;
                    var unknown = list.Where(n => !_names.Contains(n)).ToList();
                    if (unknown.Count > 0)
                        throw Refuse($"Таких рангов нет: {string.Join(", ", unknown)}.");
                    if (list.Contains(RankName))
                        throw Refuse("Ранг не может наследовать сам себя.");
                    Send(name => RankOperations.SetInherits(name, list));
                }
            }

            [Multiline]
            [Display(Order = 9, Name = "Разрешённые команды", Description = "Команды без !, через запятую или с новой строки, например motd, pr listranks. * — все команды.")]
            public string Allowed
            {
                get => Format(_data.Permissions.Allowed);
                set => SetPermissions(Parse(value, true), _data.Permissions.Disallowed);
            }

            [Multiline]
            [Display(Order = 10, Name = "Запрещённые команды", Description = "Команды без !, через запятую или с новой строки. * — все команды, кроме разрешённых. Запрет главнее разрешения.")]
            public string Disallowed
            {
                get => Format(_data.Permissions.Disallowed);
                set => SetPermissions(_data.Permissions.Allowed, Parse(value, true));
            }

            private void SetPermissions(List<string> allowed, List<string> disallowed)
            {
                if (allowed.SequenceEqual(_data.Permissions.Allowed) && disallowed.SequenceEqual(_data.Permissions.Disallowed))
                    return;
                Send(name => RankOperations.SetPermissions(name, allowed, disallowed));
            }

            private void Send(Func<string, string> change)
            {
                var name = RankName;
                _panel.Execute(() => change(name));
            }

            private ArgumentException Refuse(string message)
            {
                _panel._status.Text = message;
                return new ArgumentException(message);
            }

            private static string Format(IEnumerable<string> items) => string.Join(", ", items);

            //commands are written as they are typed in chat, so a leading ! is dropped
            private static List<string> Parse(string text, bool commands)
            {
                return (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => commands ? s.Trim().TrimStart('!').Trim() : s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .ToList();
            }
        }
    }
}
