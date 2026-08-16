using UnityEngine;

namespace ErenshorFollow
{
    // One module-level quick-close owner for all Follow-retained surfaces. This prevents a single Escape
    // frame from closing both Sim Actions and the Expedition Status underneath it.
    internal static class FollowUiSurfaceRouter
    {
        internal static FollowUiSurfaceCandidate Topmost()
        {
            return FollowUiSurfacePolicy.SelectTopmost(
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.ExpeditionStatus,
                    TravelStatusOverlay.IsCloseableVisible, TravelStatusOverlay.ExpeditionCanvasSortOrder,
                    TravelStatusOverlay.LastActivatedAt),
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.SimActions,
                    SimActionMenu.IsOpen, SimActionMenu.CanvasSortOrder, SimActionMenu.LastActivatedAt),
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.ExpeditionSetup,
                    ExpeditionSetupWindow.IsOpen, ExpeditionSetupWindow.CanvasSortOrder, ExpeditionSetupWindow.LastActivatedAt));
        }

        internal static bool AnyCloseableOpen { get { return Topmost().Open; } }

        internal static bool CloseTopmost()
        {
            FollowUiSurfaceCandidate top = Topmost();
            switch (top.Kind)
            {
                case FollowUiSurfaceKind.ExpeditionSetup:
                    return ExpeditionSetupWindow.CloseForSharedQuickClose();
                case FollowUiSurfaceKind.SimActions:
                    return SimActionMenu.CloseForSharedQuickClose();
                case FollowUiSurfaceKind.ExpeditionStatus:
                    return TravelStatusOverlay.CloseForSharedQuickClose();
                default:
                    return false;
            }
        }

        // Shared Escape dismisses the whole Follow UI layer, never Follow/Expedition gameplay.
        // This is intentionally stronger than the old topmost-only behavior so one suite-level
        // Escape leaves only launchers/status-independent gameplay behind.
        internal static bool CloseAllVisuals()
        {
            bool closed = false;
            if (ExpeditionSetupWindow.IsOpen) closed = ExpeditionSetupWindow.CloseForSharedQuickClose() || closed;
            if (SimActionMenu.IsOpen) closed = SimActionMenu.CloseForSharedQuickClose() || closed;
            if (TravelStatusOverlay.IsCloseableVisible) closed = TravelStatusOverlay.CloseForSharedQuickClose() || closed;
            return closed;
        }

        internal static void TickLocalEscapeFallback()
        {
            if (!AnyCloseableOpen) return;
            if (!SuiteQuickCloseCompatibility.ShouldHandleEscapeLocally(true)) return;
            if (Input.GetKeyDown(KeyCode.Escape)) CloseAllVisuals();
        }
    }
}
