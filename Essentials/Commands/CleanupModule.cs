using NLog;
using Sandbox.Game.Entities;
using System;
using System.Linq;
using System.Text;
using Torch.Commands;
using Torch.Mod;
using Torch.Mod.Messages;

namespace Essentials.Commands
{
    [Category("cleanup")]
    public class CleanupModule : CommandModule
    {
        private static readonly Logger Log = LogManager.GetLogger("Essentials");

        [Command("scan", "Считает гриды, подходящие под указанные условия")]
        public void Scan()
        {
            var count = ConditionsChecker.ScanConditions(Context, Context.Args).Count();
            Context.Respond($"Найдено гридов по указанным условиям: {count}.");
        }

        [Command("list", "Показывает список гридов, подходящих под указанные условия")]
        public void List()
        {
            var grids = ConditionsChecker.ScanConditions(Context, Context.Args).OrderBy(g => g.DisplayName).ToList();
            if (Context.SentBySelf)
            {
                Context.Respond(String.Join("\n", grids.Select((g, i) => $"{i + 1}. {grids[i].DisplayName} (блоков: {grids[i].BlocksCount})")));
                Context.Respond($"Найдено гридов по указанным условиям: {grids.Count}.");
            }
            else
            {
                var m = new DialogMessage("Очистка", null, $"Найдено: {grids.Count}", String.Join("\n", grids.Select((g, i) => $"{i + 1}. {grids[i].DisplayName} (блоков: {grids[i].BlocksCount})")));
                ModCommunication.SendMessageTo(m, Context.Player.SteamUserId);
            }
        }

        [Command("delete", "Удаляет гриды, подходящие под указанные условия")]
        public void Delete()
        {
            var count = 0;
            foreach (var grid in ConditionsChecker.ScanConditions(Context, Context.Args))
            {
                Log.Info($"Deleting grid: {grid.EntityId}: {grid.DisplayName}");
                EjectPilots(grid);
                grid.Close();
                count++;
            }

            Context.Respond($"Удалено гридов по указанным условиям: {count}.");
            Log.Info($"Cleanup deleted {count} grids matching conditions {string.Join(", ", Context.Args)}");
        }

        [Command("delete floatingobjects", "Удаляет летающие предметы")]
        public void FlObjDelete()
        {
            var count = 0;
            foreach (var floater in MyEntities.GetEntities().OfType<MyFloatingObject>())
            {
                Log.Info($"Deleting floating object: {floater.DisplayName}");
                floater.Close();
                count++;
            }
            Context.Respond($"Удалено летающих предметов: {count}.");
            Log.Info($"Cleanup deleted {count} floating objects");
        }

        [Command("help", "Показывает все условия очистки.")]
        public void Help()
        {
            var sb = new StringBuilder();
            foreach (var c in ConditionsChecker.GetAllConditions())
            {
                sb.AppendLine($"{c.Command}{(string.IsNullOrEmpty(c.InvertCommand) ? string.Empty : $" ({c.InvertCommand})")}:");
                sb.AppendLine($"   {c.HelpText}");
            }

            if (!Context.SentBySelf)
                ModCommunication.SendMessageTo(new DialogMessage("Справка по очистке", null, sb.ToString()), Context.Player.SteamUserId);
            else
                Context.Respond(sb.ToString());
        }

        /// <summary>
        /// Removes pilots from grid before deleting,
        /// so the character doesn't also get deleted and break everything
        /// </summary>
        /// <param name="grid"></param>
        public void EjectPilots(MyCubeGrid grid)
        {
            var b = grid.GetFatBlocks<MyCockpit>();
            foreach (var c in b)
            {
                c.RemovePilot();
            }
        }
    }
}
