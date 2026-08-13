namespace ErenshorFollow
{
    public sealed class FollowControlState
    {
        public bool GameplayReady;
        public bool Following;
        public string FollowTarget;
        public string FollowState;
        public bool Leading;
        public string LeaderName;
        public string LeadDestination;
        public string LeadState;
        public bool ExpeditionActive;
        public string ExpeditionState;
        public string ExpeditionLeader;
        public string ExpeditionDestination;
        public bool CanReturn;
        public string ReturnZone;
    }

    public static class FollowControlApi
    {
        public const int ApiVersion = 1;
        public const string ModuleId = "follow";
        public static bool HasDedicatedPanel { get { return false; } }
        public static bool IsPanelOpen { get { return false; } }

        public static FollowControlState GetBasicState()
        {
            FollowController.StatusSnapshot follow = FollowController.GetStatusSnapshot();
            LeaderController.StatusSnapshot lead = LeaderController.GetStatusSnapshot();
            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            return new FollowControlState
            {
                GameplayReady = SuiteUiPolicy.IsGameplayReady(),
                Following = follow.Active, FollowTarget = follow.TargetName, FollowState = follow.State.ToString(),
                Leading = lead.Active, LeaderName = lead.LeaderName, LeadDestination = lead.DestinationName, LeadState = lead.State.ToString(),
                ExpeditionActive = expedition.Active, ExpeditionState = expedition.State.ToString(), ExpeditionLeader = expedition.LeaderName,
                ExpeditionDestination = expedition.DestinationName, CanReturn = ExpeditionCoordinator.CanReturn(), ReturnZone = ExpeditionCoordinator.ReturnZoneName()
            };
        }

        public static string GetStatus()
        {
            FollowControlState s = GetBasicState();
            if (s.ExpeditionActive) return "Expedition: " + s.ExpeditionState + " -> " + (s.ExpeditionDestination ?? string.Empty);
            if (s.Leading) return "Lead: " + (s.LeaderName ?? string.Empty) + " -> " + (s.LeadDestination ?? string.Empty);
            if (s.Following) return "Following " + (s.FollowTarget ?? string.Empty) + " (" + (s.FollowState ?? string.Empty) + ")";
            return "Travel idle";
        }

        public static bool VerboseDiagnostics
        {
            get { return ErenshorFollowPlugin.Instance != null && ErenshorFollowPlugin.Instance.Settings != null && ErenshorFollowPlugin.Instance.Settings.DiagnosticsVerbose; }
        }

        public static bool TrySetSetting(string settingId, string value, out string failure)
        {
            ErenshorFollowPlugin plugin = ErenshorFollowPlugin.Instance;
            if (plugin == null) { failure = "Erenshor Follow is not loaded."; return false; }
            return plugin.TrySetControlSetting(settingId, value, out failure);
        }

        private static bool Queue(int action) { ErenshorFollowPlugin p = ErenshorFollowPlugin.Instance; return p != null && p.RequestControlAction(action); }
        public static bool TryStop() { return Queue(1); }
        public static bool TryPauseExpedition() { return ExpeditionCoordinator.IsActive && Queue(2); }
        public static bool TryResumeExpedition() { return ExpeditionCoordinator.IsActive && Queue(3); }
        public static bool TryCancelExpedition() { return ExpeditionCoordinator.IsActive && Queue(4); }
        public static bool TryReturn() { return ExpeditionCoordinator.CanReturn() && Queue(5); }
        public static bool OpenPanel() { return false; }
        public static bool ClosePanel() { return false; }
    }
}
