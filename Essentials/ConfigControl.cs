using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Views;
using VRage.Game.ModAPI;

namespace Essentials
{
    /// <summary>
    /// A string setting edited in a multi-line text box (Enter makes a new line).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MultilineAttribute : Attribute
    {
    }

    /// <summary>
    /// The Torch page of the plugin, built from the [Display] attributes on EssentialsConfig. GroupName is "Tab" or "Tab/Box":
    /// one TabItem per tab, consecutive settings with the same box go into one GroupBox. A setting is a "Name (?)" label with
    /// the Description as tooltip and an editor chosen by property type; a setting with an EditorType and no Name gets that
    /// editor over the full width. A last tab lists the plugin's commands. The config is the DataContext, set by the plugin.
    /// </summary>
    public class ConfigControl : UserControl
    {
        // the window of Torch for a nested collection, such as the steps of an auto command, is 400 x 370
        private const double CollectionWindowWidth = 1200;
        private const double CollectionWindowHeight = 740;

        static ConfigControl()
        {
            // Torch makes these windows itself, so they are resized when they load
            EventManager.RegisterClassHandler(typeof(ObjectCollectionEditor), LoadedEvent, new RoutedEventHandler(OnCollectionWindowLoaded));
        }

        public ConfigControl()
        {
            // the labels column has the same width in every box of a tab
            var tabs = new TabControl { Margin = new Thickness(5) };
            Grid.SetIsSharedSizeScope(tabs, true);

            var settings = typeof(EssentialsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Property = p, Display = p.GetCustomAttribute<DisplayAttribute>() })
                .Where(s => s.Display != null && s.Display.Visible)
                .OrderBy(s => s.Display.Order)
                .Select(s =>
                {
                    var path = (s.Display.GroupName ?? "").Split(new[] { '/' }, 2);
                    return new { s.Property, s.Display, Tab = path[0], Box = path.Length > 1 ? path[1] : null };
                });

            foreach (var tab in settings.GroupBy(s => s.Tab))
            {
                var panel = new StackPanel { Margin = new Thickness(5) };
                Grid grid = null;
                string box = null;

                foreach (var setting in tab)
                {
                    if (grid == null || setting.Box != box)
                    {
                        box = setting.Box;
                        grid = CreateGrid();
                        if (box == null)
                        {
                            panel.Children.Add(grid);
                        }
                        else
                        {
                            panel.Children.Add(new GroupBox
                            {
                                Header = new TextBlock { Text = box, FontWeight = FontWeights.Bold },
                                Content = grid,
                                Margin = new Thickness(0, 0, 0, 5)
                            });
                        }
                    }

                    AddSetting(grid, setting.Property, setting.Display);
                }

                if (tab.Key == RanksPanel.TabName)
                    panel.Children.Add(new RanksPanel());

                // no own ScrollViewer: Torch already wraps the plugin page in one, which gives an inner viewer
                // infinite height (nothing to scroll) while it still swallows the mouse wheel
                tabs.Items.Add(new TabItem { Header = tab.Key, Content = panel });
            }

            tabs.Items.Add(CreateCommandsTab());
            Content = tabs;
        }

        internal static Grid CreateGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Labels" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        /// <param name="textTrigger">When a text box gives its text to the setting; check boxes and lists give it at once.</param>
        /// <returns>The editor.</returns>
        internal static FrameworkElement AddSetting(Grid grid, PropertyInfo property, DisplayAttribute display,
            UpdateSourceTrigger textTrigger = UpdateSourceTrigger.PropertyChanged)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var editor = CreateEditor(property, display, textTrigger);
            editor.IsEnabled = display.Enabled;
            SetToolTip(editor, display.Description);
            Grid.SetRow(editor, row);

            if (string.IsNullOrEmpty(display.Name))
            {
                Grid.SetColumnSpan(editor, 2);
            }
            else
            {
                var label = new TextBlock
                {
                    Text = display.Name + " (?)",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 10, 0)
                };
                SetToolTip(label, display.Description);
                Grid.SetRow(label, row);
                grid.Children.Add(label);
                Grid.SetColumn(editor, 1);
            }

            grid.Children.Add(editor);
            return editor;
        }

        private static FrameworkElement CreateEditor(PropertyInfo property, DisplayAttribute display, UpdateSourceTrigger textTrigger)
        {
            if (display.EditorType != null)
            {
                // the way the Torch property grid does it: the editor edits its DataContext
                var custom = (FrameworkElement)Activator.CreateInstance(display.EditorType);
                custom.SetBinding(DataContextProperty, property.Name);
                custom.Margin = new Thickness(5);
                HideInnerFilter(custom);
                custom.PreviewMouseWheel += ForwardMouseWheel;
                return custom;
            }

            // PropertyChanged so every edit reaches the config (and the autosave) without leaving the field
            var binding = new Binding(property.Name)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };

            if (property.PropertyType == typeof(bool))
            {
                var checkBox = new CheckBox { Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
                checkBox.SetBinding(ToggleButton.IsCheckedProperty, binding);
                return checkBox;
            }

            if (property.PropertyType.IsEnum)
            {
                var comboBox = new ComboBox { Margin = new Thickness(5), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
                comboBox.ItemsSource = Enum.GetValues(property.PropertyType);
                comboBox.SetBinding(Selector.SelectedItemProperty, binding);
                return comboBox;
            }

            // strings and numbers; an unparsable number, or a value the setter refuses with an exception, gets the red
            // validation border and is not applied
            binding.UpdateSourceTrigger = textTrigger;
            binding.ValidatesOnExceptions = true;
            var textBox = new TextBox { Margin = new Thickness(5), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
            if (property.GetCustomAttribute<MultilineAttribute>() != null)
            {
                textBox.MinWidth = 400;
                textBox.MinLines = 3;
                textBox.AcceptsReturn = true;
                textBox.TextWrapping = TextWrapping.Wrap;
                textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            textBox.SetBinding(TextBox.TextProperty, binding);
            return textBox;
        }

        // The collection editor of Torch shows the selected item in its own property grid with a "Filter:" search row.
        // The fields are internal, so they are reached by reflection; if Torch renames them, the row just stays.
        private static void HideInnerFilter(FrameworkElement editor)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var grid = editor.GetType().GetField("PGrid", flags)?.GetValue(editor);
            if (!(grid?.GetType().GetField("TbFilter", flags)?.GetValue(grid) is FrameworkElement filter))
                return;

            // the box shares a small grid with its "Filter:" label
            var row = LogicalTreeHelper.GetParent(filter) as Grid ?? (FrameworkElement)filter;
            row.Visibility = Visibility.Collapsed;
        }

        // The class handler sees the collection windows of every plugin, only the ones with items of this plugin are
        // resized. The size is kept inside the screen and the window is centered on its owner again.
        private static void OnCollectionWindowLoaded(object sender, RoutedEventArgs e)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            if (!(sender is Window window))
                return;

            var editor = window.GetType().GetField("Editor", flags)?.GetValue(window);
            var list = editor?.GetType().GetField("ElementList", flags)?.GetValue(editor) as ItemsControl;
            var itemType = list?.ItemsSource?.GetType().GenericTypeArguments.FirstOrDefault();
            if (itemType?.Assembly != typeof(ConfigControl).Assembly)
                return;

            var area = SystemParameters.WorkArea;
            window.Width = Math.Min(CollectionWindowWidth, area.Width);
            window.Height = Math.Min(CollectionWindowHeight, area.Height);

            var owner = window.Owner;
            if (owner == null || owner.WindowState == WindowState.Minimized)
                return;
            window.Left = Math.Max(area.Left, Math.Min(owner.Left + (owner.ActualWidth - window.Width) / 2, area.Right - window.Width));
            window.Top = Math.Max(area.Top, Math.Min(owner.Top + (owner.ActualHeight - window.Height) / 2, area.Bottom - window.Height));
        }

        // The editor grows with its list, so its inner ScrollViewers have nothing to scroll, yet they would still eat
        // the mouse wheel. It is passed on to the page that Torch scrolls.
        private static void ForwardMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || !(sender is UIElement element) || !(VisualTreeHelper.GetParent(element) is UIElement parent))
                return;

            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = element });
        }

        internal static void SetToolTip(FrameworkElement element, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            element.ToolTip = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 450 };
            ToolTipService.SetShowDuration(element, 60000);
        }

        // Read from the [Category], [Command] and [Permission] attributes the way Torch reads them when it registers
        // the commands (no [Permission] means admin), so the list follows the code. The info commands of the config
        // are not Torch commands, the chat messages are matched against them, so they are not listed.
        private static TabItem CreateCommandsTab()
        {
            var commands = typeof(ConfigControl).Assembly.GetExportedTypes()
                .Where(t => t.IsSubclassOf(typeof(CommandModule)))
                .SelectMany(t => t.GetMethods().Select(m => new { Category = t.GetCustomAttribute<CategoryAttribute>(), Method = m }))
                .Select(c => new
                {
                    c.Category,
                    Command = c.Method.GetCustomAttribute<CommandAttribute>(),
                    Level = c.Method.GetCustomAttribute<PermissionAttribute>()?.PromoteLevel ?? MyPromoteLevel.Admin
                })
                .Where(c => c.Command != null)
                .Select(c => new
                {
                    Text = "!" + string.Join(" ", (c.Category?.Path ?? new List<string>()).Concat(c.Command.Path)),
                    c.Command.Description,
                    c.Level
                })
                .OrderBy(c => c.Text)
                .ToList();

            // own width scope: the command column fits the commands, not the settings labels of the other tabs
            var panel = new StackPanel { Margin = new Thickness(5) };
            Grid.SetIsSharedSizeScope(panel, true);
            panel.Children.Add(CreateCommandsBox("Для игроков", commands.Where(c => c.Level == MyPromoteLevel.None).Select(c => (c.Text, c.Description))));
            panel.Children.Add(CreateCommandsBox("Для админов", commands.Where(c => c.Level != MyPromoteLevel.None)
                .Select(c => (c.Text, c.Level == MyPromoteLevel.Admin ? c.Description : $"{c.Description} (уровень: {c.Level})"))));

            return new TabItem { Header = "Команды", Content = panel };
        }

        // the command is a borderless read-only text box, so it can be selected and copied
        private static GroupBox CreateCommandsBox(string header, IEnumerable<(string Command, string Description)> commands)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Commands" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach ((string command, string description) in commands)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var name = new TextBox
                {
                    Text = command,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(5, 2, 10, 2)
                };
                var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 5, 3) };

                Grid.SetRow(name, row);
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 1);
                grid.Children.Add(name);
                grid.Children.Add(text);
            }

            return new GroupBox
            {
                Header = new TextBlock { Text = header, FontWeight = FontWeights.Bold },
                Content = grid,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }
    }
}
