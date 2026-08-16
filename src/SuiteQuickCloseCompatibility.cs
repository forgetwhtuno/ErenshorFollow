namespace ErenshorFollow
{
    // Shared quick-close fallback gate. The module only gives Escape ownership to Hub when the
    // current presence payload advertises a verified quickClose binding AND this module's own
    // ui.state/closePanel provider registered successfully.
    internal static class SuiteQuickCloseCompatibility
    {
        internal static bool ShouldHandleEscapeLocally(bool ownedUiOpen)
        {
            ErenshorFollowPlugin plugin = ErenshorFollowPlugin.Instance;
            bool providerRegistered = plugin != null && plugin.SuiteQuickCloseProviderRegistered;
            return ShouldHandleEscapeLocally(ownedUiOpen, SuiteUiPolicy.IsHubQuickCloseVerified(), providerRegistered);
        }

        internal static bool ShouldHandleEscapeLocally(bool ownedUiOpen, bool verifiedHubQuickClose, bool providerRegistered)
        {
            return FollowQuickClosePolicy.ShouldHandleEscapeLocally(ownedUiOpen, verifiedHubQuickClose, providerRegistered);
        }

        internal static void Reset() { }

        internal static string RunSelfTests()
        {
            if (!ShouldHandleEscapeLocally(true, false, false)) return "FAIL follow quick-close standalone fallback";
            if (!ShouldHandleEscapeLocally(true, true, false)) return "FAIL follow quick-close missing module provider fallback";
            if (!ShouldHandleEscapeLocally(true, false, true)) return "FAIL follow quick-close unverified Hub fallback";
            if (ShouldHandleEscapeLocally(true, true, true)) return "FAIL follow quick-close Hub ownership";
            if (ShouldHandleEscapeLocally(false, true, true)) return "FAIL follow quick-close closed ui";
            return "PASS follow quick-close fallback policy";
        }
    }
}
