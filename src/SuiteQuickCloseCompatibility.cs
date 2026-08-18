namespace ErenshorFollow
{
    // Shared quick-close ownership gate. Three states, matching the current Forgotten Roads suite
    // contract:
    //   Hub absent                                  -> local Escape fallback allowed;
    //   Hub present but quick-close unverified      -> explicit X/close controls only, no Escape poll;
    //   Hub verified + this module's provider bound -> Hub owns Suite quick-close.
    // The middle state is the important one: while a Hub is loaded, no Forgotten Roads module may
    // independently poll Escape, or several modules compete for the same key press.
    internal static class SuiteQuickCloseCompatibility
    {
        internal static bool ShouldHandleEscapeLocally(bool ownedUiOpen)
        {
            ErenshorFollowPlugin plugin = ErenshorFollowPlugin.Instance;
            bool providerRegistered = plugin != null && plugin.SuiteQuickCloseProviderRegistered;
            return ShouldHandleEscapeLocally(ownedUiOpen, SuiteUiPolicy.IsHubPresent(),
                SuiteUiPolicy.IsHubQuickCloseVerified(), providerRegistered);
        }

        internal static bool ShouldHandleEscapeLocally(bool ownedUiOpen, bool hubPresent,
            bool verifiedHubQuickClose, bool providerRegistered)
        {
            return FollowQuickClosePolicy.ShouldHandleEscapeLocally(ownedUiOpen, hubPresent,
                verifiedHubQuickClose, providerRegistered);
        }

        internal static SuiteEscapeAuthority Resolve(bool hubPresent, bool verifiedHubQuickClose, bool providerRegistered)
        {
            return FollowQuickClosePolicy.Resolve(hubPresent, verifiedHubQuickClose, providerRegistered);
        }

        internal static void Reset() { }

        internal static string RunSelfTests()
        {
            if (!ShouldHandleEscapeLocally(true, false, false, false)) return "FAIL follow quick-close standalone fallback";
            if (ShouldHandleEscapeLocally(true, true, false, false)) return "FAIL follow quick-close hub present unverified must not poll Escape";
            if (ShouldHandleEscapeLocally(true, true, true, false)) return "FAIL follow quick-close missing module provider must not poll Escape";
            if (ShouldHandleEscapeLocally(true, true, false, true)) return "FAIL follow quick-close unverified Hub must not poll Escape";
            if (ShouldHandleEscapeLocally(true, true, true, true)) return "FAIL follow quick-close Hub ownership";
            if (ShouldHandleEscapeLocally(false, false, false, false)) return "FAIL follow quick-close closed ui";
            if (Resolve(false, false, false) != SuiteEscapeAuthority.StandaloneFallback) return "FAIL follow quick-close standalone authority";
            if (Resolve(true, false, true) != SuiteEscapeAuthority.ExplicitCloseControls) return "FAIL follow quick-close explicit-close authority";
            if (Resolve(true, true, true) != SuiteEscapeAuthority.HubVerified) return "FAIL follow quick-close hub authority";
            return "PASS follow quick-close fallback policy";
        }
    }
}
