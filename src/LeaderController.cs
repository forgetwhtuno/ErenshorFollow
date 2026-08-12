using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    internal static class LeaderController
    {
        internal enum TravelState { Idle, Moving, PausedForCombat, ResumingAfterCombat, WaitingForPlayer, Regrouping, PartialRouteRetry, NoProgress, Held }

        internal enum LegEvent
        {
            CombatDetected,
            CombatCleared,
            WaitingForPlayer,
            PlayerRegrouped,
            FollowManualOverride,
            RouteFailed,
            LeaderInvalid,
            GroupCouldNotCatchUp
        }

        internal struct StatusSnapshot
        {
            internal readonly bool Active;
            internal readonly string LeaderName;
            internal readonly string DestinationName;
            internal readonly TravelState State;

            internal StatusSnapshot(bool active, string leaderName, string destinationName, TravelState state)
            {
                Active = active;
                LeaderName = leaderName;
                DestinationName = destinationName;
                State = state;
            }
        }

        private static readonly NavMeshPath RouteCheckPath = new NavMeshPath();
        private static readonly List<LegEvent> PendingEvents = new List<LegEvent>();
        private static readonly List<LocalZoneRoutePlanner.RouteOption> ZoneRouteOptions = new List<LocalZoneRoutePlanner.RouteOption>();
        private static readonly List<Vector3> ZoneWaypoints = new List<Vector3>();
        private static SimPlayer _leader;
        private static Zoneline _destination;
        private static ExpeditionDestination _expeditionDestination;
        private static NPC _monster;
        private static string _monsterName;
        private static string _leaderName;
        private static string _destinationName;
        private static string _startingScene;
        private static bool _active;
        private static bool _pausedForCombat;
        private static bool _waitingForPlayer;
        private static float _waitingSince;
        private static float _nextNativeNavRefresh;
        private static float _combatClearSince;
        private static bool _originalGuardSpot;
        private static Vector3 _originalGuardPosition;
        private static Vector3 _lastLeaderProgressPosition;
        private static Vector3 _lastPartialEndpoint;
        private static bool _lastRouteWasPartial;
        private static float _lastLeaderProgressAt;
        private static float _routeProblemSince;
        private static float _nextRouteValidationAt;
        private static bool _expeditionOwned;
        private static bool _expeditionHeld;
        private static bool _regroupAfterCombat;
        private static float _regroupStableSince;
        private static int _zoneRouteIndex;
        private static Vector3 _zoneApproach;
        private static RouteCandidatePolicy.Evaluation _zoneEvaluation;
        // Failure context for the current zone leg. "Accepted candidate" means the planner produced at
        // least one option that passed RouteCandidatePolicy acceptance -- not merely that a Zoneline or a
        // raw sampled point existed. Both fields are reset by RebuildZoneOptions, which is the only entry
        // point for a leg start or an explicit resume, so they cannot leak from a previous leg or
        // expedition. They are deliberately NOT cleared by teardown: FailRoute only queues RouteFailed,
        // and ExpeditionCoordinator reads this context on a later tick, after teardown may have run.
        private static bool _legHadAcceptedCandidate;
        private static string _legRouteFailureReason;
        private static RouteCandidatePolicy.RouteFailureKind _legRouteFailureKind;
        private static bool _nativeProofPending;
        private static float _nativeProofSince;
        private static Vector3 _nativeProofStartPosition;
        private static float _nativeProofStartDistance;
        private static float _boundaryGraceSince;
        private static int _zoneWaypointIndex;

        private const float MaximumNearbyMonsterDistance = 60f;
        private const float RouteRetrySeconds = 3f;
        private const float NoProgressFailureSeconds = 5f;
        private const float WaitGap = 8f;
        private const float ResumeGap = 4.5f;
        private const float CatchUpTimeoutSeconds = 12f;
        private const float CombatSafetySeconds = 5f;
        private const float RegroupSettleSeconds = 0.75f;
        private const float NativeProofSeconds = 2.75f;
        private const float NativeProofMoveDistance = 0.75f;
        private const float NativeProofCloserDistance = 0.50f;
        private const float BoundaryGraceDistance = 5.5f;
        private const float BoundaryGraceSeconds = 8f;
        private const float WaypointReachedDistance = 1.35f;

        internal static bool ExpeditionOwned { get { return _expeditionOwned; } }
        internal static bool LegActive { get { return _active; } }

        internal static void StartSmart(SimPlayer leader, string requestedDestination)
        {
            if (!FollowController.IsUsableSim(leader) || !IsGroupedWithPlayer(leader))
            {
                Say("[Erenshor Lead] The leader must be a living Sim in your current party.", "yellow");
                return;
            }

            bool zoneAmbiguous;
            ExpeditionDestination zone = ExpeditionDestinationResolver.Resolve(requestedDestination, out zoneAmbiguous);
            if (zone != null)
            {
                ExpeditionCoordinator.Start(leader, zone, ExpeditionInitiation.Command);
                return;
            }
            if (zoneAmbiguous)
            {
                Say("[Erenshor Lead] More than one adjacent zone matches that destination. Type a longer name.", "yellow");
                return;
            }
            if (ExpeditionDestinationResolver.MatchesOnlyPartyRemovingExit(requestedDestination))
            {
                Say("[Erenshor Lead] That crossing dismisses your party, so no one can lead you through it.", "yellow");
                return;
            }

            List<string> atlasRoute;
            bool atlasAmbiguous;
            string atlasFailure;
            if (ZoneAtlasRoutePlanner.TryBuild(SceneManager.GetActiveScene().name, requestedDestination,
                ExpeditionDestinationResolver.ListCanonicalNames(),
                out atlasRoute, out atlasAmbiguous, out atlasFailure) && atlasRoute.Count >= 2)
            {
                ExpeditionCoordinator.StartRoute(leader, atlasRoute, ExpeditionInitiation.Command);
                return;
            }
            if (atlasAmbiguous)
            {
                Say("[Erenshor Lead] More than one world zone matches that destination. Type a longer name.", "yellow");
                return;
            }

            bool simAmbiguous;
            SimPlayer person = ErenshorFollowPlugin.FindSim(requestedDestination, out simAmbiguous);
            if (person != null && person != leader)
            {
                Stop(null);
                string leaderName = FollowController.ReadName(leader);
                string personName = FollowController.ReadName(person);
                FollowController.Start(person, personName);
                Say(leaderName + " tells the group: There they are. Follow " + personName + "!", "lightblue");
                return;
            }

            bool ambiguous;
            NPC monster = FindMonster(requestedDestination, out ambiguous);
            if (monster != null)
            {
                StartMonster(leader, monster);
                return;
            }

            string name = FollowController.ReadName(leader);
            Say(name + " tells the group: Sorry, I don't know where that is. " + DescribeShortChoices(), "lightblue");
        }

        internal static bool StartExpeditionLeg(SimPlayer leader, ExpeditionDestination destination, out string failure)
        {
            failure = null;
            Stop(null);
            if (!FollowController.IsUsableSim(leader) || !IsGroupedWithPlayer(leader))
            {
                failure = "The leader must be a living Sim in your current party.";
                return false;
            }
            if (CoopCompatibility.IsRemoteHuman(leader))
            {
                failure = "That leader is controlled by another client.";
                return false;
            }
            if (destination == null || destination.CrossingCount == 0)
            {
                failure = "That destination is no longer available.";
                return false;
            }
            if (InCombat(leader))
            {
                failure = "Finish combat before starting an expedition.";
                return false;
            }

            InitializeZoneLeg(leader, destination, true);
            if (!RebuildZoneOptions() || !ApplyTravelOrder())
            {
                // RebuildZoneOptions has already recorded whether acceptance produced any candidate, so
                // startup can distinguish "nothing was ever verified" from "verified approaches existed but
                // none could be ordered" without inspecting runtime objects after Stop(). Failing to assign
                // a travel order is not a crossing claim, so it stays TravelExecutionFailed.
                RouteCandidatePolicy.RouteFailureKind startKind = RouteCandidatePolicy.ResolveFailureKind(
                    _legHadAcceptedCandidate, RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
                Stop(null);
                failure = SentenceCase(RouteCandidatePolicy.DescribeRouteFailure(
                    destination.CanonicalName, startKind, "no verified approach could be ordered"));
                return false;
            }
            return true;
        }

        private static void InitializeZoneLeg(SimPlayer leader, ExpeditionDestination destination, bool expeditionOwned)
        {
            _leader = leader;
            _destination = null;
            _expeditionDestination = destination;
            _monster = null;
            _monsterName = null;
            _leaderName = FollowController.ReadName(leader);
            _destinationName = destination.CanonicalName;
            InitializeCommon(leader, expeditionOwned);
        }

        private static void InitializeCommon(SimPlayer leader, bool expeditionOwned)
        {
            _startingScene = SceneManager.GetActiveScene().name;
            _originalGuardSpot = leader.GuardSpot;
            _originalGuardPosition = leader.GetGuardPos();
            _active = true;
            _pausedForCombat = false;
            _waitingForPlayer = false;
            _waitingSince = 0f;
            _expeditionOwned = expeditionOwned;
            _expeditionHeld = false;
            _regroupAfterCombat = false;
            _regroupStableSince = 0f;
            _zoneRouteIndex = -1;
            _zoneApproach = Vector3.zero;
            _zoneEvaluation = null;
            _nativeProofPending = false;
            _nativeProofSince = 0f;
            _boundaryGraceSince = 0f;
            _lastLeaderProgressAt = 0f;
            _routeProblemSince = 0f;
            _nextRouteValidationAt = 0f;
            PendingEvents.Clear();
        }

        internal static void Tick()
        {
            if (!_active) return;
            string scene = SceneManager.GetActiveScene().name;
            if (!string.Equals(scene, _startingScene, StringComparison.OrdinalIgnoreCase))
            {
                if (_expeditionOwned)
                {
                    ClearWithoutRestore();
                    FollowController.Stop();
                    return;
                }
                if (_monster != null)
                {
                    Stop("The hunt stopped after changing zones.");
                    return;
                }
                if (SceneMatchesDestination(scene))
                {
                    string arrived = _destinationName;
                    ClearWithoutRestore();
                    FollowController.Stop();
                    Say("[Erenshor Lead] Arrived in " + arrived + ".", "lightblue");
                }
                else
                {
                    Stop("Travel was interrupted by an unexpected zone change to " + scene + ".");
                }
                return;
            }

            if (!FollowController.IsUsableSim(_leader) || !IsGroupedWithPlayer(_leader))
            {
                if (_expeditionOwned) { Report(LegEvent.LeaderInvalid); return; }
                Stop("Leader travel stopped because the party or destination changed.");
                return;
            }
            if (_expeditionOwned && CoopCompatibility.IsRemoteHuman(_leader))
            {
                Report(LegEvent.LeaderInvalid);
                return;
            }
            if (_monster == null && _expeditionDestination != null && !CurrentCrossingUsable())
            {
                FailRoute("the selected crossing disappeared", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed);
                return;
            }
            if (_destination == null && _monster == null)
            {
                if (_expeditionOwned) { Report(LegEvent.RouteFailed); return; }
                Stop("Leader travel stopped because the destination changed.");
                return;
            }

            if (_expeditionHeld) return;
            if (!_pausedForCombat && !FollowController.Active)
            {
                if (_expeditionOwned)
                {
                    if (FollowController.LastStopReason == FollowController.StopReason.ManualMovement)
                        Report(LegEvent.FollowManualOverride);
                    else
                        FailRoute("player follow could not keep a route to the leader", RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
                    return;
                }
                Stop("Leader travel stopped because following was cancelled or no route was available.");
                return;
            }

            if (_monster != null)
            {
                bool monsterUsable = IsUsableMonster(_monster);
                if (!monsterUsable)
                {
                    string defeated = _monsterName;
                    Stop(null);
                    Say(_leaderName + " tells the group: Looks like " + defeated + " is already gone.", "lightblue");
                    return;
                }
                if (!IsMonsterNearby(_monster, MaximumNearbyMonsterDistance + 10f, monsterUsable))
                {
                    Stop("The nearby-monster lead stopped because the target moved out of the local area.");
                    return;
                }
            }

            bool combat = InCombat(_leader);
            if (_monster != null && combat && Vector3.Distance(_leader.transform.position, _monster.transform.position) <= 18f)
            {
                string found = _monsterName;
                Stop(null);
                Say(_leaderName + " tells the group: There's " + found + "—get ready!", "lightblue");
                return;
            }
            if (combat && !_pausedForCombat)
            {
                _pausedForCombat = true;
                _combatClearSince = 0f;
                _waitingForPlayer = false;
                _waitingSince = 0f;
                _regroupAfterCombat = false;
                _regroupStableSince = 0f;
                _nativeProofPending = false;
                _boundaryGraceSince = 0f;
                try { _leader.FreeFollow(); } catch { }
                FollowController.Stop();
                Report(LegEvent.CombatDetected);
                LegSay("[Erenshor Lead] Travel paused for combat.", "yellow");
                return;
            }
            if (_pausedForCombat)
            {
                if (combat) { _combatClearSince = 0f; return; }
                if (_combatClearSince <= 0f) _combatClearSince = Time.time;
                if (Time.time - _combatClearSince < CombatSafetySeconds) return;
                _pausedForCombat = false;
                _combatClearSince = 0f;
                Report(LegEvent.CombatCleared);
                if (!BeginRegroupHold(true))
                {
                    FailRoute("could not enter the post-combat regroup hold", RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
                    return;
                }
                return;
            }

            float playerGap = PlayerGap();
            if (!_waitingForPlayer && playerGap > WaitGap)
            {
                if (!BeginRegroupHold(false))
                {
                    FailRoute("could not hold the leader for regrouping", RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
                    return;
                }
                Report(LegEvent.WaitingForPlayer);
                LegSay(_leaderName + " tells the group: I'll wait here—catch up.", "lightblue");
                return;
            }
            if (_waitingForPlayer)
            {
                if (Time.time - _waitingSince >= CatchUpTimeoutSeconds)
                {
                    if (_expeditionOwned) { Report(LegEvent.GroupCouldNotCatchUp); return; }
                    Stop("Leader travel stopped because the group could not catch up.");
                    return;
                }
                if (playerGap > ResumeGap)
                {
                    _regroupStableSince = 0f;
                    return;
                }
                if (_regroupStableSince <= 0f) _regroupStableSince = Time.time;
                if (Time.time - _regroupStableSince < RegroupSettleSeconds) return;
                bool afterCombat = _regroupAfterCombat;
                _waitingForPlayer = false;
                _waitingSince = 0f;
                _regroupStableSince = 0f;
                _regroupAfterCombat = false;
                if (!ApplyTravelOrder())
                {
                    FailRoute("could not reapply the selected route after regrouping", RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
                    return;
                }
                Report(LegEvent.PlayerRegrouped);
                LegSay(afterCombat
                    ? "[Erenshor Lead] Combat is clear. " + _leaderName + " is resuming travel."
                    : _leaderName + " tells the group: Ready? Let's keep moving.", "lightblue");
                return;
            }

            if (_monster == null && _nativeProofPending)
            {
                if (HandleNativeProof()) return;
            }

            if (_monster == null) AdvanceZoneWaypointIfReached();

            if (_monster == null && BoundaryGraceActive())
            {
                RefreshNativeNavigation(false, Vector3.zero);
                return;
            }

            if (!ValidateLeaderRoute())
            {
                FailRoute("route validation stopped making useful progress", RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
                return;
            }
            RefreshNativeNavigation(false, Vector3.zero);

            NavMeshAgent nav = _leader.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isOnNavMesh && nav.hasPath && nav.pathStatus == NavMeshPathStatus.PathInvalid)
                FailRoute("the native NavMeshAgent reported PathInvalid", RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed);
        }

        private static bool BeginRegroupHold(bool afterCombat)
        {
            if (_leader == null) return false;
            try { _leader.AssignGuardSpot(_leader.transform.position); } catch { return false; }
            FollowController.Start(_leader, _leaderName);
            _waitingForPlayer = true;
            _waitingSince = Time.time;
            _regroupAfterCombat = afterCombat;
            _regroupStableSince = 0f;
            return true;
        }

        // siteKind is supplied by the failure site, never inferred from the reason text. Callers that are not
        // specifically about reaching the selected crossing must pass TravelExecutionFailed.
        private static void FailRoute(string reason, RouteCandidatePolicy.RouteFailureKind siteKind)
        {
            if (_expeditionOwned && _monster == null && TryNextZoneOption(reason)) return;
            string routeTarget = _monster == null ? _destinationName : _monsterName;
            Verbose("all verified route candidates failed for " + routeTarget + (string.IsNullOrWhiteSpace(reason) ? string.Empty : ": " + reason));
            // Capture the terminal reason and category now: RouteFailed is only queued here, and
            // ExpeditionCoordinator reads this context on a later tick once teardown may already have run.
            _legRouteFailureReason = reason;
            _legRouteFailureKind = RouteCandidatePolicy.ResolveFailureKind(_legHadAcceptedCandidate, siteKind);
            if (_expeditionOwned) { Report(LegEvent.RouteFailed); return; }
            Stop(SentenceCase(RouteCandidatePolicy.DescribeRouteFailure(routeTarget, _legRouteFailureKind, reason)));
        }

        // Semantic snapshot of why the current leg's routing ended, for the expedition owner to phrase.
        internal struct RouteFailureContext
        {
            internal readonly RouteCandidatePolicy.RouteFailureKind Kind;
            internal readonly string Reason;
            internal RouteFailureContext(RouteCandidatePolicy.RouteFailureKind kind, string reason)
            {
                Kind = kind;
                Reason = reason;
            }
        }

        internal static RouteFailureContext LastRouteFailure()
        {
            return new RouteFailureContext(_legRouteFailureKind, _legRouteFailureReason);
        }

        // The pure helper returns a clause so it can be embedded ("Expedition ended: <clause>"). Standalone
        // chat lines need it as a sentence.
        private static string SentenceCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        internal static void DrainEvents(List<LegEvent> into)
        {
            if (into == null) return;
            into.AddRange(PendingEvents);
            PendingEvents.Clear();
        }

        internal static void HoldForExpedition()
        {
            if (!_active || !_expeditionOwned) return;
            _expeditionHeld = true;
            _pausedForCombat = false;
            _waitingForPlayer = false;
            _waitingSince = 0f;
            _regroupAfterCombat = false;
            _regroupStableSince = 0f;
            _combatClearSince = 0f;
            _nativeProofPending = false;
            _boundaryGraceSince = 0f;
            FollowController.Stop();
            try { if (_leader != null) _leader.AssignGuardSpot(_leader.transform.position); } catch { }
            PendingEvents.Clear();
        }

        internal static bool ResumeExpeditionLeg(out string failure)
        {
            failure = null;
            if (!_active || !_expeditionOwned)
            {
                failure = "No expedition leg is active.";
                return false;
            }
            if (!FollowController.IsUsableSim(_leader) || !IsGroupedWithPlayer(_leader) || CoopCompatibility.IsRemoteHuman(_leader))
            {
                failure = "The leader is no longer a living local Sim in your party.";
                return false;
            }
            if (_expeditionDestination == null || _expeditionDestination.CrossingCount == 0)
            {
                failure = "That destination is no longer available.";
                return false;
            }
            if (InCombat(_leader))
            {
                failure = "Travel cannot resume during combat.";
                return false;
            }
            _expeditionHeld = false;
            _lastLeaderProgressAt = 0f;
            _routeProblemSince = 0f;
            PendingEvents.Clear();

            // Explicit resume is a route boundary: rebuild against live geometry from the leader's new
            // position rather than replaying a stale approach captured before the pause.
            if (!RebuildZoneOptions() || !ApplyTravelOrder())
            {
                failure = SentenceCase(RouteCandidatePolicy.DescribeRouteFailure(
                    _destinationName,
                    RouteCandidatePolicy.ResolveFailureKind(
                        _legHadAcceptedCandidate, RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed),
                    "no verified approach could be ordered"));
                return false;
            }
            return true;
        }

        internal static void ReleaseExpedition(bool restoreLeaderState)
        {
            if (restoreLeaderState) Stop(null);
            else
            {
                ClearWithoutRestore();
                FollowController.Stop();
            }
        }

        internal static SimPlayer CurrentLeader { get { return _leader; } }

        private static void Report(LegEvent legEvent)
        {
            if (!_expeditionOwned) return;
            if (!PendingEvents.Contains(legEvent)) PendingEvents.Add(legEvent);
        }

        internal static void Resume()
        {
            if (!_active)
            {
                Say("[Erenshor Lead] No leader trip is active.", "yellow");
                return;
            }
            if (_expeditionOwned)
            {
                ExpeditionCoordinator.Resume();
                return;
            }
            if (InCombat(_leader))
            {
                Say("[Erenshor Lead] Travel cannot resume during combat.", "yellow");
                return;
            }
            _pausedForCombat = false;
            _waitingForPlayer = false;
            _waitingSince = 0f;
            _combatClearSince = 0f;
            _regroupAfterCombat = false;
            _regroupStableSince = 0f;
            if (!ApplyTravelOrder())
            {
                Stop("No walkable route to " + (_monster == null ? _destinationName : _monsterName) + ".");
                return;
            }
            Say("[Erenshor Lead] Resuming travel to " + _destinationName + ".", "lightblue");
        }

        internal static void Stop(string reason)
        {
            if (_active && _leader != null)
            {
                try
                {
                    if (_originalGuardSpot) _leader.AssignGuardSpot(_originalGuardPosition);
                    else _leader.FreeFollow();
                }
                catch { }
            }
            bool wasExpedition = _expeditionOwned;
            ClearWithoutRestore();
            FollowController.Stop();
            if (wasExpedition) ExpeditionCoordinator.NotifyLegTornDown();
            else if (!string.IsNullOrWhiteSpace(reason)) Say("[Erenshor Lead] " + reason, "lightblue");
        }

        internal static string Status()
        {
            if (!_active) return "[Erenshor Lead] No leader trip is active. Use /elead zones to list adjacent zones.";
            return "[Erenshor Lead] " + _leaderName + " -> " + (_monster == null ? _destinationName : _monsterName) + (_pausedForCombat ? " (paused for combat)." : ".");
        }

        internal static StatusSnapshot GetStatusSnapshot()
        {
            TravelState state = TravelState.Idle;
            if (_active)
            {
                if (_expeditionHeld)
                    state = TravelState.Held;
                else if (_pausedForCombat)
                    state = _combatClearSince > 0f ? TravelState.ResumingAfterCombat : TravelState.PausedForCombat;
                else if (_waitingForPlayer)
                    state = _regroupAfterCombat ? TravelState.Regrouping : TravelState.WaitingForPlayer;
                else if (_lastLeaderProgressAt > 0f && Time.time - _lastLeaderProgressAt >= RouteRetrySeconds)
                    state = TravelState.NoProgress;
                else if (_lastRouteWasPartial || _routeProblemSince > 0f || _nativeProofPending)
                    state = TravelState.PartialRouteRetry;
                else
                    state = TravelState.Moving;
            }
            return new StatusSnapshot(_active, _leaderName, _monster == null ? _destinationName : _monsterName, state);
        }

        internal static string DescribeDestinations()
        {
            List<string> names = ExpeditionDestinationResolver.ListCanonicalNames();
            return names.Count == 0
                ? "[Erenshor Lead] No adjacent zone exits are currently available."
                : "[Erenshor Lead] Adjacent zones: " + string.Join(", ", names.ToArray()) + ". Use /elead <SimName> <zone>.";
        }

        internal static List<string> GetMenuDestinations()
        {
            // The menu snapshots this list when opened and provides its own scroll viewport, so all verified
            // live adjacent destinations can be exposed without doing scene scans in OnGUI.
            return ExpeditionDestinationResolver.ListCanonicalNames();
        }

        internal static bool IsPlayerPartySim(SimPlayer sim) { return IsGroupedWithPlayer(sim); }

        private static bool RebuildZoneOptions()
        {
            ZoneRouteOptions.Clear();
            _zoneRouteIndex = -1;
            _legHadAcceptedCandidate = false;
            _legRouteFailureReason = null;
            _legRouteFailureKind = RouteCandidatePolicy.RouteFailureKind.NoAcceptedRoute;
            _destination = null;
            _zoneEvaluation = null;
            _nativeProofPending = false;
            _boundaryGraceSince = 0f;
            _zoneWaypointIndex = -1;
            ZoneWaypoints.Clear();
            if (_leader == null || _expeditionDestination == null) return false;

            List<Zoneline> liveCrossings = ExpeditionDestinationResolver.GetCrossings(_expeditionDestination.CanonicalName, false);
            if (liveCrossings.Count == 0) return false;
            LocalZoneRoutePlanner.Plan plan = LocalZoneRoutePlanner.Build(_leader.transform.position, liveCrossings);
            ZoneRouteOptions.AddRange(plan.Options);
            // plan.Options carries only options that passed RouteCandidatePolicy acceptance, so a non-empty
            // list is the authoritative "a verified crossing approach existed" signal for this leg.
            _legHadAcceptedCandidate = ZoneRouteOptions.Count > 0;
            Verbose("built " + ZoneRouteOptions.Count + " accepted approach candidate(s) across " + liveCrossings.Count +
                " crossing(s) for " + _expeditionDestination.CanonicalName);
            return SelectZoneOption(0);
        }

        private static bool SelectZoneOption(int index)
        {
            while (index >= 0 && index < ZoneRouteOptions.Count)
            {
                LocalZoneRoutePlanner.RouteOption option = ZoneRouteOptions[index];
                if (option != null && option.Crossing != null && option.Crossing.gameObject != null &&
                    option.Crossing.gameObject.activeInHierarchy && !option.Crossing.RemoveParty)
                {
                    _zoneRouteIndex = index;
                    _destination = option.Crossing;
                    _zoneApproach = option.Approach;
                    _zoneEvaluation = option.Evaluation;
                    _lastRouteWasPartial = option.Evaluation != null &&
                        option.Evaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.PartialNearCrossing;
                    _routeProblemSince = 0f;
                    _lastLeaderProgressAt = Time.time;
                    _lastLeaderProgressPosition = _leader == null ? Vector3.zero : _leader.transform.position;
                    _nativeProofPending = false;
                    _boundaryGraceSince = 0f;
                    ZoneWaypoints.Clear();
                    if (option.PathCorners != null) ZoneWaypoints.AddRange(option.PathCorners);
                    _zoneWaypointIndex = FirstUsefulWaypoint(ZoneWaypoints,
                        _leader == null ? Vector3.zero : _leader.transform.position);
                    Verbose("selected route candidate " + option.StableKey + " => " + option.Evaluation.Acceptance +
                        " approach=" + LocalZoneRoutePlanner.FormatVector(option.Approach) +
                        " corners=" + ZoneWaypoints.Count);
                    return true;
                }
                index++;
            }
            return false;
        }

        private static bool TryNextZoneOption(string reason)
        {
            int next = _zoneRouteIndex + 1;
            while (SelectZoneOption(next))
            {
                Verbose("trying next verified approach after " + reason);
                if (ApplyCurrentZoneTravelOrder()) return true;
                next = _zoneRouteIndex + 1;
            }
            return false;
        }

        private static bool ApplyTravelOrder()
        {
            if (_leader == null || (_destination == null && _monster == null)) return false;
            if (_monster == null)
            {
                if (ApplyCurrentZoneTravelOrder()) return true;
                return TryNextZoneOption("native order could not be assigned");
            }

            // Monster/NPC lead retains the original stricter preflight. The relaxed boundary policy is
            // intentionally zone-only.
            Vector3 target = _monster.transform.position;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(target, out hit, 8f, NavMesh.AllAreas)) return false;
            if (!TryCalculateMeaningfulRoute(hit.position, true)) return false;
            try
            {
                _leader.AssignGuardSpot(hit.position);
                RefreshNativeNavigation(true, hit.position);
                FollowController.Start(_leader, _leaderName);
                return true;
            }
            catch { return false; }
        }

        private static bool ApplyCurrentZoneTravelOrder()
        {
            if (_leader == null || !CurrentCrossingUsable()) return false;
            try
            {
                Vector3 target = CurrentZoneTarget();
                _leader.AssignGuardSpot(target);
                RefreshNativeNavigation(true, target);
                FollowController.Start(_leader, _leaderName);
                _lastLeaderProgressPosition = _leader.transform.position;
                _lastLeaderProgressAt = Time.time;
                _routeProblemSince = 0f;
                _nextRouteValidationAt = 0f;
                _boundaryGraceSince = 0f;

                _nativeProofPending = _zoneEvaluation != null && _zoneEvaluation.NeedsNativeProof;
                if (_nativeProofPending)
                {
                    _nativeProofSince = Time.time;
                    _nativeProofStartPosition = _leader.transform.position;
                    _nativeProofStartDistance = LocalZoneRoutePlanner.DistanceToCrossing(_nativeProofStartPosition, _destination);
                    Verbose("allowing bounded native-navigation proof for candidate " + ZoneRouteOptions[_zoneRouteIndex].StableKey);
                }
                return true;
            }
            catch { return false; }
        }

        private static bool HandleNativeProof()
        {
            if (!_nativeProofPending || _leader == null || _destination == null) return false;
            Vector3 now = _leader.transform.position;
            float moved = HorizontalDistance(now, _nativeProofStartPosition);
            float remaining = LocalZoneRoutePlanner.DistanceToCrossing(now, _destination);
            if (moved >= NativeProofMoveDistance && _nativeProofStartDistance - remaining >= NativeProofCloserDistance)
            {
                _nativeProofPending = false;
                _lastLeaderProgressPosition = now;
                _lastLeaderProgressAt = Time.time;
                _routeProblemSince = 0f;
                Verbose("native-navigation proof accepted after moving " + moved.ToString("F2") + "m toward the crossing");
                return false;
            }
            if (Time.time - _nativeProofSince >= NativeProofSeconds)
            {
                _nativeProofPending = false;
                FailRoute("native navigation made no meaningful progress during the bounded proof window", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed);
                return true;
            }
            RefreshNativeNavigation(false, Vector3.zero);
            return true;
        }

        private static void AdvanceZoneWaypointIfReached()
        {
            if (_leader == null || ZoneWaypoints.Count == 0 || _zoneWaypointIndex < 0 ||
                _zoneWaypointIndex >= ZoneWaypoints.Count) return;
            bool advanced = false;
            while (_zoneWaypointIndex < ZoneWaypoints.Count &&
                HorizontalDistance(_leader.transform.position, ZoneWaypoints[_zoneWaypointIndex]) <= WaypointReachedDistance)
            {
                _zoneWaypointIndex++;
                advanced = true;
            }
            if (!advanced) return;
            Vector3 target = CurrentZoneTarget();
            try
            {
                _leader.AssignGuardSpot(target);
                RefreshNativeNavigation(true, target);
                _lastLeaderProgressPosition = _leader.transform.position;
                _lastLeaderProgressAt = Time.time;
                Verbose(_zoneWaypointIndex < ZoneWaypoints.Count
                    ? "advanced to NavMesh corner " + (_zoneWaypointIndex + 1) + "/" + ZoneWaypoints.Count +
                        " at " + LocalZoneRoutePlanner.FormatVector(target)
                    : "completed NavMesh corners; continuing to crossing approach at " +
                        LocalZoneRoutePlanner.FormatVector(target));
            }
            catch { }
        }

        private static int FirstUsefulWaypoint(IList<Vector3> points, Vector3 start)
        {
            if (points == null || points.Count == 0) return -1;
            int index = 0;
            while (index < points.Count && HorizontalDistance(start, points[index]) <= WaypointReachedDistance) index++;
            return index;
        }

        private static Vector3 CurrentZoneTarget()
        {
            return _zoneWaypointIndex >= 0 && _zoneWaypointIndex < ZoneWaypoints.Count
                ? ZoneWaypoints[_zoneWaypointIndex] : _zoneApproach;
        }

        private static bool BoundaryGraceActive()
        {
            if (_destination == null || _zoneEvaluation == null || _zoneEvaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.Complete)
            {
                _boundaryGraceSince = 0f;
                return false;
            }
            float distance = LocalZoneRoutePlanner.DistanceToCrossing(_leader.transform.position, _destination);
            if (distance > BoundaryGraceDistance)
            {
                _boundaryGraceSince = 0f;
                return false;
            }
            if (_boundaryGraceSince <= 0f) _boundaryGraceSince = Time.time;
            if (Time.time - _boundaryGraceSince <= BoundaryGraceSeconds)
            {
                _lastLeaderProgressAt = Time.time;
                return true;
            }
            _boundaryGraceSince = 0f;
            FailRoute("the boundary approach did not produce a real zone transition", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed);
            return true;
        }

        private static void RefreshNativeNavigation(bool force, Vector3 forcedTarget)
        {
            if (_leader == null || (!force && (_destination == null && _monster == null))) return;
            if (!force && Time.time < _nextNativeNavRefresh) return;
            _nextNativeNavRefresh = Time.time + 0.4f;
            Vector3 target = force ? forcedTarget : (_monster == null ? CurrentZoneTarget() : _monster.transform.position);
            try
            {
                NPC npc = _leader.MyStats == null || _leader.MyStats.Myself == null ? null : _leader.MyStats.Myself.MyNPC;
                if (npc != null) npc.HighPriorityNavUpdate(target);
            }
            catch { }
        }

        private static bool InCombat(SimPlayer sim)
        {
            if (GameData.InCombat) return true;
            try { if (sim != null && sim.IsSimGroupInCombat()) return true; } catch { }
            NPC npc = sim == null || sim.MyStats == null || sim.MyStats.Myself == null ? null : sim.MyStats.Myself.MyNPC;
            return npc != null && npc.CurrentAggroTarget != null;
        }

        private static void StartMonster(SimPlayer leader, NPC monster)
        {
            Stop(null);
            if (!FollowController.IsUsableSim(leader) || !IsGroupedWithPlayer(leader) || !IsUsableMonster(monster) ||
                !IsMonsterNearby(monster, MaximumNearbyMonsterDistance))
            {
                Say("[Erenshor Lead] The leader and monster must both be available in the current zone.", "yellow");
                return;
            }
            if (InCombat(leader))
            {
                Say("[Erenshor Lead] Finish combat before starting a hunt.", "yellow");
                return;
            }
            _leader = leader;
            _destination = null;
            _expeditionDestination = null;
            _monster = monster;
            _monsterName = ReadMonsterName(monster);
            _leaderName = FollowController.ReadName(leader);
            _destinationName = null;
            InitializeCommon(leader, false);
            if (!ApplyTravelOrder())
            {
                Stop("I can't find a walkable route to " + _monsterName + ".");
                return;
            }
            Say(_leaderName + " tells the group: I know where a " + _monsterName + " is. Follow me!", "lightblue");
        }

        private static NPC FindMonster(string requested, out bool ambiguous)
        {
            ambiguous = false;
            if (string.IsNullOrWhiteSpace(requested)) return null;
            string query = requested.Trim();
            NPC best = null;
            string bestName = null;
            float bestDistance = float.MaxValue;
            foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (!IsUsableMonster(npc) || !IsMonsterNearby(npc, MaximumNearbyMonsterDistance)) continue;
                string name = ReadMonsterName(npc);
                if (name.Length == 0 || name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool exact = name.Equals(query, StringComparison.OrdinalIgnoreCase);
                if (bestName != null && !name.Equals(bestName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!exact && !bestName.Equals(query, StringComparison.OrdinalIgnoreCase)) ambiguous = true;
                    else if (exact) { ambiguous = false; best = null; bestDistance = float.MaxValue; }
                    else continue;
                }
                if (ambiguous) continue;
                float distance = Vector3.Distance(GameData.PlayerControl.transform.position, npc.transform.position);
                if (best == null || distance < bestDistance)
                {
                    best = npc;
                    bestName = name;
                    bestDistance = distance;
                }
            }
            return ambiguous ? null : best;
        }

        private static bool IsUsableMonster(NPC npc)
        {
            if (npc == null || npc.gameObject == null || !npc.gameObject.activeInHierarchy || npc.SimPlayer ||
                npc.NeverAggro || npc.MiningNode || npc.TreasureChest || npc.SummonedByPlayer) return false;
            Character character = npc.GetComponent<Character>();
            if (character == null || character.Master != null || !character.Alive || character.Invulnerable || character.isVendor) return false;
            string identity = ReadMonsterName(npc) + " " + npc.gameObject.name;
            return identity.IndexOf("pet", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsMonsterNearby(NPC npc, float maximumDistance)
        {
            return IsMonsterNearby(npc, maximumDistance, IsUsableMonster(npc));
        }

        private static bool IsMonsterNearby(NPC npc, float maximumDistance, bool usable)
        {
            if (!usable || GameData.PlayerControl == null) return false;
            return Vector3.Distance(GameData.PlayerControl.transform.position, npc.transform.position) <= maximumDistance;
        }

        private static float PlayerGap()
        {
            if (_leader == null || GameData.PlayerControl == null) return 0f;
            Vector3 player = GameData.PlayerControl.transform.position;
            Vector3 leader = _leader.transform.position;
            player.y = 0f;
            leader.y = 0f;
            return Vector3.Distance(player, leader);
        }

        private static string ReadMonsterName(NPC npc)
        {
            if (npc == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(npc.NPCName)) return npc.NPCName.Trim();
            return npc.gameObject == null ? string.Empty : npc.gameObject.name;
        }

        private static string DescribeShortChoices()
        {
            List<string> choices = new List<string>();
            List<string> zones = ExpeditionDestinationResolver.ListCanonicalNames();
            for (int i = 0; i < zones.Count && choices.Count < 2; i++) choices.Add(zones[i]);
            foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (choices.Count >= 3) break;
                if (!IsUsableMonster(npc) || !IsMonsterNearby(npc, MaximumNearbyMonsterDistance)) continue;
                string name = ReadMonsterName(npc);
                if (name.Length > 0 && !choices.Exists(delegate(string x) { return x.Equals(name, StringComparison.OrdinalIgnoreCase); })) choices.Add(name);
            }
            if (choices.Count == 0) return "I don't know any safe destinations nearby.";
            return "I can lead you to " + string.Join(", ", choices.ToArray()) + ".";
        }

        private static bool IsGroupedWithPlayer(SimPlayer sim)
        {
            try
            {
                return sim != null && sim.InGroup && GameData.SimPlayerGrouping != null && GameData.SimPlayerGrouping.IsSimInPlayerGroup(sim);
            }
            catch { return false; }
        }

        private static bool SceneMatchesDestination(string scene)
        {
            return !string.IsNullOrWhiteSpace(scene) && !string.IsNullOrWhiteSpace(_destinationName) &&
                   string.Equals(scene.Trim(), _destinationName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidateLeaderRoute()
        {
            if (_leader == null) return false;
            Vector3 leaderPosition = _leader.transform.position;
            if (HorizontalDistance(leaderPosition, _lastLeaderProgressPosition) > 0.25f)
            {
                _lastLeaderProgressPosition = leaderPosition;
                _lastLeaderProgressAt = Time.time;
                _routeProblemSince = 0f;
            }
            if (_lastLeaderProgressAt > 0f && Time.time - _lastLeaderProgressAt >= NoProgressFailureSeconds) return false;
            if (Time.time < _nextRouteValidationAt) return true;
            _nextRouteValidationAt = Time.time + 0.4f;

            if (_monster != null)
            {
                NavMeshHit targetHit;
                if (!NavMesh.SamplePosition(_monster.transform.position, out targetHit, 8f, NavMesh.AllAreas))
                {
                    if (_routeProblemSince <= 0f) _routeProblemSince = Time.time;
                    return Time.time - _routeProblemSince < RouteRetrySeconds;
                }
                return TryCalculateMeaningfulRoute(targetHit.position, false);
            }
            return TryCalculateMeaningfulZoneRoute();
        }

        private static bool TryCalculateMeaningfulZoneRoute()
        {
            if (_leader == null || _destination == null) return false;
            NavMeshHit startHit;
            RouteCandidatePolicy.Candidate candidate = new RouteCandidatePolicy.Candidate();
            candidate.StableKey = _zoneRouteIndex >= 0 && _zoneRouteIndex < ZoneRouteOptions.Count
                ? ZoneRouteOptions[_zoneRouteIndex].StableKey : "runtime";
            candidate.Active = CurrentCrossingUsable();
            candidate.RemoveParty = _destination.RemoveParty;
            candidate.Sampled = true;
            candidate.ApproachDistanceToCrossing = LocalZoneRoutePlanner.DistanceToCrossing(_zoneApproach, _destination);
            candidate.StartDistanceToCrossing = LocalZoneRoutePlanner.DistanceToCrossing(_leader.transform.position, _destination);
            candidate.EndpointDistanceToCrossing = candidate.StartDistanceToCrossing;
            candidate.RouteLength = float.MaxValue;
            candidate.Path = RouteCandidatePolicy.PathKind.Invalid;

            if (NavMesh.SamplePosition(_leader.transform.position, out startHit, 5f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(startHit.position, _zoneApproach, NavMesh.AllAreas, RouteCheckPath) &&
                RouteCheckPath.status != NavMeshPathStatus.PathInvalid && RouteCheckPath.corners != null && RouteCheckPath.corners.Length >= 2)
            {
                candidate.CornerCount = RouteCheckPath.corners.Length;
                candidate.Path = RouteCheckPath.status == NavMeshPathStatus.PathComplete
                    ? RouteCandidatePolicy.PathKind.Complete : RouteCandidatePolicy.PathKind.Partial;
                candidate.EndpointDistanceToCrossing = LocalZoneRoutePlanner.DistanceToCrossing(
                    RouteCheckPath.corners[RouteCheckPath.corners.Length - 1], _destination);
                candidate.RouteLength = PathLength(RouteCheckPath.corners);
            }

            RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
            if (evaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.Complete ||
                evaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.PartialNearCrossing)
            {
                _zoneEvaluation = evaluation;
                _lastRouteWasPartial = evaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.PartialNearCrossing;
                _routeProblemSince = 0f;
                if (_lastLeaderProgressAt <= 0f) _lastLeaderProgressAt = Time.time;
                return true;
            }

            if (_routeProblemSince <= 0f) _routeProblemSince = Time.time;
            return Time.time - _routeProblemSince < RouteRetrySeconds;
        }

        // Original strict route validation retained for monster/NPC lead.
        private static bool TryCalculateMeaningfulRoute(Vector3 target, bool initialize)
        {
            if (_leader == null) return false;
            NavMeshHit startHit;
            if (!NavMesh.SamplePosition(_leader.transform.position, out startHit, 5f, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(startHit.position, target, NavMesh.AllAreas, RouteCheckPath) ||
                RouteCheckPath.status == NavMeshPathStatus.PathInvalid || RouteCheckPath.corners == null || RouteCheckPath.corners.Length < 2)
            {
                if (initialize) return false;
                if (_routeProblemSince <= 0f) _routeProblemSince = Time.time;
                return Time.time - _routeProblemSince < RouteRetrySeconds;
            }

            Vector3 leaderPosition = _leader.transform.position;
            bool moved = initialize || Vector3.Distance(leaderPosition, _lastLeaderProgressPosition) > 0.25f;
            if (moved)
            {
                _lastLeaderProgressPosition = leaderPosition;
                _lastLeaderProgressAt = Time.time;
                _routeProblemSince = 0f;
            }

            if (RouteCheckPath.status == NavMeshPathStatus.PathComplete)
            {
                _lastRouteWasPartial = false;
                _routeProblemSince = 0f;
            }
            else
            {
                Vector3 endpoint = RouteCheckPath.corners[RouteCheckPath.corners.Length - 1];
                float startRemaining = Vector3.Distance(startHit.position, target);
                float endpointRemaining = Vector3.Distance(endpoint, target);
                if (endpointRemaining >= startRemaining - 1f)
                {
                    if (initialize) return false;
                    if (_routeProblemSince <= 0f) _routeProblemSince = Time.time;
                }
                else
                {
                    bool changed = !_lastRouteWasPartial || Vector3.Distance(endpoint, _lastPartialEndpoint) > 0.75f;
                    if (changed || moved) _routeProblemSince = 0f;
                    else if (_routeProblemSince <= 0f) _routeProblemSince = Time.time;
                    _lastPartialEndpoint = endpoint;
                    _lastRouteWasPartial = true;
                }
            }

            if (_lastLeaderProgressAt <= 0f) _lastLeaderProgressAt = Time.time;
            if (Time.time - _lastLeaderProgressAt >= NoProgressFailureSeconds) return false;
            return _routeProblemSince <= 0f || Time.time - _routeProblemSince < RouteRetrySeconds;
        }

        private static bool CurrentCrossingUsable()
        {
            return _destination != null && _destination.gameObject != null && _destination.gameObject.activeInHierarchy &&
                   !_destination.RemoveParty && !string.IsNullOrWhiteSpace(_destination.DestinationZone) &&
                   _destination.DestinationZone.Trim().Equals(_destinationName, StringComparison.OrdinalIgnoreCase);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float PathLength(Vector3[] corners)
        {
            if (corners == null || corners.Length < 2) return 0f;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += HorizontalDistance(corners[i - 1], corners[i]);
            return length;
        }

        private static void Verbose(string message)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogDebug("Route: " + message); } catch { }
        }

        private static void ClearWithoutRestore()
        {
            _active = false;
            _pausedForCombat = false;
            _waitingForPlayer = false;
            _waitingSince = 0f;
            _nextNativeNavRefresh = 0f;
            _combatClearSince = 0f;
            _leader = null;
            _destination = null;
            _expeditionDestination = null;
            _monster = null;
            _monsterName = null;
            _leaderName = null;
            _destinationName = null;
            _startingScene = null;
            _originalGuardSpot = false;
            _originalGuardPosition = Vector3.zero;
            _lastLeaderProgressPosition = Vector3.zero;
            _lastPartialEndpoint = Vector3.zero;
            _lastRouteWasPartial = false;
            _lastLeaderProgressAt = 0f;
            _routeProblemSince = 0f;
            _nextRouteValidationAt = 0f;
            _expeditionOwned = false;
            _expeditionHeld = false;
            _regroupAfterCombat = false;
            _regroupStableSince = 0f;
            _zoneRouteIndex = -1;
            _zoneApproach = Vector3.zero;
            _zoneEvaluation = null;
            _nativeProofPending = false;
            _nativeProofSince = 0f;
            _nativeProofStartPosition = Vector3.zero;
            _nativeProofStartDistance = 0f;
            _boundaryGraceSince = 0f;
            _zoneWaypointIndex = -1;
            ZoneRouteOptions.Clear();
            ZoneWaypoints.Clear();
            PendingEvents.Clear();
        }

        private static void Say(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
        }

        private static void LegSay(string message, string color)
        {
            if (_expeditionOwned) return;
            Say(message, color);
        }
    }
}
