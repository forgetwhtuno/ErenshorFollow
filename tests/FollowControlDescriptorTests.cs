using System;

namespace ErenshorFollow
{
    internal static class FollowControlDescriptorTests
    {
        private static int Main()
        {
            int failed = 0;
            failed += Check("verbose true descriptor exact bool wire",
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(true), "value") == "true" &&
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(true), "type") == "bool");
            failed += Check("verbose false descriptor exact bool wire",
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(false), "value") == "false");
            failed += Check("developer tier explicit",
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(false), "tier") == "developer");

            string normalized;
            failed += Check("mutable verbose setting routes",
                FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "TRUE", out normalized) && normalized == "true" &&
                FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "false", out normalized) && normalized == "false");
            failed += Check("invalid bool rejected",
                !FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "1", out normalized) &&
                !FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "on", out normalized));
            failed += Check("unadvertised setting rejected",
                !FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("overlayPositionX", "50", out normalized));

            string describe = FollowSuiteDescriptorPolicy.BuildDescribe("0.6.0", new string('s', 500));
            failed += Check("status bounded", Field(describe, "status").Length == FollowSuiteDescriptorPolicy.MaxHubText);
            failed += Check("no private or secret fields exposed",
                !FollowSuiteDescriptorPolicy.ContainsSensitiveFieldName(describe) &&
                !FollowSuiteDescriptorPolicy.ContainsSensitiveFieldName(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(true)));
            failed += Check("safe action allowlist advertises contextual close",
                Field(describe, "actions") == "closePanel,stop,pauseExpedition,resumeExpedition,cancelExpedition,return");

            SuiteHubPresenceState ordinary = SuiteHubPresencePolicy.Parse(
                "protocol=1&module=suitehub&status=Ready&uiAvailable=true&quickCloseContract=1&quickClose=0");
            SuiteHubPresenceState verified = SuiteHubPresencePolicy.Parse(
                "protocol=1&module=suitehub&status=Ready&uiAvailable=true&quickCloseContract=1&quickClose=1");
            failed += Check("Hub usability does not imply quick-close", ordinary.Usable && !ordinary.QuickCloseVerified);
            failed += Check("Hub quick-close requires both verified capability fields", verified.Usable && verified.QuickCloseVerified);
            failed += Check("malformed or duplicate Hub presence fails closed",
                !SuiteHubPresencePolicy.Parse("protocol=1&module=suitehub&module=suitehub&status=Ready&uiAvailable=true").Usable &&
                !SuiteHubPresencePolicy.Parse("protocol=2&module=suitehub&status=Ready&uiAvailable=true&quickCloseContract=1&quickClose=1").Usable);

            failed += Check("standalone Escape fallback ownership",
                FollowQuickClosePolicy.ShouldHandleEscapeLocally(true, false, false) &&
                FollowQuickClosePolicy.ShouldHandleEscapeLocally(true, true, false) &&
                FollowQuickClosePolicy.ShouldHandleEscapeLocally(true, false, true) &&
                !FollowQuickClosePolicy.ShouldHandleEscapeLocally(true, true, true) &&
                !FollowQuickClosePolicy.ShouldHandleEscapeLocally(false, true, true));

            string uiState = SuiteUiStatePolicy.Build("follow", true, 530, 12.3456);
            failed += Check("ui.state exact closeable wire",
                Field(uiState, "protocol") == "1" && Field(uiState, "module") == "follow" &&
                Field(uiState, "open") == "true" && Field(uiState, "closeable") == "true" &&
                Field(uiState, "sortOrder") == "530" && Field(uiState, "activated") == "12.346");
            failed += Check("ui.state clamps unsafe values",
                Field(SuiteUiStatePolicy.Build("follow", false, 50000, double.NaN), "sortOrder") == "10000" &&
                Field(SuiteUiStatePolicy.Build("follow", false, 50000, double.NaN), "activated") == "0");

            FollowUiSurfaceCandidate topSetup = FollowUiSurfacePolicy.SelectTopmost(
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.ExpeditionStatus, true, 520, 50d),
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.SimActions, true, 530, 40d),
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.ExpeditionSetup, true, 540, 30d));
            failed += Check("Follow quick-close chooses highest retained surface",
                topSetup.Kind == FollowUiSurfaceKind.ExpeditionSetup);
            FollowUiSurfaceCandidate topRecent = FollowUiSurfacePolicy.SelectTopmost(
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.ExpeditionStatus, true, 520, 10d),
                new FollowUiSurfaceCandidate(FollowUiSurfaceKind.SimActions, true, 520, 20d));
            failed += Check("same-sort Follow quick-close chooses most recently activated surface",
                topRecent.Kind == FollowUiSurfaceKind.SimActions);

            failed += Check("actor eligibility fails closed",
                FollowActorEligibilityPolicy.Evaluate(true, false, true) == FollowActorEligibility.Eligible &&
                FollowActorEligibilityPolicy.Evaluate(false, false, true) == FollowActorEligibility.MissingOrDead &&
                FollowActorEligibilityPolicy.Evaluate(true, true, true) == FollowActorEligibility.RemoteAuthority &&
                FollowActorEligibilityPolicy.Evaluate(true, false, false) == FollowActorEligibility.LeftParty);

            failed += Check("repeated/switch follow releases prior movement ownership before rebinding",
                FollowStartTransitionPolicy.ShouldReleaseMovementBeforeStart(true) &&
                !FollowStartTransitionPolicy.ShouldReleaseMovementBeforeStart(false));
            failed += Check("generic stop action is offered only for ordinary active Follow/Lead",
                FollowStartTransitionPolicy.ShouldOfferGenericStop(false, true, false) &&
                FollowStartTransitionPolicy.ShouldOfferGenericStop(false, false, true) &&
                !FollowStartTransitionPolicy.ShouldOfferGenericStop(false, false, false) &&
                !FollowStartTransitionPolicy.ShouldOfferGenericStop(true, true, true));

            failed += Check("legacy pixel UI positions recover instead of clamping as normalized",
                FollowUiPositionPolicy.InterpretStoredAxis(18f) == FollowUiPositionPolicy.Unset &&
                FollowUiPositionPolicy.InterpretStoredAxis(140f) == FollowUiPositionPolicy.Unset &&
                Math.Abs(FollowUiPositionPolicy.InterpretStoredAxis(0.35f) - 0.35f) < 0.0001f);
            failed += Check("normalized UI geometry remains bounded",
                Math.Abs(FollowUiPositionPolicy.ResolveAxis(0.5f, 0.1f, 1000f, 300f) - 500f) < 0.001f &&
                Math.Abs(FollowUiPositionPolicy.ResolveAxis(0.95f, 0.1f, 1000f, 300f) - 700f) < 0.001f &&
                Math.Abs(FollowUiPositionPolicy.NormalizeAxis(250f, 1000f) - 0.25f) < 0.001f);

            Console.WriteLine(failed == 0 ? "Follow control descriptor tests: ALL PASS" : "Follow control descriptor tests: FAILURES=" + failed);
            return failed == 0 ? 0 : 1;
        }

        private static int Check(string name, bool ok)
        {
            Console.WriteLine("[Follow ControlApi] " + name + ": " + (ok ? "PASS" : "FAIL"));
            return ok ? 0 : 1;
        }

        private static string Field(string line, string key)
        {
            string[] pairs = (line ?? string.Empty).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = Uri.UnescapeDataString(pairs[i].Substring(0, eq));
                if (k == key) return Uri.UnescapeDataString(pairs[i].Substring(eq + 1));
            }
            return string.Empty;
        }
    }
}
