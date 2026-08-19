using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    // Owns exactly one active expedition and is the only writer of ExpeditionState. LeaderController
    // executes the leg and reports outcomes; this class decides what those outcomes mean.
    internal static class ExpeditionCoordinator
    {
        private static readonly List<LeaderController.LegEvent> DrainedEvents = new List<LeaderController.LegEvent>();
        private static ExpeditionSession _session;
        private static int _nextSessionId = 1;
        private static bool _releasingLeg;
        private static bool _terminalEmitted;
        private static float _terminalAt;
        private static float _transitionSince;
        private static float _sceneSettledSince;
        private static float _postZoneRouteReadinessSince;
        private static float _nextPostZoneRouteProbeAt;
        private static int _postZoneRouteProbeCount;
        private static string _postZoneRouteLastObservation;
        private static ExpeditionPauseReason _postZoneCarriedPause;
        private static bool _externalOverride;

        // Post-arrival record so Return can still be offered once the session itself has cleared.
        private static string _lastOriginZone;
        private static SimPlayerTracking _lastLeaderTracking;
        private static string _lastLeaderName;
        private static float _lastCompletedAt;

        private const float TerminalVisibleSeconds = 4f;
        private const float TransitionSettleSeconds = 2.5f;
        private const float TransitionTimeoutSeconds = 60f;
        private const float ReturnOfferSeconds = 600f;

        internal static bool IsActive
        {
            get { return _session != null && !IsTerminal(_session.State); }
        }

        internal static ExpeditionStatusSnapshot GetStatusSnapshot()
        {
            if (_session == null)
                return new ExpeditionStatusSnapshot(0, false, ExpeditionState.Idle, ExpeditionObjective.Outbound,
                    null, null, null, null, 0, ExpeditionPauseReason.None, 0, false, null);

            int remaining = Math.Max(0, _session.PlannedZones.Count - 1 - _session.CurrentRouteIndex);
            return new ExpeditionStatusSnapshot(_session.SessionId, !IsTerminal(_session.State), _session.State, _session.Objective,
                _session.LeaderName, _session.DestinationName, _session.CurrentZone, _session.CurrentLegDestinationName,
                remaining, _session.PauseReason, _session.CombatInterruptions, _session.RouteReadinessPending,
                _session.FailureDetail);
        }

        internal static bool IsLeaderTracking(SimPlayerTracking tracking)
        {
            return tracking != null && _session != null && object.ReferenceEquals(_session.LeaderTracking, tracking);
        }

        // --- lifecycle entry points -------------------------------------------------------------

        internal static void Start(SimPlayer leader, ExpeditionDestination destination, ExpeditionInitiation source)
        {
            List<string> route = new List<string>();
            route.Add(ActiveScene());
            if (destination != null) route.Add(destination.CanonicalName);
            string failure;
            ExpeditionStartOutcome unusedOutcome;
            if (!TryStartPrepared(leader, destination, route, source, ExpeditionObjective.Outbound, out failure, out unusedOutcome))
                Say("[Erenshor Expedition] " + failure, "yellow");
        }

        internal static void StartRoute(SimPlayer leader, IList<string> plannedZones, ExpeditionInitiation source)
        {
            if (plannedZones == null || plannedZones.Count < 2)
            {
                string rejected = "That route does not leave the current zone.";
                TraceRejectedAttempt(source, null, rejected);
                Say("[Erenshor Expedition] " + rejected, "yellow");
                return;
            }
            bool ambiguous;
            ExpeditionDestination firstLeg = ExpeditionDestinationResolver.Resolve(plannedZones[1], out ambiguous);
            if (firstLeg == null)
            {
                string rejected = "The first atlas hop to " + plannedZones[1] +
                    " is not a verified live exit from this zone.";
                TraceRejectedAttempt(source, plannedZones[plannedZones.Count - 1], rejected);
                Say("[Erenshor Expedition] " + rejected, "yellow");
                return;
            }

            string failure;
            ExpeditionStartOutcome unusedOutcome;
            if (!TryStartPrepared(leader, firstLeg, plannedZones, source, ExpeditionObjective.Outbound, out failure, out unusedOutcome))
                Say("[Erenshor Expedition] " + failure, "yellow");
        }

        // Turns a fine-grained identity/route admission code into the exact wording the pre-refactor
        // inline checks used, so the visible message did not change when this was wired to the shared,
        // already-tested ExpeditionWorkflowPolicy.EvaluateStart policy instead of duplicating its logic.
        // Unusable/RemoteAuthority intentionally share one branch: the original code chose between the
        // two messages by re-checking IsRemoteHuman independently of which condition actually failed
        // LeaderValid, and that same independent check is reproduced here.
        private static string DescribeIdentityAdmission(ExpeditionStartAdmission admission, SimPlayer avatar)
        {
            switch (admission)
            {
                case ExpeditionStartAdmission.AlreadyActive:
                    return "Another expedition is already active.";
                case ExpeditionStartAdmission.MissingTracking:
                case ExpeditionStartAdmission.LeftParty:
                    return "The intended leader is no longer in your party.";
                case ExpeditionStartAdmission.IdentityMismatch:
                    return "The intended Sim identity is not currently loaded.";
                case ExpeditionStartAdmission.Unusable:
                case ExpeditionStartAdmission.RemoteAuthority:
                    return avatar != null && CoopCompatibility.IsRemoteHuman(avatar)
                        ? "That leader is controlled by another client."
                        : "The intended leader is no longer a living local Sim in your party.";
                default:
                    return "The expedition could not start safely.";
            }
        }

        // Setup-window entry point. The window stores only SimPlayerTracking plus a final destination.
        // Start re-resolves that exact tracking object's current avatar, re-runs local/party/remote/alive
        // admission, and rebuilds the atlas route against the CURRENT scene/live first-hop Zonelines. A
        // stale setup preview therefore cannot authorize movement after identity or route conditions change.
        //
        // The outcome out-param is the explicit Start-result contract the setup UI switches on instead of
        // inferring success from "the button was clicked" or from parsing the failure string.
        internal static bool TryStartRouteExact(SimPlayerTracking tracking, string finalDestination,
            ExpeditionInitiation source, out string failure, out ExpeditionStartOutcome outcome)
        {
            failure = null;
            outcome = ExpeditionStartOutcome.Rejected;

            SimPlayer avatar = tracking == null ? null : SimTrackingRebind.CurrentAvatar(tracking);
            bool trackingPresent = tracking != null;
            bool sameTracking = avatar != null && SimTrackingRebind.AvatarMatchesTracking(tracking, avatar);
            bool inParty = trackingPresent && SimTrackingRebind.TrackingIsInPlayerGroup(tracking);
            // Folds the original LeaderValid(avatar)'s IsPlayerPartySim check into "usable" so the shared
            // policy's boolean set still expresses the exact same three-way admission requirement.
            bool usable = avatar != null && FollowController.IsUsableSim(avatar) && LeaderController.IsPlayerPartySim(avatar);
            bool remoteAuthority = avatar != null && CoopCompatibility.IsRemoteHuman(avatar);

            // Identity/party is resolved with a placeholder route count that can never itself trigger
            // NoRoute, preserving the original check order (and its short-circuit: the atlas is not built
            // at all while the leader is already known-invalid).
            ExpeditionStartAdmission identity = ExpeditionWorkflowPolicy.EvaluateStart(
                IsActive, trackingPresent, sameTracking, inParty, usable, remoteAuthority, 2);
            if (identity != ExpeditionStartAdmission.Allowed)
            {
                outcome = ExpeditionWorkflowPolicy.ToStartOutcome(identity);
                failure = DescribeIdentityAdmission(identity, avatar);
                TraceRejectedAttempt(source, finalDestination, failure);
                return false;
            }

            string origin = ActiveScene();
            List<string> liveFirstHops = ExpeditionDestinationResolver.ListCanonicalNames();
            List<string> route;
            bool ambiguous;
            string routeFailure;
            bool routeBuilt = ZoneAtlasRoutePlanner.TryBuild(origin, finalDestination, liveFirstHops,
                out route, out ambiguous, out routeFailure) && route.Count >= 2;
            ExpeditionStartAdmission withRoute = ExpeditionWorkflowPolicy.EvaluateStart(
                IsActive, trackingPresent, sameTracking, inParty, usable, remoteAuthority, routeBuilt ? route.Count : 0);
            if (withRoute != ExpeditionStartAdmission.Allowed)
            {
                outcome = ExpeditionWorkflowPolicy.ToStartOutcome(withRoute);
                failure = string.IsNullOrWhiteSpace(routeFailure)
                    ? "No safe atlas route is currently available to that destination."
                    : routeFailure;
                TraceRejectedAttempt(source, finalDestination, failure);
                return false;
            }

            ExpeditionDestination firstLeg = ExpeditionDestinationResolver.Resolve(route[1], out ambiguous);
            if (firstLeg == null || !SameScene(firstLeg.CanonicalName, route[1]))
            {
                outcome = ExpeditionStartOutcome.NoRoute;
                failure = "The first atlas hop to " + route[1] + " is not a verified live exit from this zone.";
                TraceRejectedAttempt(source, finalDestination, failure);
                return false;
            }

            return TryStartPrepared(avatar, firstLeg, route, source, ExpeditionObjective.Outbound, out failure, out outcome);
        }

        private static bool TryStartPrepared(SimPlayer leader, ExpeditionDestination destination, IList<string> plannedZones,
            ExpeditionInitiation source, ExpeditionObjective objective, out string failure, out ExpeditionStartOutcome outcome)
        {
            failure = null;
            outcome = ExpeditionStartOutcome.Rejected;
            if (IsActive) Cancel("Starting a new expedition.");
            ClearSession();
            int pendingSessionId = _nextSessionId;
            ExpeditionPhaseTelemetry.Begin(pendingSessionId);
            ExpeditionPhaseTelemetry.Record("command_received",
                "source=" + source + " destination=" + SafeTelemetryName(destination == null ? null : destination.CanonicalName));

            if (destination == null)
            {
                failure = "That is not a verified live zone exit.";
                outcome = ExpeditionStartOutcome.NoRoute;
                ExpeditionPhaseTelemetry.Record("command_rejected", failure);
                return false;
            }

            // A multi-zone expedition is only safe when Erenshor exposes the persistent Sim identity
            // that survives avatar destruction/recreation. Capture it before any movement begins so a
            // failed identity read cannot leave a travel leg running without a reacquisition key.
            SimPlayerTracking stableTracking = ReadTracking(leader);
            if (stableTracking == null || !SimTrackingRebind.TrackingIsInPlayerGroup(stableTracking) ||
                !SimTrackingRebind.AvatarMatchesTracking(stableTracking, leader))
            {
                failure = "That leader does not have a verified persistent party identity, so the expedition cannot start safely.";
                outcome = ExpeditionStartOutcome.InvalidLeader;
                ExpeditionPhaseTelemetry.Record("leader_validation_failed", failure);
                ExpeditionPhaseTelemetry.Record("command_rejected", failure);
                return false;
            }
            ExpeditionPhaseTelemetry.Record("leader_validated",
                "leader=" + SafeTelemetryName(FollowController.ReadName(leader)) + " exactTracking=true localParty=true");

            string legFailure;
            ExpeditionStartOutcome legOutcome;
            if (!LeaderController.StartExpeditionLeg(leader, destination, out legFailure, out legOutcome))
            {
                failure = string.IsNullOrWhiteSpace(legFailure) ? "The first travel leg could not start safely." : legFailure;
                outcome = legOutcome;
                ExpeditionPhaseTelemetry.Record("command_rejected", failure);
                return false;
            }
            outcome = ExpeditionStartOutcome.Accepted;
            ExpeditionPhaseTelemetry.Record("command_accepted",
                "leader=" + SafeTelemetryName(FollowController.ReadName(leader)) +
                " firstLeg=" + SafeTelemetryName(destination.CanonicalName));

            ExpeditionSession session = new ExpeditionSession(pendingSessionId);
            _nextSessionId++;
            session.Objective = objective;
            session.Purpose = objective == ExpeditionObjective.Return ? ExpeditionPurpose.ReturnToOrigin : ExpeditionPurpose.TravelToZone;
            session.LeaderRuntime = leader;
            session.LeaderTracking = stableTracking;
            session.LeaderName = FollowController.ReadName(leader);
            session.OriginZone = ActiveScene();
            session.CurrentZone = session.OriginZone;
            session.Destination = destination;
            if (plannedZones != null)
            {
                for (int i = 0; i < plannedZones.Count; i++)
                    if (!string.IsNullOrWhiteSpace(plannedZones[i])) session.PlannedZones.Add(plannedZones[i].Trim());
            }
            if (session.PlannedZones.Count < 2)
            {
                session.PlannedZones.Clear();
                session.PlannedZones.Add(session.OriginZone);
                session.PlannedZones.Add(destination.CanonicalName);
            }
            session.CurrentRouteIndex = 0;
            session.FinalDestinationName = session.PlannedZones[session.PlannedZones.Count - 1];
            session.VerifiedZonesCrossed.Add(session.OriginZone);
            session.StartedUtc = DateTime.UtcNow;
            session.InitiationSource = source;
            session.State = ExpeditionState.Forming;
            _session = session;
            _terminalEmitted = false;
            _externalOverride = false;

            Emit("expedition_started");
            // Forming exists so the session is observable before movement; departure follows immediately
            // because the leg is already under way.
            _session.State = ExpeditionState.Traveling;
            Emit("expedition_departed");
            Say("[Erenshor Expedition] " + session.LeaderName + " is leading the group to " + session.DestinationName +
                (session.PlannedZones.Count > 2 ? " via " + destination.CanonicalName : string.Empty) +
                ". Combat temporarily takes precedence; pause/resume remains available from Expedition Status.", "lightblue");
            return true;
        }

        internal static void Pause(ExpeditionPauseReason reason)
        {
            if (!IsActive)
            {
                Say("[Erenshor Expedition] No expedition is active.", "yellow");
                return;
            }
            if (_session.State == ExpeditionState.Transitioning)
            {
                Say("[Erenshor Expedition] The group is between zones; wait for the transition to finish.", "yellow");
                return;
            }
            if (_session.State == ExpeditionState.Paused) return;

            bool nativeGroupOrder = reason == ExpeditionPauseReason.PlayerGroupOrder;
            LeaderController.HoldForExpedition(nativeGroupOrder);
            _session.State = ExpeditionState.Paused;
            _session.PauseReason = reason;
            Emit("expedition_paused");
            if (nativeGroupOrder)
                Say("[Erenshor Expedition] Native group movement has control. Use Group Follow or /expedition resume when you want the route again.", "yellow");
            else
                Say("[Erenshor Expedition] Held at " + _session.LeaderName + "'s position (" + DescribePause(reason) +
                    "). Use /expedition resume to continue.", "yellow");
        }

        internal static void Resume()
        {
            if (!IsActive)
            {
                Say("[Erenshor Expedition] No expedition is active.", "yellow");
                return;
            }
            if (_session.State != ExpeditionState.Paused)
            {
                Say("[Erenshor Expedition] The expedition is not paused (" + DescribeState(_session.State) + ").", "yellow");
                return;
            }

            if (FollowController.ManualMovementKeyHeld())
            {
                Say("[Erenshor Expedition] Let go of your movement keys first, then resume.", "yellow");
                return;
            }

            string failure;
            if (!LeaderController.ResumeExpeditionLeg(out failure))
            {
                Say("[Erenshor Expedition] Cannot resume: " + failure, "yellow");
                return;
            }
            _session.State = ExpeditionState.Traveling;
            _session.PauseReason = ExpeditionPauseReason.None;
            Emit("expedition_resumed");
            Say("[Erenshor Expedition] " + _session.LeaderName + " is moving again toward " + _session.DestinationName + ".", "lightblue");
        }

        internal static string ReturnZoneName()
        {
            return ReturnOriginZone();
        }

        internal static bool CanReturn()
        {
            string origin = ReturnOriginZone();
            if (string.IsNullOrWhiteSpace(origin) || SameScene(origin, ActiveScene())) return false;
            List<string> route;
            bool ambiguous;
            string failure;
            return ZoneAtlasRoutePlanner.TryBuild(ActiveScene(), origin,
                ExpeditionDestinationResolver.ListCanonicalNames(), out route, out ambiguous, out failure) &&
                route.Count >= 2 && ExpeditionDestinationResolver.IsCurrentlyReachable(route[1]);
        }

        internal static void TryReturn()
        {
            string origin = ReturnOriginZone();
            if (string.IsNullOrWhiteSpace(origin))
            {
                Say("[Erenshor Expedition] There is no recent expedition to return from.", "yellow");
                return;
            }
            if (SameScene(origin, ActiveScene()))
            {
                Say("[Erenshor Expedition] You are already in " + origin + ".", "yellow");
                return;
            }

            List<string> route;
            bool ambiguous;
            string routeFailure;
            if (!ZoneAtlasRoutePlanner.TryBuild(ActiveScene(), origin,
                ExpeditionDestinationResolver.ListCanonicalNames(), out route, out ambiguous, out routeFailure) || route.Count < 2)
            {
                Say("[Erenshor Expedition] There is no verified atlas route back to " + origin + ".", "yellow");
                return;
            }
            ExpeditionDestination destination = ExpeditionDestinationResolver.Resolve(route[1], out ambiguous);
            if (destination == null)
            {
                Say("[Erenshor Expedition] The next return hop to " + route[1] + " is not a verified live exit.", "yellow");
                return;
            }

            SimPlayer leader = ResolveReturnLeader();
            if (leader == null)
            {
                Say("[Erenshor Expedition] The previous leader is no longer a living local Sim in your party.", "yellow");
                return;
            }

            string startFailure;
            ExpeditionStartOutcome unusedOutcome;
            if (TryStartPrepared(leader, destination, route, ExpeditionInitiation.Command, ExpeditionObjective.Return, out startFailure, out unusedOutcome))
                Emit("expedition_returning");
            else
                Say("[Erenshor Expedition] " + startFailure, "yellow");
        }

        internal static void Cancel(string reason)
        {
            if (!IsActive) return;
            _session.State = ExpeditionState.Cancelled;
            _session.FailureDetail = reason;
            _session.RouteReadinessPending = false;
            ExpeditionPhaseTelemetry.Record("cancelled", string.IsNullOrWhiteSpace(reason) ? "no reason" : reason);
            ReleaseLeg(true);
            _terminalAt = Time.time;
            Emit("expedition_cancelled");
            Say("[Erenshor Expedition] Expedition cancelled" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason), "yellow");
        }

        private static void Fail(ExpeditionFailureReason reason, string detail, bool leaderStillValid)
        {
            if (!IsActive) return;
            _session.State = ExpeditionState.Failed;
            _session.FailureReason = reason;
            // Always store a real, non-blank reason: the status surface shows this exact text, and an
            // empty FailureDetail would silently regress to "the expedition could not continue" even
            // though the chat line right below already carries the specific DescribeFailure() text.
            _session.FailureDetail = string.IsNullOrWhiteSpace(detail) ? DescribeFailure(reason) : detail;
            _session.RouteReadinessPending = false;
            ExpeditionPhaseTelemetry.Record("failed", reason + ": " + _session.FailureDetail);
            ReleaseLeg(leaderStillValid);
            _terminalAt = Time.time;
            Emit("expedition_failed");
            Say("[Erenshor Expedition] Expedition ended: " + _session.FailureDetail, "yellow");
        }

        // Called by LeaderController when something outside this class tore the leg down.
        internal static void NotifyLegTornDown()
        {
            if (_releasingLeg || !IsActive) return;
            _session.State = ExpeditionState.Cancelled;
            _session.FailureDetail = "travel was taken over by another command";
            _terminalAt = Time.time;
            Emit("expedition_cancelled");
            Say("[Erenshor Expedition] Expedition cancelled: travel was taken over by another command.", "yellow");
        }

        // Native Run Away is an emergency override. Any zone it produces is not an expedition arrival.
        internal static void NoteExternalOverride()
        {
            if (!IsActive) return;
            _externalOverride = true;
            if (_session.State != ExpeditionState.Transitioning && _session.State != ExpeditionState.Paused)
            {
                LeaderController.HoldForExpedition(true);
                _session.State = ExpeditionState.Paused;
                _session.PauseReason = ExpeditionPauseReason.PlayerGroupOrder;
                Emit("expedition_paused");
                Say("[Erenshor Expedition] Run Away has native movement control; the expedition route is paused.", "yellow");
            }
        }

        internal static void HandleSceneLoaded(Scene scene)
        {
            if (!IsActive) return;
            if (_session.State != ExpeditionState.Transitioning) EnterTransitioning();
            _sceneSettledSince = 0f;
        }

        // --- per-frame ---------------------------------------------------------------------------

        internal static void Tick()
        {
            if (_session == null) return;

            if (IsTerminal(_session.State))
            {
                if (Time.time - _terminalAt >= TerminalVisibleSeconds) ClearSession();
                return;
            }

            if (_session.State == ExpeditionState.Transitioning)
            {
                TickTransition();
                return;
            }

            // A zone change outranks every other signal: the leader avatar is destroyed by zoning, so no
            // leg state observed afterwards is meaningful.
            if (GameData.Zoning || !SameScene(ActiveScene(), _session.CurrentZone))
            {
                EnterTransitioning();
                return;
            }

            if (_session.State == ExpeditionState.Paused)
            {
                // Only leader validity matters while held; the leg issues no orders.
                if (!LeaderValid(_session.LeaderRuntime))
                    Fail(ExpeditionFailureReason.LeaderUnavailable, null, false);
                return;
            }

            if (!LeaderController.LegActive || !LeaderController.ExpeditionOwned)
            {
                Fail(ExpeditionFailureReason.InternalError, "the travel leg stopped unexpectedly", false);
                return;
            }

            DrainedEvents.Clear();
            LeaderController.DrainEvents(DrainedEvents);
            for (int i = 0; i < DrainedEvents.Count; i++)
            {
                Apply(DrainedEvents[i]);
                if (!IsActive || _session.State == ExpeditionState.Transitioning || _session.State == ExpeditionState.Paused) return;
            }
        }

        private static void Apply(LeaderController.LegEvent legEvent)
        {
            switch (legEvent)
            {
                case LeaderController.LegEvent.CombatDetected:
                    if (_session.State != ExpeditionState.CombatInterrupted)
                    {
                        _session.State = ExpeditionState.CombatInterrupted;
                        _session.CombatInterruptions++;
                        Emit("expedition_combat_interrupted");
                        Say("[Erenshor Expedition] Travel paused for combat. Erenshor has the fight.", "yellow");
                    }
                    break;

                case LeaderController.LegEvent.CombatCleared:
                    _session.State = ExpeditionState.Regrouping;
                    Say("[Erenshor Expedition] Combat is clear. " + _session.LeaderName + " is waiting for the group.", "lightblue");
                    break;

                case LeaderController.LegEvent.WaitingForPlayer:
                    _session.State = ExpeditionState.Regrouping;
                    Say(_session.LeaderName + " tells the group: I'll wait here—catch up.", "lightblue");
                    break;

                case LeaderController.LegEvent.PlayerRegrouped:
                    _session.State = ExpeditionState.Traveling;
                    Emit("expedition_resumed");
                    Say(_session.LeaderName + " tells the group: Ready? Let's keep moving.", "lightblue");
                    break;

                case LeaderController.LegEvent.FollowManualOverride:
                    Pause(ExpeditionPauseReason.PlayerManualMovement);
                    break;

                case LeaderController.LegEvent.RouteFailed:
                    // The leg classified the failure at its own call site; without that, every route failure
                    // read as "no walkable route", including cases where verified approaches existed and
                    // cases where travel failed for reasons unrelated to the crossing.
                    LeaderController.RouteFailureContext routeFailure = LeaderController.LastRouteFailure();
                    Fail(ExpeditionFailureReason.RouteFailed, RouteCandidatePolicy.DescribeRouteFailure(
                        _session.DestinationName, routeFailure.Kind, routeFailure.Reason), true);
                    break;

                case LeaderController.LegEvent.LeaderInvalid:
                    Fail(LeaderFailureReason(), null, false);
                    break;

                case LeaderController.LegEvent.GroupCouldNotCatchUp:
                    // Losing formation is recoverable and does not invalidate identity or the verified
                    // route. Hold instead of destroying the whole expedition; the player can deliberately
                    // resume once the group is together, or cancel at any time.
                    Pause(ExpeditionPauseReason.GroupCouldNotCatchUp);
                    break;
            }
        }

        // --- zone transition ---------------------------------------------------------------------

        private static void EnterTransitioning()
        {
            if (_session == null || _session.State == ExpeditionState.Transitioning) return;
            _session.State = ExpeditionState.Transitioning;
            _session.RouteReadinessPending = false;
            _transitionSince = Time.time;
            _sceneSettledSince = 0f;
            ExpeditionPhaseTelemetry.Record("zone_transition_observed",
                "from=" + SafeTelemetryName(_session.CurrentZone) +
                " expected=" + SafeTelemetryName(_session.CurrentLegDestinationName) +
                " nativeZoning=" + GameData.Zoning);
            // The leader avatar is destroyed by zoning, so release without touching it.
            ReleaseLeg(false);
            _session.LeaderRuntime = null;
        }

        private static void TickTransition()
        {
            string scene = ActiveScene();
            bool sceneChanged = !SameScene(scene, _session.CurrentZone);
            bool timedOut = _transitionSince > 0f && Time.time - _transitionSince >= TransitionTimeoutSeconds;

            if (!sceneChanged)
            {
                if (timedOut)
                    Fail(ExpeditionFailureReason.InternalError, "the zone transition never completed.", false);
                return;
            }
            if (GameData.Zoning)
            {
                _sceneSettledSince = 0f;
                return;
            }

            // Exact avatar rebind and scene-manager readiness are not evidence that the scene's
            // Zonelines/NavMesh are ready for a new leg.  Once rebinding succeeded, this is the only
            // transition work allowed until a fresh bounded route probe proves the next leg.
            if (_postZoneRouteReadinessSince > 0f)
            {
                TickPostZoneRouteReadiness();
                return;
            }

            // Route identity is already known as soon as the real active scene changes. Do not wait up
            // to the leader-rebind timeout when the native game plainly loaded the wrong destination.
            if (_externalOverride)
            {
                Cancel("the group used Run Away and left the route.");
                return;
            }
            if (!SameScene(scene, _session.CurrentLegDestinationName))
            {
                Fail(ExpeditionFailureReason.UnexpectedZone, "an unexpected zone change to " + scene + ".", false);
                return;
            }
            ExpeditionPhaseTelemetry.Record("destination_scene_entered",
                "scene=" + SafeTelemetryName(scene) + " waitingForExactLeader=true");

            // The native group manager destroys the old SimPlayer and later rebinds the same
            // SimPlayerTracking.MyAvatar. Do not treat sceneLoaded or manager creation as proof that
            // that rebind has completed; wait for the exact persistent identity with the same bounded
            // policy used by ordinary direct Follow.
            bool gameReady = GameData.SimMngr != null && GameData.SimPlayerGrouping != null &&
                             GameData.GroupMembers != null;
            if (!gameReady)
                _sceneSettledSince = 0f;
            else if (_sceneSettledSince <= 0f)
                _sceneSettledSince = Time.time;

            bool settled = gameReady && _sceneSettledSince > 0f &&
                           Time.time - _sceneSettledSince >= TransitionSettleSeconds;
            SimPlayerTracking tracking = _session.LeaderTracking;
            SimPlayer avatar = settled ? SimTrackingRebind.CurrentAvatar(tracking) : null;

            FollowRebindInputs input = new FollowRebindInputs();
            input.Zoning = GameData.Zoning;
            input.SceneChanged = sceneChanged;
            input.GameReady = gameReady;
            input.Settled = settled;
            input.TrackingInGroup = settled && SimTrackingRebind.TrackingIsInPlayerGroup(tracking);
            input.AvatarPresent = avatar != null;
            input.SameTracking = avatar != null && SimTrackingRebind.AvatarMatchesTracking(tracking, avatar);
            input.AvatarUsable = avatar != null && FollowController.IsUsableSim(avatar);
            input.LivePartyMember = avatar != null && LeaderController.IsPlayerPartySim(avatar);
            input.RemoteAuthority = avatar != null && CoopCompatibility.IsRemoteHuman(avatar);
            input.TimedOut = timedOut;

            FollowRebindFailure rebindFailure;
            FollowRebindDecision decision = FollowRebindPolicy.Evaluate(input, out rebindFailure);
            if (decision == FollowRebindDecision.Waiting) return;
            if (decision == FollowRebindDecision.Stop)
            {
                FailTransitionReacquire(rebindFailure, scene);
                return;
            }

            _session.LeaderRuntime = avatar;
            ExpeditionPhaseTelemetry.Record("leader_reacquired",
                "leader=" + SafeTelemetryName(_session.LeaderName) + " scene=" + SafeTelemetryName(scene) + " exactTracking=true");
            CompleteTransition(scene);
        }

        private static void FailTransitionReacquire(FollowRebindFailure failure, string scene)
        {
            ExpeditionFailureReason reason = ExpeditionFailureReason.LeaderNotReacquired;
            string detail;
            switch (failure)
            {
                case FollowRebindFailure.LeftParty:
                    reason = ExpeditionFailureReason.LeaderLeftParty;
                    detail = _session.LeaderName + " is no longer in the player's real group after entering " + scene + ".";
                    break;
                case FollowRebindFailure.RemoteAuthority:
                    reason = ExpeditionFailureReason.LeaderRemote;
                    detail = _session.LeaderName + " is remote-authority after entering " + scene + ".";
                    break;
                case FollowRebindFailure.IdentityMismatch:
                    detail = _session.LeaderName + " was replaced by an avatar with a different SimPlayerTracking identity after entering " + scene + ".";
                    break;
                case FollowRebindFailure.TargetUnavailable:
                    detail = _session.LeaderName + " is unavailable or dead after entering " + scene + ".";
                    break;
                default:
                    detail = _session.LeaderName + " was not reacquired before the bounded zone-rebind timeout after entering " + scene + ".";
                    break;
            }
            Fail(reason, detail, false);
        }

        private static void CompleteTransition(string scene)
        {
            _session.CurrentZone = scene;
            if (!_session.VerifiedZonesCrossed.Contains(scene)) _session.VerifiedZonesCrossed.Add(scene);
            ExpeditionPhaseTelemetry.Record("destination_zone_entered",
                "scene=" + SafeTelemetryName(scene) + " exactLeaderReacquired=true");
            Emit("expedition_zone_entered");

            ExpeditionPauseReason carriedPause = _session.PauseReason;
            _session.CurrentRouteIndex++;
            ExpeditionLegAdvanceDecision advance = ExpeditionWorkflowPolicy.ResolveLegAdvance(
                SameScene(scene, _session.DestinationName), _session.CurrentRouteIndex, _session.PlannedZones.Count);
            if (advance == ExpeditionLegAdvanceDecision.Arrive)
            {
                // Physical arrival is terminal even if the player had held the expedition before manually
                // entering the expected native crossing. Do not leak a stale pause reason into the arrival event.
                _session.PauseReason = ExpeditionPauseReason.None;
                Arrive();
                return;
            }
            BeginPostZoneRouteReadiness(carriedPause);
        }

        private static void BeginPostZoneRouteReadiness(ExpeditionPauseReason carriedPause)
        {
            _postZoneRouteReadinessSince = Time.time;
            _nextPostZoneRouteProbeAt = 0f;
            _postZoneRouteProbeCount = 0;
            _postZoneRouteLastObservation = "not yet probed";
            _postZoneCarriedPause = carriedPause;
            // The leader is already reacquired by the time this begins; State stays Transitioning for
            // this whole probing window, so the UI needs its own signal to stop saying "reacquiring".
            if (_session != null) _session.RouteReadinessPending = true;
            ExpeditionPhaseTelemetry.Record("post_zone_route_readiness",
                "scene=" + SafeTelemetryName(_session.CurrentZone) +
                " expectedNext=" + SafeTelemetryName(ExpectedNextZone()) +
                " policy=fresh-zoneline-and-navmesh-probe");
            TickPostZoneRouteReadiness();
        }

        // A probe is a fresh resolver/atlas/planner observation, never a retry of stale crossing objects
        // or sampled points.  It is intentionally bounded and runs at a fixed low frequency rather than
        // every Update so diagnostic output remains phase-level rather than frame-level.
        private static void TickPostZoneRouteReadiness()
        {
            if (_session == null || _postZoneRouteReadinessSince <= 0f) return;
            if (Time.time < _nextPostZoneRouteProbeAt) return;
            _nextPostZoneRouteProbeAt = Time.time + PostZoneRouteReadinessPolicy.ProbeIntervalSeconds;
            _postZoneRouteProbeCount++;

            SimPlayer leader = ReacquireLeader();
            if (leader == null)
            {
                Fail(ExpeditionFailureReason.LeaderNotReacquired,
                    _session.LeaderName + " was lost while waiting for post-zone route readiness.", false);
                return;
            }

            List<string> discovered = ExpeditionDestinationResolver.ListCanonicalNames();
            List<string> replanned;
            bool routeAmbiguous;
            string routeFailure;
            bool atlasReady = ZoneAtlasRoutePlanner.TryBuild(_session.CurrentZone, _session.DestinationName,
                discovered, out replanned, out routeAmbiguous, out routeFailure) && replanned.Count >= 2;
            string nextZone = atlasReady ? replanned[1] : ExpectedNextZone();
            bool ambiguous;
            ExpeditionDestination nextLeg = atlasReady ? ExpeditionDestinationResolver.Resolve(nextZone, out ambiguous) : null;
            LeaderController.ZoneRouteReadinessSnapshot route = LeaderController.InspectZoneRouteReadiness(leader, nextLeg);

            PostZoneRouteReadinessInputs input = new PostZoneRouteReadinessInputs();
            input.ElapsedSeconds = Time.time - _postZoneRouteReadinessSince;
            input.AttemptCount = _postZoneRouteProbeCount;
            input.AtlasRouteAvailable = atlasReady;
            input.NextLegResolved = nextLeg != null && SameScene(nextLeg.CanonicalName, nextZone);
            input.LiveCrossingCount = route.LiveCrossingCount;
            input.StartSampled = route.StartSampled;
            input.AcceptedCandidateCount = route.AcceptedCandidateCount;
            PostZoneRouteReadinessDecision decision = PostZoneRouteReadinessPolicy.Evaluate(input);
            string classification = PostZoneRouteReadinessPolicy.DescribePending(input);
            _postZoneRouteLastObservation = "expected=" + SafeTelemetryName(ExpectedNextZone()) +
                " next=" + SafeTelemetryName(nextZone) +
                " discovered=" + string.Join(",", discovered.ToArray()) +
                " atlas=" + atlasReady + " crossings=" + route.LiveCrossingCount +
                " startSampled=" + route.StartSampled + " accepted=" + route.AcceptedCandidateCount +
                " detail=" + SafeDiagnostic(route.Detail) +
                (string.IsNullOrWhiteSpace(routeFailure) ? string.Empty : " atlasFailure=" + SafeTelemetryName(routeFailure));
            string probeSummary = "attempt=" + input.AttemptCount + "/" + PostZoneRouteReadinessPolicy.MaximumAttempts +
                " elapsed=" + input.ElapsedSeconds.ToString("F1") + "s classification=" + classification +
                " | " + _postZoneRouteLastObservation;
            ExpeditionPhaseTelemetry.Record("post_zone_route_probe", probeSummary);
            LogPostZoneProbe(probeSummary);

            if (decision == PostZoneRouteReadinessDecision.Failed)
            {
                Fail(ExpeditionFailureReason.RouteFailed,
                    "post-zone route readiness timed out after " + input.ElapsedSeconds.ToString("F1") + "s; " +
                    classification + ". Last fresh observation: " + _postZoneRouteLastObservation, true);
                return;
            }
            if (decision != PostZoneRouteReadinessDecision.Ready) return;

            ExpeditionPhaseTelemetry.Record("next_leg_revalidated",
                "from=" + SafeTelemetryName(_session.CurrentZone) + " liveExit=" + SafeTelemetryName(nextZone) +
                " readinessAttempts=" + input.AttemptCount);

            string startFailure;
            // StartExpeditionLeg has its own cleanup on a rejected native order.  This coordinator owns
            // the transition state, so prevent that cleanup from being misclassified as an external
            // cancellation while this bounded readiness attempt is still being evaluated.
            bool started;
            ExpeditionStartOutcome startOutcome;
            _releasingLeg = true;
            try { started = LeaderController.StartExpeditionLeg(leader, nextLeg, out startFailure, out startOutcome); }
            finally { _releasingLeg = false; }
            if (!started)
            {
                _postZoneRouteLastObservation = "fresh-ready probe could not start leg: " + SafeTelemetryName(startFailure) +
                    " (" + startOutcome + ")";
                ExpeditionPhaseTelemetry.Record("post_zone_route_start_rejected",
                    "attempt=" + input.AttemptCount + " reason=" + _postZoneRouteLastObservation);
                if (input.ElapsedSeconds >= PostZoneRouteReadinessPolicy.TimeoutSeconds ||
                    input.AttemptCount >= PostZoneRouteReadinessPolicy.MaximumAttempts)
                    Fail(ExpeditionFailureReason.RouteFailed,
                        "post-zone route readiness exhausted after a fresh-ready start rejection: " + _postZoneRouteLastObservation,
                        true);
                return;
            }

            if (_session.PlannedZones.Count > _session.CurrentRouteIndex + 1)
                _session.PlannedZones.RemoveRange(_session.CurrentRouteIndex + 1,
                    _session.PlannedZones.Count - (_session.CurrentRouteIndex + 1));
            for (int i = 1; i < replanned.Count; i++) _session.PlannedZones.Add(replanned[i]);
            _session.LeaderRuntime = leader;
            _session.Destination = nextLeg;
            _session.State = ExpeditionState.Traveling;
            _session.PauseReason = ExpeditionPauseReason.None;
            _session.RouteReadinessPending = false;
            _transitionSince = 0f;
            _sceneSettledSince = 0f;
            _postZoneRouteReadinessSince = 0f;
            _nextPostZoneRouteProbeAt = 0f;
            ExpeditionPhaseTelemetry.Record("resume_next_leg",
                "leader=" + SafeTelemetryName(_session.LeaderName) + " next=" + SafeTelemetryName(nextZone) +
                " readinessAttempts=" + input.AttemptCount + " elapsed=" + input.ElapsedSeconds.ToString("F1") + "s");
            Say("[Erenshor Expedition] Continuing through " + _session.CurrentZone + " toward " +
                _session.DestinationName + "; next exit is " + nextZone + ".", "lightblue");
            if (_postZoneCarriedPause != ExpeditionPauseReason.None) Pause(_postZoneCarriedPause);
            _postZoneCarriedPause = ExpeditionPauseReason.None;
        }

        private static string ExpectedNextZone()
        {
            if (_session == null) return null;
            int index = _session.CurrentRouteIndex + 1;
            return index >= 0 && index < _session.PlannedZones.Count ? _session.PlannedZones[index] : null;
        }

        // Arrival is only real when both the canonical destination scene and the exact persistent
        // leader identity have been verified on the far side. A same-name or missing avatar cannot
        // produce a successful terminal record.
        private static void Arrive()
        {
            SimPlayer leader = ReacquireLeader();
            if (leader == null)
            {
                Fail(ExpeditionFailureReason.LeaderNotReacquired,
                    _session.LeaderName + " was not safely reacquired at the final destination.", false);
                return;
            }

            _session.State = ExpeditionState.Arrived;
            _terminalAt = Time.time;
            FollowController.Stop();

            // The leader is a freshly spawned avatar with the new zone's own default state. Reapplying the
            // guard spot saved in the previous zone would push it at a coordinate from a different scene,
            // so post-arrival state is deliberately left to the game.
            Say("[Erenshor Expedition] Arrived in " + _session.DestinationName + ". " + _session.LeaderName +
                " led the way" + (_session.CombatInterruptions > 0 ? " through " + _session.CombatInterruptions + " fight(s)." : "."), "lightblue");

            _session.LeaderRuntime = leader;
            ExpeditionPhaseTelemetry.Record("arrived",
                "destination=" + SafeTelemetryName(_session.DestinationName) + " exactLeader=true");
            Emit("expedition_arrived");
            RememberForReturn();
        }

        private static SimPlayer ReacquireLeader()
        {
            SimPlayerTracking tracking = _session.LeaderTracking;
            if (tracking == null) return null;
            SimPlayer avatar = SimTrackingRebind.CurrentAvatar(tracking);
            if (!SimTrackingRebind.AvatarMatchesTracking(tracking, avatar)) return null;
            return LeaderValid(avatar) ? avatar : null;
        }

        private static void RememberForReturn()
        {
            _lastOriginZone = _session.OriginZone;
            _lastLeaderTracking = _session.LeaderTracking;
            _lastLeaderName = _session.LeaderName;
            _lastCompletedAt = Time.time;
        }

        // --- helpers ------------------------------------------------------------------------------

        private static string ReturnOriginZone()
        {
            if (IsActive && !string.IsNullOrWhiteSpace(_session.OriginZone)) return _session.OriginZone;
            if (_lastLeaderTracking == null || string.IsNullOrWhiteSpace(_lastOriginZone)) return null;
            return Time.time - _lastCompletedAt <= ReturnOfferSeconds ? _lastOriginZone : null;
        }

        private static SimPlayer ResolveReturnLeader()
        {
            SimPlayerTracking tracking = IsActive ? _session.LeaderTracking : _lastLeaderTracking;
            if (tracking == null) return null;
            SimPlayer avatar = SimTrackingRebind.CurrentAvatar(tracking);
            if (!SimTrackingRebind.AvatarMatchesTracking(tracking, avatar)) return null;
            return LeaderValid(avatar) ? avatar : null;
        }

        // Every acquisition and reacquisition re-runs the full ownership guard, including after zoning.
        private static bool LeaderValid(SimPlayer sim)
        {
            if (sim == null) return false;
            if (!FollowController.IsUsableSim(sim)) return false;
            if (CoopCompatibility.IsRemoteHuman(sim)) return false;
            return LeaderController.IsPlayerPartySim(sim);
        }

        private static ExpeditionFailureReason LeaderFailureReason()
        {
            SimPlayer sim = _session.LeaderRuntime;
            if (sim == null || !FollowController.IsUsableSim(sim)) return ExpeditionFailureReason.LeaderUnavailable;
            if (CoopCompatibility.IsRemoteHuman(sim)) return ExpeditionFailureReason.LeaderRemote;
            return ExpeditionFailureReason.LeaderLeftParty;
        }

        private static SimPlayerTracking ReadTracking(SimPlayer sim)
        {
            try { return sim == null ? null : sim.MySimTracking; }
            catch { return null; }
        }

        private static void ReleaseLeg(bool leaderStillValid)
        {
            _releasingLeg = true;
            try { LeaderController.ReleaseExpedition(leaderStillValid); }
            catch { }
            finally { _releasingLeg = false; }
        }

        private static void ClearSession()
        {
            _session = null;
            _terminalEmitted = false;
            _terminalAt = 0f;
            _transitionSince = 0f;
            _sceneSettledSince = 0f;
            _postZoneRouteReadinessSince = 0f;
            _nextPostZoneRouteProbeAt = 0f;
            _postZoneRouteProbeCount = 0;
            _postZoneRouteLastObservation = null;
            _postZoneCarriedPause = ExpeditionPauseReason.None;
            _externalOverride = false;
            DrainedEvents.Clear();
        }

        internal static void Shutdown()
        {
            if (IsActive) Cancel("the plugin is unloading.");
            ClearSession();
        }

        private static bool IsTerminal(ExpeditionState state)
        {
            return state == ExpeditionState.Arrived || state == ExpeditionState.Cancelled || state == ExpeditionState.Failed;
        }

        private static string ActiveScene()
        {
            try { return SceneManager.GetActiveScene().name; }
            catch { return null; }
        }

        private static bool SameScene(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeTelemetryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            string result = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return result.Length <= 80 ? result : result.Substring(0, 80);
        }

        private static string SafeDiagnostic(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            string result = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return result.Length <= 1400 ? result : result.Substring(0, 1400);
        }

        private static void LogPostZoneProbe(string detail)
        {
            try
            {
                if (ErenshorFollowPlugin.Instance != null)
                    ErenshorFollowPlugin.Instance.LogInfo("[Expedition route readiness] session=" +
                        (_session == null ? 0 : _session.SessionId) + " | " + SafeDiagnostic(detail));
            }
            catch { }
        }

        private static void TraceRejectedAttempt(ExpeditionInitiation source, string destination, string failure)
        {
            string detail = "source=" + source + " destination=" + SafeTelemetryName(destination) +
                " reason=" + (string.IsNullOrWhiteSpace(failure) ? "unspecified validation failure" : failure);
            if (IsActive)
            {
                // Do not overwrite the active session's telemetry identity merely because a second
                // setup/command was rejected while that expedition continues running.
                ExpeditionPhaseTelemetry.Record("command_rejected", detail);
                return;
            }
            ExpeditionPhaseTelemetry.Begin(_nextSessionId);
            ExpeditionPhaseTelemetry.Record("command_received",
                "source=" + source + " destination=" + SafeTelemetryName(destination));
            ExpeditionPhaseTelemetry.Record("command_rejected", detail);
        }

        // Terminal events are emitted exactly once per session; arrival is the one that matters socially.
        private static void Emit(string eventType)
        {
            if (_session == null) return;
            bool terminal = eventType == "expedition_arrived" || eventType == "expedition_cancelled" || eventType == "expedition_failed";
            if (terminal)
            {
                if (_terminalEmitted) return;
                _terminalEmitted = true;
            }
            ExpeditionIntegrationBridge.Emit(eventType, _session);
        }

        internal static string Diagnostics()
        {
            if (_session == null) return "[Erenshor Expedition] identity: no active session | scene=" + ActiveScene() +
                " | phase=" + ExpeditionPhaseTelemetry.Describe();
            SimPlayerTracking tracking = _session.LeaderTracking;
            SimPlayer avatar = tracking == null ? null : SimTrackingRebind.CurrentAvatar(tracking);
            bool same = tracking != null && avatar != null && SimTrackingRebind.AvatarMatchesTracking(tracking, avatar);
            bool inGroup = tracking != null && SimTrackingRebind.TrackingIsInPlayerGroup(tracking);
            bool localParty = avatar != null && LeaderController.IsPlayerPartySim(avatar);
            bool remote = avatar != null && CoopCompatibility.IsRemoteHuman(avatar);
            return "[Erenshor Expedition] identity: tracking=" + (tracking == null ? "missing" : "present") +
                " avatar=" + (avatar == null ? "missing" : "present") +
                " exact=" + same +
                " trackingInGroup=" + inGroup +
                " localParty=" + localParty +
                " remote=" + remote +
                " scene=" + ActiveScene() +
                " state=" + DescribeState(_session.State) +
                " routeIndex=" + _session.CurrentRouteIndex +
                "/" + Math.Max(0, _session.PlannedZones.Count - 1) +
                " | phase=" + ExpeditionPhaseTelemetry.Describe();
        }

        internal static string Status()
        {
            if (_session == null)
            {
                string origin = ReturnOriginZone();
                string suffix = CanReturn() ? " Return to " + origin + " is available (/expedition return)." : string.Empty;
                return "[Erenshor Expedition] No expedition is active." + suffix;
            }
            string line = "[Erenshor Expedition] " + _session.LeaderName + " -> " + _session.DestinationName +
                          (_session.Objective == ExpeditionObjective.Return ? " (return)" : string.Empty) +
                          " | " + DescribeState(_session.State);
            if (!string.IsNullOrWhiteSpace(_session.CurrentLegDestinationName) &&
                !SameScene(_session.CurrentLegDestinationName, _session.DestinationName))
                line += " | next exit: " + _session.CurrentLegDestinationName;
            if (_session.PlannedZones.Count > 2)
                line += " | leg " + Math.Min(_session.CurrentRouteIndex + 1, _session.PlannedZones.Count - 1) +
                    "/" + (_session.PlannedZones.Count - 1);
            if (_session.State == ExpeditionState.Paused) line += " (" + DescribePause(_session.PauseReason) + ")";
            if (_session.State == ExpeditionState.Transitioning)
            {
                SimPlayerTracking tracking = _session.LeaderTracking;
                SimPlayer avatar = SimTrackingRebind.CurrentAvatar(tracking);
                string identity = tracking == null ? "missing" :
                    (avatar == null ? "waiting" : (SimTrackingRebind.AvatarMatchesTracking(tracking, avatar) ? "reacquired" : "MISMATCH"));
                line += " | leader identity: " + identity + " | scene: " + ActiveScene();
                if (_session.RouteReadinessPending) line += " | checking route to next zone...";
            }
            if ((_session.State == ExpeditionState.Cancelled || _session.State == ExpeditionState.Failed) &&
                !string.IsNullOrWhiteSpace(_session.FailureDetail))
                line += " : " + _session.FailureDetail;
            if (_session.CombatInterruptions > 0) line += " | combat interruptions: " + _session.CombatInterruptions;
            return line;
        }

        internal static string DescribeState(ExpeditionState state)
        {
            switch (state)
            {
                case ExpeditionState.Forming: return "Forming";
                case ExpeditionState.Traveling: return "Traveling";
                case ExpeditionState.CombatInterrupted: return "Combat interrupted";
                case ExpeditionState.Regrouping: return "Regrouping";
                case ExpeditionState.Paused: return "Paused";
                case ExpeditionState.Transitioning: return "Changing zones";
                case ExpeditionState.Arrived: return "Arrived";
                case ExpeditionState.Cancelled: return "Cancelled";
                case ExpeditionState.Failed: return "Failed";
                default: return "Idle";
            }
        }

        private static string DescribePause(ExpeditionPauseReason reason)
        {
            switch (reason)
            {
                case ExpeditionPauseReason.PlayerManualMovement: return "you took over movement";
                case ExpeditionPauseReason.PlayerGroupOrder: return "you gave the group a direct order";
                case ExpeditionPauseReason.GroupCouldNotCatchUp: return "the group fell behind";
                case ExpeditionPauseReason.PlayerRequest: return "you asked to hold";
                default: return "held";
            }
        }

        private static string DescribeFailure(ExpeditionFailureReason reason)
        {
            switch (reason)
            {
                case ExpeditionFailureReason.LeaderUnavailable: return "the leader is no longer available.";
                case ExpeditionFailureReason.LeaderLeftParty: return "the leader left the party.";
                case ExpeditionFailureReason.LeaderRemote: return "the leader is controlled by another client.";
                case ExpeditionFailureReason.RouteFailed: return "the route could not be completed.";
                case ExpeditionFailureReason.DestinationLost: return "the destination is no longer available.";
                case ExpeditionFailureReason.UnexpectedZone: return "the group ended up somewhere unexpected.";
                case ExpeditionFailureReason.LeaderNotReacquired: return "the leader could not be found after zoning.";
                default: return "the expedition could not continue.";
            }
        }

        private static void Say(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
        }
    }
}
