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
                return new ExpeditionStatusSnapshot(false, ExpeditionState.Idle, ExpeditionObjective.Outbound, null, null, ExpeditionPauseReason.None, 0);
            return new ExpeditionStatusSnapshot(!IsTerminal(_session.State), _session.State, _session.Objective,
                _session.LeaderName, _session.DestinationName, _session.PauseReason, _session.CombatInterruptions);
        }

        // --- lifecycle entry points -------------------------------------------------------------

        internal static void Start(SimPlayer leader, ExpeditionDestination destination, ExpeditionInitiation source)
        {
            List<string> route = new List<string>();
            route.Add(ActiveScene());
            if (destination != null) route.Add(destination.CanonicalName);
            Start(leader, destination, route, source, ExpeditionObjective.Outbound);
        }

        internal static void StartRoute(SimPlayer leader, IList<string> plannedZones, ExpeditionInitiation source)
        {
            if (plannedZones == null || plannedZones.Count < 2)
            {
                Say("[Erenshor Expedition] That route does not leave the current zone.", "yellow");
                return;
            }
            bool ambiguous;
            ExpeditionDestination firstLeg = ExpeditionDestinationResolver.Resolve(plannedZones[1], out ambiguous);
            if (firstLeg == null)
            {
                Say("[Erenshor Expedition] The first atlas hop to " + plannedZones[1] +
                    " is not a verified live exit from this zone.", "yellow");
                return;
            }
            Start(leader, firstLeg, plannedZones, source, ExpeditionObjective.Outbound);
        }

        private static void Start(SimPlayer leader, ExpeditionDestination destination, IList<string> plannedZones,
            ExpeditionInitiation source, ExpeditionObjective objective)
        {
            if (IsActive) Cancel("Starting a new expedition.");
            ClearSession();

            if (destination == null)
            {
                Say("[Erenshor Expedition] That is not a verified adjacent zone. Use /expedition to see the current list.", "yellow");
                return;
            }

            string failure;
            if (!LeaderController.StartExpeditionLeg(leader, destination, out failure))
            {
                Say("[Erenshor Expedition] " + failure, "yellow");
                return;
            }

            ExpeditionSession session = new ExpeditionSession(_nextSessionId++);
            session.Objective = objective;
            session.Purpose = objective == ExpeditionObjective.Return ? ExpeditionPurpose.ReturnToOrigin : ExpeditionPurpose.TravelToZone;
            session.LeaderRuntime = leader;
            session.LeaderTracking = ReadTracking(leader);
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
                ". Combat pauses the trip; /expedition pause holds it.", "lightblue");
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

            LeaderController.HoldForExpedition();
            _session.State = ExpeditionState.Paused;
            _session.PauseReason = reason;
            Emit("expedition_paused");
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

            Start(leader, destination, route, ExpeditionInitiation.Command, ExpeditionObjective.Return);
            if (IsActive) Emit("expedition_returning");
        }

        internal static void Cancel(string reason)
        {
            if (!IsActive) return;
            _session.State = ExpeditionState.Cancelled;
            _session.FailureDetail = reason;
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
            _session.FailureDetail = detail;
            ReleaseLeg(leaderStillValid);
            _terminalAt = Time.time;
            Emit("expedition_failed");
            Say("[Erenshor Expedition] Expedition ended: " + (string.IsNullOrWhiteSpace(detail) ? DescribeFailure(reason) : detail), "yellow");
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
            if (IsActive) _externalOverride = true;
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
                    Fail(ExpeditionFailureReason.RouteFailed, "no walkable route to " + _session.DestinationName + ".", true);
                    break;

                case LeaderController.LegEvent.LeaderInvalid:
                    Fail(LeaderFailureReason(), null, false);
                    break;

                case LeaderController.LegEvent.GroupCouldNotCatchUp:
                    // Preserved from the existing Lead behavior. Whether this should become a Paused state
                    // instead is an open question for live play; see EXPEDITIONS_DESIGN.md section 12.
                    Fail(ExpeditionFailureReason.RouteFailed, "the group could not catch up.", true);
                    break;
            }
        }

        // --- zone transition ---------------------------------------------------------------------

        private static void EnterTransitioning()
        {
            if (_session == null || _session.State == ExpeditionState.Transitioning) return;
            _session.State = ExpeditionState.Transitioning;
            _transitionSince = Time.time;
            _sceneSettledSince = 0f;
            // The leader avatar is destroyed by zoning, so release without touching it.
            ReleaseLeg(false);
            _session.LeaderRuntime = null;
        }

        private static void TickTransition()
        {
            string scene = ActiveScene();
            if (SameScene(scene, _session.CurrentZone))
            {
                if (Time.time - _transitionSince >= TransitionTimeoutSeconds)
                    Fail(ExpeditionFailureReason.InternalError, "the zone transition never completed.", false);
                return;
            }
            if (GameData.Zoning)
            {
                _sceneSettledSince = 0f;
                return;
            }
            // Group data is rebuilt during zone setup; wait for it to settle rather than guessing a frame.
            if (GameData.SimMngr == null || GameData.SimPlayerGrouping == null)
            {
                _sceneSettledSince = 0f;
                if (Time.time - _transitionSince >= TransitionTimeoutSeconds)
                    Fail(ExpeditionFailureReason.InternalError, "the game did not settle after zoning.", false);
                return;
            }
            if (_sceneSettledSince <= 0f) _sceneSettledSince = Time.time;
            if (Time.time - _sceneSettledSince < TransitionSettleSeconds) return;

            CompleteTransition(scene);
        }

        private static void CompleteTransition(string scene)
        {
            _session.CurrentZone = scene;
            if (!_session.VerifiedZonesCrossed.Contains(scene)) _session.VerifiedZonesCrossed.Add(scene);
            Emit("expedition_zone_entered");

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
            _session.CurrentRouteIndex++;
            if (SameScene(scene, _session.DestinationName) || _session.CurrentRouteIndex >= _session.PlannedZones.Count - 1)
            {
                Arrive();
                return;
            }
            ContinueNextLeg();
        }

        private static void ContinueNextLeg()
        {
            SimPlayer leader = ReacquireLeader();
            if (leader == null)
            {
                Fail(ExpeditionFailureReason.LeaderNotReacquired,
                    _session.LeaderName + " was not reacquired after entering " + _session.CurrentZone + ".", false);
                return;
            }
            List<string> replanned;
            bool routeAmbiguous;
            string routeFailure;
            if (!ZoneAtlasRoutePlanner.TryBuild(_session.CurrentZone, _session.DestinationName,
                ExpeditionDestinationResolver.ListCanonicalNames(), out replanned, out routeAmbiguous, out routeFailure) ||
                replanned.Count < 2)
            {
                Fail(ExpeditionFailureReason.DestinationLost,
                    string.IsNullOrWhiteSpace(routeFailure) ? "no safe live next hop is available from " + _session.CurrentZone + "." : routeFailure,
                    false);
                return;
            }
            if (_session.PlannedZones.Count > _session.CurrentRouteIndex + 1)
                _session.PlannedZones.RemoveRange(_session.CurrentRouteIndex + 1,
                    _session.PlannedZones.Count - (_session.CurrentRouteIndex + 1));
            for (int i = 1; i < replanned.Count; i++) _session.PlannedZones.Add(replanned[i]);
            int nextIndex = _session.CurrentRouteIndex + 1;
            string nextZone = _session.PlannedZones[nextIndex];
            bool ambiguous;
            ExpeditionDestination nextLeg = ExpeditionDestinationResolver.Resolve(nextZone, out ambiguous);
            if (nextLeg == null || !SameScene(nextLeg.CanonicalName, nextZone))
            {
                Fail(ExpeditionFailureReason.DestinationLost,
                    "the next atlas hop to " + nextZone + " is not a verified live exit from " + _session.CurrentZone + ".", false);
                return;
            }
            string failure;
            if (!LeaderController.StartExpeditionLeg(leader, nextLeg, out failure))
            {
                Fail(ExpeditionFailureReason.RouteFailed, failure, true);
                return;
            }
            _session.LeaderRuntime = leader;
            _session.Destination = nextLeg;
            _session.State = ExpeditionState.Traveling;
            _session.PauseReason = ExpeditionPauseReason.None;
            _transitionSince = 0f;
            _sceneSettledSince = 0f;
            Say("[Erenshor Expedition] Continuing through " + _session.CurrentZone + " toward " +
                _session.DestinationName + "; next exit is " + nextZone + ".", "lightblue");
        }

        // v1 arrival: the game actually transitioned and the active scene is the canonical destination.
        private static void Arrive()
        {
            SimPlayer leader = ReacquireLeader();
            _session.State = ExpeditionState.Arrived;
            _terminalAt = Time.time;
            FollowController.Stop();

            // The leader is a freshly spawned avatar with the new zone's own default state. Reapplying the
            // guard spot saved in the previous zone would push it at a coordinate from a different scene,
            // so post-arrival state is deliberately left to the game.
            Say("[Erenshor Expedition] Arrived in " + _session.DestinationName + ". " + _session.LeaderName +
                " led the way" + (_session.CombatInterruptions > 0 ? " through " + _session.CombatInterruptions + " fight(s)." : "."), "lightblue");
            if (leader == null)
                Say("[Erenshor Expedition] " + _session.LeaderName + " is no longer with the group on this side.", "yellow");

            _session.LeaderRuntime = leader;
            Emit("expedition_arrived");
            RememberForReturn();
        }

        private static SimPlayer ReacquireLeader()
        {
            SimPlayerTracking tracking = _session.LeaderTracking;
            if (tracking == null) return null;
            SimPlayer avatar = null;
            try { avatar = tracking.MyAvatar; } catch { return null; }
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
            SimPlayer avatar = null;
            try { avatar = tracking.MyAvatar; } catch { return null; }
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
