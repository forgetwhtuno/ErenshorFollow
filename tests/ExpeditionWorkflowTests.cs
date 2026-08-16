using System;

namespace ErenshorFollow
{
    // ExpeditionModels carries these runtime references, but workflow policy itself is Unity/game independent.
    internal sealed class SimPlayer { }
    internal sealed class SimPlayerTracking { }
    internal sealed class Zoneline { }

    internal static class ExpeditionWorkflowTests
    {
        private static int _passed;
        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void StartAdmission()
        {
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, true, true, true, true, false, 3) == ExpeditionStartAdmission.Allowed,
                "valid exact local party leader and multi-hop route admitted");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(true, true, true, true, true, false, 3) == ExpeditionStartAdmission.AlreadyActive,
                "second expedition rejected");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, false, false, false, false, false, 3) == ExpeditionStartAdmission.MissingTracking,
                "missing persistent tracking rejected");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, true, false, true, true, false, 3) == ExpeditionStartAdmission.IdentityMismatch,
                "wrong same-name avatar identity rejected");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, true, true, false, true, false, 3) == ExpeditionStartAdmission.LeftParty,
                "leader invalidation before Start rejected");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, true, true, true, false, false, 3) == ExpeditionStartAdmission.Unusable,
                "dead or unusable leader rejected");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, true, true, true, true, true, 3) == ExpeditionStartAdmission.RemoteAuthority,
                "remote COOP human rejected");
            Assert(ExpeditionWorkflowPolicy.EvaluateStart(false, true, true, true, true, false, 1) == ExpeditionStartAdmission.NoRoute,
                "unstartable route rejected");
        }

        private static void LiveLegAuthorizationPolicy()
        {
            Assert(ExpeditionWorkflowPolicy.CanAuthorizeLiveLeg(true, true, 3),
                "exact identity plus current live first leg authorizes multi-hop start");
            Assert(!ExpeditionWorkflowPolicy.CanAuthorizeLiveLeg(true, false, 3),
                "stale setup route cannot authorize movement after its live first leg disappears");
            Assert(!ExpeditionWorkflowPolicy.CanAuthorizeLiveLeg(false, true, 3),
                "same-name replacement cannot authorize a prepared route");
            Assert(!ExpeditionWorkflowPolicy.CanAuthorizeLiveLeg(true, true, 1),
                "route with no transition cannot authorize a leg");
        }

        private static void SetupLifecycle()
        {
            Assert(ExpeditionWorkflowPolicy.CloseSetupAfterStart(true), "successful Start closes setup");
            Assert(!ExpeditionWorkflowPolicy.CloseSetupAfterStart(false), "failed Start leaves setup available for correction");
            Assert(ExpeditionWorkflowPolicy.ShouldAutoSelectReplacement(false), "initial setup may choose a convenient first route");
            Assert(!ExpeditionWorkflowPolicy.ShouldAutoSelectReplacement(true), "live route loss never silently redirects an explicit destination");
        }

        private static void ActiveStatusControls()
        {
            ExpeditionUiActionVisibility traveling = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.Traveling, true, false, false, false, false, false);
            Assert(traveling.ShowPause && !traveling.ShowResume && traveling.ShowCancel, "traveling status exposes Pause and Cancel");

            ExpeditionUiActionVisibility paused = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.Paused, true, false, false, false, false, false);
            Assert(!paused.ShowPause && paused.ShowResume && paused.ShowCancel, "paused status exposes Resume and Cancel");

            ExpeditionUiActionVisibility zoning = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.Transitioning, true, false, false, false, false, false);
            Assert(!zoning.ShowPause && !zoning.ShowResume && zoning.ShowCancel, "zoning status is Cancel-only");

            ExpeditionUiActionVisibility combat = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.CombatInterrupted, true, false, false, false, false, false);
            Assert(!combat.ShowPause && !combat.ShowResume && combat.ShowCancel, "native combat owns movement without extra user mode");
        }

        private static void ArrivalCapabilities()
        {
            ExpeditionUiActionVisibility absent = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.Arrived, false, true, false, false, false, true);
            Assert(!absent.ShowCampHere && absent.ShowReturn, "Campmaster absent hides Camp Here while Return remains possible");

            ExpeditionUiActionVisibility present = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.Arrived, false, true, true, false, false, true);
            Assert(present.ShowCampHere && present.ShowReturn, "verified Campmaster capability exposes Camp Here");

            ExpeditionUiActionVisibility activeCamp = ExpeditionWorkflowPolicy.ResolveStatusActions(
                ExpeditionState.Arrived, false, true, true, true, false, true);
            Assert(!activeCamp.ShowCampHere, "already-active Campmaster hunt suppresses duplicate handoff");
        }

        private static void UiCloseIsPresentationOnly()
        {
            Assert(!ExpeditionWorkflowPolicy.StatusCloseCancelsRuntime(), "closing or Escape-hiding status never cancels expedition");
            Assert(ExpeditionWorkflowPolicy.ShouldAutoShowForSession(4, 5), "new expedition session reopens status automatically");
            Assert(!ExpeditionWorkflowPolicy.ShouldAutoShowForSession(5, 5), "manual hide remains scoped to the current session");
        }

        private static void OneHopAndMultiLegAdvance()
        {
            Assert(ExpeditionWorkflowPolicy.ResolveLegAdvance(true, 1, 2) == ExpeditionLegAdvanceDecision.Arrive,
                "one-hop destination arrives after first verified scene/reacquire");
            Assert(ExpeditionWorkflowPolicy.ResolveLegAdvance(false, 1, 3) == ExpeditionLegAdvanceDecision.Continue,
                "multi-leg route advances after first verified intermediate zone");
            Assert(ExpeditionWorkflowPolicy.ResolveLegAdvance(false, 2, 3) == ExpeditionLegAdvanceDecision.Arrive,
                "final multi-leg index arrives rather than starting a stale extra leg");
        }

        public static int Main()
        {
            StartAdmission();
            LiveLegAuthorizationPolicy();
            SetupLifecycle();
            ActiveStatusControls();
            ArrivalCapabilities();
            UiCloseIsPresentationOnly();
            OneHopAndMultiLegAdvance();
            Console.WriteLine("Expedition workflow tests passed: " + _passed);
            return 0;
        }
    }
}
