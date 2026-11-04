using NLog;
using Essentials.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
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
            if (!ConditionsChecker.TryScanConditions(Context, Context.Args, out var grids, out _))
                return;

            Context.Respond($"Найдено гридов по указанным условиям: {grids.Count}.");
        }

        [Command("list", "Показывает список гридов, подходящих под указанные условия")]
        public void List()
        {
            if (!ConditionsChecker.TryScanConditions(Context, Context.Args, out var grids, out _))
                return;

            var lines = String.Join("\n", grids.OrderBy(g => g.DisplayName).Select((g, i) =>
                $"{i + 1}. {g.DisplayName} (ID: {g.EntityId}, владелец: {GetOwnerName(g)}, блоков: {g.BlocksCount}, координаты: {Utilities.FormatGridCenter(g)})"));
            if (Context.SentBySelf)
            {
                Context.Respond(lines);
                Context.Respond($"Найдено гридов по указанным условиям: {grids.Count}.");
            }
            else
            {
                var m = new DialogMessage("Очистка", null, $"Найдено: {grids.Count}", lines);
                ModCommunication.SendMessageTo(m, Context.Player.SteamUserId);
            }
        }

        [Command("delete", "Удаляет гриды, подходящие под указанные условия. Без условий не запускается")]
        public void Delete()
        {
            if (Context.Args.Count == 0)
            {
                Context.Respond("Удаление без условий запрещено: оно удалит все гриды без пилотов. Список условий: !cleanup help");
                return;
            }

            if (!ConditionsChecker.TryScanConditions(Context, Context.Args, out var grids, out var excludedStations))
                return;

            foreach (var (station, groupSize) in excludedStations)
                Log.Info($"Excluded from deletion as a trade station: {station.EntityId}: {station.DisplayName}, owner: {GetOwnerName(station)}, position: {Utilities.FormatGridCenter(station)}, grids in group: {groupSize}");

            foreach (var grid in grids)
            {
                Log.Info($"Deleting grid: {grid.EntityId}: {grid.DisplayName}, owner: {GetOwnerName(grid)}, blocks: {grid.BlocksCount}, position: {Utilities.FormatGridCenter(grid)}");
                grid.Close();
            }

            Context.Respond($"Удалено гридов по указанным условиям: {grids.Count}.");
            Log.Info($"Cleanup deleted {grids.Count} grids matching conditions {string.Join(", ", Context.Args)}");
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
                sb.AppendLine($"    {c.HelpText}");
            }

            if (!Context.SentBySelf)
                ModCommunication.SendMessageTo(new DialogMessage("Справка по очистке", null, sb.ToString()), Context.Player.SteamUserId);
            else
                Context.Respond(sb.ToString());
        }

        private static string GetOwnerName(MyCubeGrid grid)
        {
            var ownerId = Ownership.GetOwner(grid);
            if (ownerId == 0)
                return "-";

            return MySession.Static.Players.TryGetIdentity(ownerId)?.DisplayName ?? ownerId.ToString();
        }
    }
}
