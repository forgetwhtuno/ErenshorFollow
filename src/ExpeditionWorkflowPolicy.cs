namespace ErenshorFollow
{
    internal enum ExpeditionLegAdvanceDecision
    {
        Arrive,
        Continue
    }

    internal enum ExpeditionStartAdmission
    {
        Allowed,
        AlreadyActive,
        MissingTracking,
        IdentityMismatch,
        LeftParty,
        Unusable,
        RemoteAuthority,
        NoRoute
    }

    internal struct ExpeditionUiActionVisibility
    {
        internal readonly bool ShowPause;
        internal readonly bool ShowResume;
        internal readonly bool ShowCancel;
        internal readonly bool ShowCampHere;
        internal readonly bool ShowReturn;

        internal ExpeditionUiActionVisibility(bool pause, bool resume, bool cancel, bool camp, bool returnTrip)
        {
            ShowPause = pause;
            ShowResume = resume;
            ShowCancel = cancel;
            ShowCampHere = camp;
            ShowReturn = returnTrip;
        }
    }

    // Pure workflow policy so the MMO-facing UI rules are regression-testable without Unity or Erenshor.
    // It does not authorize movement; runtime controllers still re-check the real game state.
    internal static class ExpeditionWorkflowPolicy
    {
        internal static ExpeditionStartAdmission EvaluateStart(bool expeditionAlreadyActive, bool trackingPresent,
            bool sameTracking, bool inParty, bool usable, bool remoteAuthority, int routeZoneCount)
        {
            if (expeditionAlreadyActive) return ExpeditionStartAdmission.AlreadyActive;
            if (!trackingPresent) return ExpeditionStartAdmission.MissingTracking;
            if (!sameTracking) return ExpeditionStartAdmission.IdentityMismatch;
            if (!inParty) return ExpeditionStartAdmission.LeftParty;
            if (!usable) return ExpeditionStartAdmission.Unusable;
            if (remoteAuthority) return ExpeditionStartAdmission.RemoteAuthority;
            if (routeZoneCount < 2) return ExpeditionStartAdmission.NoRoute;
            return ExpeditionStartAdmission.Allowed;
        }

        internal static ExpeditionUiActionVisibility ResolveStatusActions(ExpeditionState state, bool active,
            bool verifiedArrival, bool campmasterAvailable, bool campmasterActive, bool campRequestPending, bool canReturn)
        {
            if (verifiedArrival)
            {
                bool camp = campmasterAvailable && !campmasterActive && !campRequestPending;
                return new ExpeditionUiActionVisibility(false, false, false, camp, canReturn);
            }
            if (!active) return new ExpeditionUiActionVisibility(false, false, false, false, false);

            bool resume = state == ExpeditionState.Paused;
            bool pause = !resume && state != ExpeditionState.Transitioning &&
                         state != ExpeditionState.CombatInterrupted && state != ExpeditionState.Regrouping;
            return new ExpeditionUiActionVisibility(pause, resume, true, false, false);
        }

        // A setup preview is informational only. Starting or continuing a leg requires both the exact
        // persistent leader identity and a freshly resolved live Zoneline for the first hop in the
        // CURRENT loaded scene. This is the pure expression of the stale-setup/next-leg safety rule.
        internal static bool CanAuthorizeLiveLeg(bool exactTrackingIdentity, bool liveFirstHopResolved, int routeZoneCount)
        {
            return exactTrackingIdentity && liveFirstHopResolved && routeZoneCount >= 2;
        }

        internal static bool CloseSetupAfterStart(bool startSucceeded) { return startSucceeded; }

        // Route refresh may pick a convenient initial default, but must not redirect an already-displayed
        // destination to some other zone if live route authority changes underneath the setup window.
        internal static bool ShouldAutoSelectReplacement(bool hadExplicitSelection) { return !hadExplicitSelection; }

        // Closing/hiding status is presentation only by product contract. Runtime cancellation requires an
        // explicit Cancel action and may never be inferred from window close/Escape.
        internal static bool StatusCloseCancelsRuntime() { return false; }

        internal static bool ShouldAutoShowForSession(int previouslySeenSessionId, int currentSessionId)
        {
            return currentSessionId > 0 && currentSessionId != previouslySeenSessionId;
        }

        internal static ExpeditionLegAdvanceDecision ResolveLegAdvance(bool sceneMatchesFinalDestination,
            int routeIndexAfterArrival, int plannedZoneCount)
        {
            return sceneMatchesFinalDestination || routeIndexAfterArrival >= plannedZoneCount - 1
                ? ExpeditionLegAdvanceDecision.Arrive
                : ExpeditionLegAdvanceDecision.Continue;
        }
    }
}
