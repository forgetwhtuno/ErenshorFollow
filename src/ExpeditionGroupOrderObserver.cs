using HarmonyLib;

namespace ErenshorFollow
{
    // Explicit native party orders beat expedition automation. These are postfix observers only: Follow
    // never invokes them, and never fights an order the player just gave.
    //
    // SimPlayerGrouping.GroupGuard / GroupFollow / RunAway are public parameterless methods in the
    // installed build and are the real handlers behind the group quick-commands.
    [HarmonyPatch(typeof(SimPlayerGrouping), "GroupGuard")]
    internal static class ExpeditionGroupGuardPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { if (ExpeditionCoordinator.IsActive) LeaderController.NoteMovementBoundary("GroupGuard.before"); }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                if (!ExpeditionCoordinator.IsActive) return;
                LeaderController.NoteMovementWriter("Native.GroupGuard");
                LeaderController.NoteMovementBoundary("GroupGuard.after");
                ExpeditionCoordinator.Pause(ExpeditionPauseReason.PlayerGroupOrder);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(SimPlayerGrouping), "GroupFollow")]
    internal static class ExpeditionGroupFollowPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { if (ExpeditionCoordinator.IsActive) LeaderController.NoteMovementBoundary("GroupFollow.before"); }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                if (ExpeditionCoordinator.IsActive)
                {
                    LeaderController.NoteMovementWriter("Native.GroupFollow");
                    LeaderController.NoteMovementBoundary("GroupFollow.after");
                }
                // Only undoes the pause that a Guard order caused. Follow issued for any other reason is
                // left alone rather than silently restarting an outing the player did not ask to resume.
                ExpeditionStatusSnapshot status = ExpeditionCoordinator.GetStatusSnapshot();
                if (status.Active && status.State == ExpeditionState.Paused &&
                    status.PauseReason == ExpeditionPauseReason.PlayerGroupOrder)
                    ExpeditionCoordinator.Resume();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(SimPlayerGrouping), "RunAway")]
    internal static class ExpeditionRunAwayPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { if (ExpeditionCoordinator.IsActive) LeaderController.NoteMovementBoundary("RunAway.before"); }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                // Run Away deliberately drives the group across a zone boundary to escape. Whatever zone
                // results is an emergency outcome, not an expedition arrival.
                if (ExpeditionCoordinator.IsActive)
                {
                    LeaderController.NoteMovementWriter("Native.RunAway");
                    LeaderController.NoteMovementBoundary("RunAway.after");
                    ExpeditionCoordinator.NoteExternalOverride();
                }
            }
            catch { }
        }
    }
}
