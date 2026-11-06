using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Views;
using VRage.Game.ModAPI;

namespace Essentials
{
    /// <summary>
    /// The Torch page of the plugin: the settings grid as before, and a tab that lists the plugin's commands.
    /// The settings grid takes the config from the DataContext of this control, which the plugin sets.
    /// </summary>
    public class ConfigControl : UserControl
    {
        public ConfigControl()
        {
            // no own ScrollViewer: Torch already wraps the plugin page in one, an inner viewer would swallow the mouse wheel
            var tabs = new TabControl { Margin = new Thickness(5) };
            tabs.Items.Add(new TabItem { Header = "Настройки", Content = new PropertyGrid() });
            tabs.Items.Add(CreateCommandsTab());
            Content = tabs;
        }

        // Read from the [Category], [Command] and [Permission] attributes the way Torch reads them when it registers
        // the commands (no [Permission] means admin), so the list follows the code. The info commands made from the
        // config are registered at run time and are not listed.
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

            // own width scope: the command column fits the commands of both boxes
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
