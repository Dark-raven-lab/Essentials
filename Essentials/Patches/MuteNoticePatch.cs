using System;
using System.Reflection;
using NLog;
using Torch.Managers.ChatManager;
using Torch.Managers.PatchManager;
using VRage.Network;

namespace Essentials.Patches
{
    /// <summary>
    /// Torch checks its mute list at the start of this method and gives plugins no event for a dropped message.
    /// </summary>
    public static class MuteNoticePatch
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            //the types are needed: the base class has a method with the same name
            var target = typeof(ChatManagerServer).GetMethod("RaiseMessageRecieved", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(ChatMsg), typeof(bool).MakeByRefType() }, null);
            if (target == null)
            {
                Log.Error("Chat method not found, muted players won't be told about their mute");
                return;
            }

            ctx.GetPattern(target).Prefixes.Add(typeof(MuteNoticePatch).GetMethod(nameof(NotifyMutedAuthor), BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static void NotifyMutedAuthor(ChatMsg message)
        {
            //an exception here would lose the message of a player who isn't muted
            try
            {
                EssentialsPlugin.Instance.Mutes.NotifyIfMuted(message.Author);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to tell a player about their mute");
            }
        }
    }
}
