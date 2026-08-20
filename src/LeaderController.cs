using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    internal static class LeaderController
    {
        internal enum TravelState { Idle, StartingMovement, Moving, PausedForCombat, ResumingAfterCombat, WaitingForPlayer, Regrouping, PartialRouteRetry, NoProgress, Held }

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

        internal struct ZoneRouteReadinessSnapshot
        {
            internal int LiveCrossingCount;
            internal bool StartSampled;
            internal int AcceptedCandidateCount;
            internal string Detail;
        }

        private static readonly NavMeshPath RouteCheckPath = new NavMeshPath();
        private static readonly List<LegEvent> PendingEvents = new List<LegEvent>();
        private static readonly List<LocalZoneRoutePlanner.RouteOption> ZoneRouteOptions = new List<LocalZoneRoutePlanner.RouteOption>();
        private static readonly List<Vector3> ZoneWaypoints = new List<Vector3>();
        private static readonly List<LocalZoneRoutePlanner.CrossingTraversalOption> CrossingTraversalOptions =
            new List<LocalZoneRoutePlanner.CrossingTraversalOption>();
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
        private static bool _movementProofPending;
        private static float _movementProofSince;
        private static Vector3 _movementProofStartPosition;
        private static float _movementProofStartTargetDistance;
        private static Vector3 _movementProofTarget;
        private static int _movementOrderReissues;
        private static string _lastMovementDiagnostic = "not started";
        // Verbose zero-candidate records can be several kilobytes once per-seed geometry is included.
        // Route readiness may retry the same failed crossing repeatedly, so emitting the exact same
        // forensic blob every retry creates avoidable string/IO pressure. Keep one detailed record per
        // scene+destination at most every 10s; the short "built 0 accepted" line still reports every
        // rebuild while Verbose is enabled.
        private static string _lastZeroCandidateDetailKey = string.Empty;
        private static float _lastZeroCandidateDetailAt = -999f;
        private const float ZeroCandidateDetailRepeatSeconds = 10f;
        private static int _zoneWaypointIndex;
        private static int _crossingTraversalIndex;
        private static bool _crossingAttemptActive;
        private static float _crossingAttemptSince;
        private static int _crossingAttemptCount;
        private static Vector3 _crossingAttemptTarget;
        private static bool _crossingAttemptTargetValid;
        private static bool _leaderCrossingTriggerEntered;
        private static float _leaderCrossingTriggerAt;
        private static bool _playerCrossingTriggerEntered;
        private static float _playerCrossingTriggerAt;
        private static bool _approachTelemetryEmitted;
        private static string _lastCrossingDiagnostic = "not started";
        private static bool _routeResampleAttempted;
        private static bool _travelMovementOwned;
        private static int _movementOwnerGeneration;
        private static int _currentOrderGeneration;
        private static string _lastMovementWriter = "none";
        private static int _lastMovementWriterGeneration;
        private static float _lastMovementWriterAt;
        private static float _capturedAgentSpeed;
        private static bool _capturedAgentSpeedValid;
        private static float _lastOwnedAgentSpeed;
        private static bool _setAgentSpeed;
        private static Animator _ownedAnimator;
        private static bool _capturedWalking;
        private static bool _capturedPatrol;
        private static bool _capturedAnimatorState;
        private static bool _lastOwnedWalking;
        private static bool _lastOwnedPatrol;
        private static bool _setAnimatorState;
        private static Vector3 _locomotionSamplePosition;
        private static float _locomotionSampleAt;

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
        private const float WaypointReachedDistance = 1.35f;

        internal static bool ExpeditionOwned { get { return _expeditionOwned; } }
        internal static bool LegActive { get { return _active; } }
        internal static bool TravelMovementOwned { get { return _travelMovementOwned; } }

        internal static void StartSmart(SimPlayer leader, string requestedDestination)
        {
            if (!FollowController.IsUsableSim(leader) || !IsGroupedWithPlayer(leader))
            {
                Say("[Erenshor Lead] The leader must be a living Sim in your current party.", "yellow");
                return;
            }
            if (CoopCompatibility.IsRemoteHuman(leader))
            {
                Say("[Erenshor Lead] Remote COOP players cannot own Follow/Expedition automation.", "yellow");
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

        internal static bool StartExpeditionLeg(SimPlayer leader, ExpeditionDestination destination,
            out string failure, out ExpeditionStartOutcome outcome)
        {
            failure = null;
            outcome = ExpeditionStartOutcome.Rejected;
            Stop(null);
            if (!FollowController.IsUsableSim(leader) || !IsGroupedWithPlayer(leader))
            {
                failure = "The leader must be a living Sim in your current party.";
                outcome = ExpeditionStartOutcome.InvalidLeader;
                return false;
            }
            if (CoopCompatibility.IsRemoteHuman(leader))
            {
                failure = "That leader is controlled by another client.";
                outcome = ExpeditionStartOutcome.InvalidLeader;
                return false;
            }
            if (destination == null || destination.CrossingCount == 0)
            {
                failure = "That destination is no longer available.";
                outcome = ExpeditionStartOutcome.NoRoute;
                return false;
            }
            if (InCombat(leader))
            {
                failure = "Finish combat before starting an expedition.";
                outcome = ExpeditionStartOutcome.NotReady;
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
                string startDetail = !_legHadAcceptedCandidate || string.IsNullOrWhiteSpace(_lastMovementDiagnostic) ||
                    _lastMovementDiagnostic == "not started" || _lastMovementDiagnostic == "rebuilding live route candidates"
                    ? "no verified approach could be ordered" : _lastMovementDiagnostic;
                Stop(null);
                failure = SentenceCase(RouteCandidatePolicy.DescribeRouteFailure(
                    destination.CanonicalName, startKind, startDetail));
                outcome = ExpeditionStartOutcome.NoRoute;
                return false;
            }
            outcome = ExpeditionStartOutcome.Accepted;
            return true;
        }

        // Used only while a post-zone leg is still in its readiness phase.  It asks the resolver and
        // planner for a new scene-local snapshot and deliberately does not mutate movement ownership.
        internal static ZoneRouteReadinessSnapshot InspectZoneRouteReadiness(SimPlayer leader,
            ExpeditionDestination destination)
        {
            ZoneRouteReadinessSnapshot snapshot = new ZoneRouteReadinessSnapshot();
            if (leader == null || destination == null || string.IsNullOrWhiteSpace(destination.CanonicalName))
            {
                snapshot.Detail = "leader or expected destination missing";
                return snapshot;
            }
            List<Zoneline> crossings = ExpeditionDestinationResolver.GetCrossings(destination.CanonicalName, false);
            snapshot.LiveCrossingCount = crossings.Count;
            if (crossings.Count == 0)
            {
                snapshot.Detail = "liveCrossings=0";
                return snapshot;
            }
            LocalZoneRoutePlanner.Plan plan = LocalZoneRoutePlanner.Build(leader.transform.position, crossings);
            snapshot.StartSampled = plan.StartSampled;
            snapshot.AcceptedCandidateCount = plan.Options.Count;
            snapshot.Detail = LocalZoneRoutePlanner.DescribeReadiness(plan);
            return snapshot;
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
            ResetMovementProof("not started");
            ResetCrossingState("not started");
            _routeResampleAttempted = false;
            _lastLeaderProgressAt = 0f;
            _routeProblemSince = 0f;
            _nextRouteValidationAt = 0f;
            _travelMovementOwned = false;
            _movementOwnerGeneration = ExpeditionMovementOwnershipPolicy.NextGeneration(_movementOwnerGeneration);
            _currentOrderGeneration = 0;
            _lastMovementWriter = "Leg.Initialize";
            _lastMovementWriterGeneration = _movementOwnerGeneration;
            _lastMovementWriterAt = Time.time;
            ResetOwnedMovementAdapter();
            ExpeditionMovementTelemetry.Reset();
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

            if (_expeditionOwned && HandleObservedCrossingWait()) return;

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
                NoteMovementBoundary("combat_yield.before");
                ReleaseTravelMovementOwnership("combat_yield", false);
                _pausedForCombat = true;
                _combatClearSince = 0f;
                _waitingForPlayer = false;
                _waitingSince = 0f;
                _regroupAfterCombat = false;
                _regroupStableSince = 0f;
                _nativeProofPending = false;
                ResetMovementProof("yielded to native combat");
                ResetCrossingState("yielded to native combat");
                try { _leader.FreeFollow(); } catch { }
                FollowController.Stop();
                NoteMovementWriter("NativeCombatYield");
                NoteMovementBoundary("combat_yield.after");
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
                NoteMovementBoundary(afterCombat ? "combat_resume.after" : "regroup_resume.after");
                LegSay(afterCombat
                    ? "[Erenshor Lead] Combat is clear. " + _leaderName + " is resuming travel."
                    : _leaderName + " tells the group: Ready? Let's keep moving.", "lightblue");
                return;
            }

            if (_monster == null && _movementProofPending)
            {
                if (HandleMovementAcquisition()) return;
            }

            if (_monster == null && _nativeProofPending)
            {
                if (HandleNativeProof()) return;
            }

            if (_monster == null) AdvanceZoneWaypointIfReached();

            if (_monster == null && HandleCrossingAttempt()) return;

            RefreshNativeNavigation(false, Vector3.zero);
            SyncOwnedTravelMovement();
            TickMovementTelemetry();
            NavMeshAgent nav = ResolveLeaderAgent();
            if (nav != null && nav.isOnNavMesh && nav.hasPath && nav.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                FailRoute("the native NavMeshAgent reported PathInvalid", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed);
                return;
            }
            if (_monster == null && LeaderNoProgressExpired())
            {
                float crossingDistance = _destination == null ? float.MaxValue :
                    LocalZoneRoutePlanner.DistanceToCrossing(_leader.transform.position, _destination);
                if (crossingDistance <= ExpeditionCrossingPolicy.StalledNearTriggerDistance)
                    FailRoute("leader reached the boundary area but no safe trigger-crossing traversal could be issued; true trigger distance=" +
                        crossingDistance.ToString("F1") + "m; " + DescribeNativeMovementState(CurrentZoneTarget()),
                        RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed);
                else
                    FailMovementOwnership("route validation saw no movement; " + DescribeNativeMovementState(CurrentZoneTarget()));
                return;
            }
            if (!ValidateLeaderRoute())
            {
                FailRoute("live route geometry no longer validates", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed);
                return;
            }
        }

        private static bool BeginRegroupHold(bool afterCombat)
        {
            if (_leader == null) return false;
            NoteMovementBoundary(afterCombat ? "combat_regroup.before" : "regroup_hold.before");
            ReleaseTravelMovementOwnership(afterCombat ? "combat_regroup" : "regroup_hold", true);
            try { _leader.AssignGuardSpot(_leader.transform.position); } catch { return false; }
            NoteMovementWriter(afterCombat ? "Native.RegroupAfterCombat" : "Native.RegroupHold");
            FollowController.Start(_leader, _leaderName);
            _waitingForPlayer = true;
            _waitingSince = Time.time;
            _regroupAfterCombat = afterCombat;
            _regroupStableSince = 0f;
            NoteMovementBoundary(afterCombat ? "combat_regroup.after" : "regroup_hold.after");
            return true;
        }

        // siteKind is supplied by the failure site, never inferred from the reason text. Callers that are not
        // specifically about reaching the selected crossing must pass TravelExecutionFailed.
        private static void FailRoute(string reason, RouteCandidatePolicy.RouteFailureKind siteKind)
        {
            if (_expeditionOwned && _monster == null)
            {
                if (TryNextZoneOption(reason)) return;

                // One event-boundary re-sample is allowed after every pre-built geometry candidate has
                // failed. This is not an endless route search: it rebuilds the same live Zoneline set once
                // from the leader's CURRENT position, then either issues one newly verified order or ends
                // the leg. Movement-ownership failures deliberately bypass this method and never use the
                // geometry re-sample as a substitute for a controller that is not executing orders.
                if (!_routeResampleAttempted && siteKind != RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed)
                {
                    _routeResampleAttempted = true;
                    bool everAccepted = _legHadAcceptedCandidate;
                    ExpeditionPhaseTelemetry.Record("route_resample",
                        "all pre-built candidates exhausted; rebuilding once from current leader position");
                    Verbose("all pre-built route candidates failed; performing one bounded live re-sample");
                    bool rebuilt = RebuildZoneOptions();
                    _legHadAcceptedCandidate = _legHadAcceptedCandidate || everAccepted;
                    if (rebuilt && ApplyCurrentZoneTravelOrder()) return;
                    reason = string.IsNullOrWhiteSpace(reason)
                        ? "bounded live route re-sample did not produce an executable crossing order"
                        : reason + "; bounded live route re-sample did not produce an executable crossing order";
                }
            }
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

        internal static void HoldForExpedition(bool preserveNativeGroupOrder = false)
        {
            if (!_active || !_expeditionOwned) return;
            NoteMovementBoundary(preserveNativeGroupOrder ? "native_group_order.before" : "expedition_hold.before");
            ReleaseTravelMovementOwnership(preserveNativeGroupOrder ? "native_group_order" : "expedition_hold",
                !preserveNativeGroupOrder);
            _expeditionHeld = true;
            _pausedForCombat = false;
            _waitingForPlayer = false;
            _waitingSince = 0f;
            _regroupAfterCombat = false;
            _regroupStableSince = 0f;
            _combatClearSince = 0f;
            _nativeProofPending = false;
            ResetMovementProof("expedition held");
            ResetCrossingState("expedition held");
            FollowController.Stop();
            if (!preserveNativeGroupOrder)
            {
                try
                {
                    if (_leader != null) _leader.AssignGuardSpot(_leader.transform.position);
                    NoteMovementWriter("Native.ExpeditionHold");
                }
                catch { }
            }
            PendingEvents.Clear();
            NoteMovementBoundary(preserveNativeGroupOrder ? "native_group_order.after" : "expedition_hold.after");
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
            NoteMovementBoundary("explicit_resume.before");
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
                    !_legHadAcceptedCandidate || string.IsNullOrWhiteSpace(_lastMovementDiagnostic) ||
                        _lastMovementDiagnostic == "rebuilding live route candidates"
                        ? "no verified approach could be ordered" : _lastMovementDiagnostic));
                return false;
            }
            NoteMovementBoundary("explicit_resume.after");
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
            if (_active) NoteMovementBoundary("cleanup.before");
            ReleaseTravelMovementOwnership("cleanup", true);
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
            if (wasExpedition) ExpeditionPhaseTelemetry.Record("movement_cleanup", "expedition movement ownership released");
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
                else if (_movementProofPending)
                    state = TravelState.StartingMovement;
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
            ResetMovementProof("rebuilding live route candidates");
            ResetCrossingState("rebuilding live route candidates");
            _zoneWaypointIndex = -1;
            ZoneWaypoints.Clear();
            if (_leader == null || _expeditionDestination == null) return false;

            List<Zoneline> liveCrossings = ExpeditionDestinationResolver.GetCrossings(_expeditionDestination.CanonicalName, false);
            ExpeditionPhaseTelemetry.Record("target_zone", "destination=" + SafeName(_expeditionDestination.CanonicalName) +
                " liveCrossings=" + liveCrossings.Count);
            if (liveCrossings.Count == 0) return false;
            LocalZoneRoutePlanner.Plan plan = LocalZoneRoutePlanner.Build(_leader.transform.position, liveCrossings);
            ZoneRouteOptions.AddRange(plan.Options);
            // plan.Options carries only options that passed RouteCandidatePolicy acceptance, so a non-empty
            // list is the authoritative "a verified crossing approach existed" signal for this leg.
            _legHadAcceptedCandidate = ZoneRouteOptions.Count > 0;
            Verbose("built " + ZoneRouteOptions.Count + " accepted approach candidate(s) across " + liveCrossings.Count +
                " crossing(s) for " + _expeditionDestination.CanonicalName);
            // Bounded diagnostic for the exact "0 accepted" failure this summary line cannot explain on
            // its own: one extra line, only on the failure branch, describing every sampled/rejected
            // candidate the planner actually measured for this destination. See LocalZoneRoutePlanner.
            // DescribeReadiness / RouteCandidatePolicy.DescribeCandidate for the field set.
            if (!_legHadAcceptedCandidate && ErenshorFollowPlugin.VerboseDiagnostics)
            {
                string scene = SceneManager.GetActiveScene().name ?? string.Empty;
                string destination = _expeditionDestination.CanonicalName ?? string.Empty;
                string detailKey = scene + "|" + destination;
                float now = Time.unscaledTime;
                if (!string.Equals(detailKey, _lastZeroCandidateDetailKey, StringComparison.Ordinal) ||
                    now < _lastZeroCandidateDetailAt || now - _lastZeroCandidateDetailAt >= ZeroCandidateDetailRepeatSeconds)
                {
                    _lastZeroCandidateDetailKey = detailKey;
                    _lastZeroCandidateDetailAt = now;
                    Verbose("zero-candidate detail zone=" + scene +
                        " destination=" + destination + " " + LocalZoneRoutePlanner.DescribeReadiness(plan));
                }
            }
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
                    ResetCrossingState("route candidate selected");
                    ZoneWaypoints.Clear();
                    if (option.PathCorners != null) ZoneWaypoints.AddRange(option.PathCorners);
                    _zoneWaypointIndex = FirstUsefulWaypoint(ZoneWaypoints,
                        _leader == null ? Vector3.zero : _leader.transform.position);
                    ExpeditionPhaseTelemetry.Record("exit_chosen", "destination=" + SafeName(_destinationName) +
                        " crossing=" + option.StableKey);
                    ExpeditionPhaseTelemetry.Record("route_candidate", option.Evaluation.Acceptance +
                        " approach=" + LocalZoneRoutePlanner.FormatVector(option.Approach) +
                        " corners=" + ZoneWaypoints.Count + " reason=" + option.Evaluation.Reason);
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
                // A failure to issue an order to the selected Sim is an ownership/state problem, not
                // evidence that another geometric approach point will work. Runtime PathInvalid may
                // still advance to another verified approach candidate later.
                return ApplyCurrentZoneTravelOrder();
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
            Vector3 target = CurrentZoneTarget();
            string orderFailure;
            if (!TryIssueZoneTravelOrder(target, true, true, "Follow.OrderIssue", out orderFailure))
            {
                _lastMovementDiagnostic = orderFailure;
                Verbose("native movement order rejected before travel: " + orderFailure);
                return false;
            }

            FollowController.Start(_leader, _leaderName);
            _lastLeaderProgressPosition = _leader.transform.position;
            _lastLeaderProgressAt = Time.time;
            _routeProblemSince = 0f;
            _nextRouteValidationAt = 0f;
            ResetCrossingState("approaching verified zoneline");
            BeginMovementProof(target);

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

        private static bool TryIssueZoneTravelOrder(Vector3 target, bool releasePriorState, bool ownTravelMovement,
            string writer, out string failure)
        {
            failure = null;
            LocalZoneRoutePlanner.RouteOption selected = _zoneRouteIndex >= 0 && _zoneRouteIndex < ZoneRouteOptions.Count
                ? ZoneRouteOptions[_zoneRouteIndex] : null;
            ExpeditionOrderProofPolicy.Record orderProof = ExpeditionOrderProofDiagnostic.Begin(_leader, target,
                selected, _destinationName, _travelMovementOwned);
            if (_leader == null)
            {
                failure = "selected Sim avatar is unavailable";
                ExpeditionOrderProofDiagnostic.Complete(orderProof, _travelMovementOwned, false, false,
                    "order_precondition", "leader_not_available");
                return false;
            }

            if (_expeditionOwned) TickMovementTelemetry();

            try
            {
                // Native group-follow recovery uses FreeFollow(). For an expedition we apply it only to
                // the exact selected Sim, never to the whole group, then immediately assign the mod-owned
                // guard destination. This is the strongest verified way to release a stale guard/rest
                // posture without mutating task/sitting fields that have not been proven safe.
                if (releasePriorState) _leader.FreeFollow();
                if (ownTravelMovement) AcquireTravelMovementOwnership();
                else ReleaseTravelMovementOwnership("crossing_handoff", false);
                _currentOrderGeneration = ExpeditionMovementOwnershipPolicy.NextGeneration(_currentOrderGeneration);
                NoteMovementBoundary((writer ?? "Follow.Order") + ".before");
                _leader.AssignGuardSpot(target);
                NoteMovementWriter((writer ?? "Follow.Order") + ".GuardSpot");
            }
            catch (Exception ex)
            {
                if (ownTravelMovement) ReleaseTravelMovementOwnership("order_issue_failed", true);
                failure = "selected Sim rejected the guard travel order (" + ex.GetType().Name + ")";
                ExpeditionOrderProofDiagnostic.Complete(orderProof, _travelMovementOwned, false, false,
                    "movement_ownership", "exception:" + ex.GetType().Name);
                return false;
            }

            NPC npc = ResolveLeaderNpc();
            if (npc == null)
            {
                if (ownTravelMovement) ReleaseTravelMovementOwnership("npc_owner_missing", true);
                failure = "selected Sim has no resolvable native NPC movement owner";
                ExpeditionOrderProofDiagnostic.Complete(orderProof, _travelMovementOwned, false, false,
                    "mover_owner", "npc_owner_missing");
                return false;
            }

            try
            {
                PrepareAgentForOrder(npc, target, ownTravelMovement);
                // Read-only evidence for the exact target that the next existing call hands to native movement.
                ExpeditionOrderProofDiagnostic.ProbeMover(orderProof, npc, target);
                npc.HighPriorityNavUpdate(target);
                NoteMovementWriter(writer ?? "Follow.Order");
                _nextNativeNavRefresh = Time.time + 0.4f;
                NoteMovementBoundary((writer ?? "Follow.Order") + ".after");
                TickMovementTelemetry();
                ExpeditionOrderProofDiagnostic.Complete(orderProof, _travelMovementOwned, true, true, null, null);
                return true;
            }
            catch (Exception ex)
            {
                if (ownTravelMovement) ReleaseTravelMovementOwnership("native_order_failed", true);
                failure = "native NPC movement order failed (" + ex.GetType().Name + ")";
                ExpeditionOrderProofDiagnostic.Complete(orderProof, _travelMovementOwned, true, false,
                    "native_order", "exception:" + ex.GetType().Name);
                return false;
            }
        }

        private static void BeginMovementProof(Vector3 target)
        {
            _movementProofPending = true;
            _movementProofSince = Time.time;
            _movementProofStartPosition = _leader == null ? Vector3.zero : _leader.transform.position;
            _movementProofTarget = target;
            _movementProofStartTargetDistance = HorizontalDistance(_movementProofStartPosition, target);
            _movementOrderReissues = 0;
            _lastMovementDiagnostic = "native movement order issued; awaiting visible progress";
        }

        private static void ResetMovementProof(string diagnostic)
        {
            _movementProofPending = false;
            _movementProofSince = 0f;
            _movementProofStartPosition = Vector3.zero;
            _movementProofStartTargetDistance = 0f;
            _movementProofTarget = Vector3.zero;
            _movementOrderReissues = 0;
            if (!string.IsNullOrWhiteSpace(diagnostic)) _lastMovementDiagnostic = diagnostic;
        }

        private static bool HandleMovementAcquisition()
        {
            if (!_movementProofPending || _leader == null) return false;

            ExpeditionMovementObservation observation = CaptureMovementObservation(_movementProofTarget);
            ExpeditionMovementIssue issue;
            ExpeditionMovementDecision decision = ExpeditionMovementPolicy.Evaluate(observation, out issue);
            if (decision == ExpeditionMovementDecision.Waiting) return true;

            if (decision == ExpeditionMovementDecision.ProgressObserved)
            {
                _movementProofPending = false;
                _lastLeaderProgressPosition = _leader.transform.position;
                _lastLeaderProgressAt = Time.time;
                _routeProblemSince = 0f;
                _lastMovementDiagnostic = "native movement confirmed";
                Verbose("native movement ownership confirmed: " + DescribeNativeMovementState(_movementProofTarget));
                return false;
            }

            // The ordered point was actually reached. Clear the movement proof and return false so
            // this tick CONTINUES into AdvanceZoneWaypointIfReached()/HandleCrossingAttempt() rather
            // than returning early. Returning true here is what previously made the crossing phase
            // structurally unreachable: the proof never cleared on arrival, so the tick short-
            // circuited above the crossing handoff on every subsequent frame until the reissue
            // budget ran out and the route failed.
            if (decision == ExpeditionMovementDecision.ArrivedAtTarget)
            {
                _movementProofPending = false;
                _lastLeaderProgressPosition = _leader.transform.position;
                _lastLeaderProgressAt = Time.time;
                _routeProblemSince = 0f;
                _lastMovementDiagnostic = ExpeditionMovementPolicy.Describe(issue);
                Verbose("native movement ownership arrival: " + _lastMovementDiagnostic + "; " +
                    DescribeNativeMovementState(_movementProofTarget));
                return false;
            }

            string description = ExpeditionMovementPolicy.Describe(issue);
            if (decision == ExpeditionMovementDecision.ReissueNativeOrder)
            {
                _movementOrderReissues++;
                string failure;
                if (!TryIssueZoneTravelOrder(_movementProofTarget, true, true, "Follow.Reissue", out failure))
                {
                    _movementProofPending = false;
                    FailMovementOwnership(description + "; reissue failed: " + failure);
                    return true;
                }
                _movementProofSince = Time.time;
                _movementProofStartPosition = _leader.transform.position;
                _movementProofStartTargetDistance = HorizontalDistance(_movementProofStartPosition, _movementProofTarget);
                _lastMovementDiagnostic = description + "; native order reissued " + _movementOrderReissues + "/" +
                    ExpeditionMovementPolicy.MaximumReissues;
                Verbose(_lastMovementDiagnostic + ": " + DescribeNativeMovementState(_movementProofTarget));
                return true;
            }

            _movementProofPending = false;
            if (decision == ExpeditionMovementDecision.TryNextRouteCandidate)
            {
                FailRoute(description, RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed);
                return true;
            }

            FailMovementOwnership(description + "; " + DescribeNativeMovementState(_movementProofTarget));
            return true;
        }

        private static ExpeditionMovementObservation CaptureMovementObservation(Vector3 target)
        {
            ExpeditionMovementObservation observation = new ExpeditionMovementObservation();
            observation.CombatControl = InCombat(_leader);
            NPC npc = ResolveLeaderNpc();
            observation.NpcResolved = npc != null;
            NavMeshAgent nav = ResolveLeaderAgent();
            observation.AgentPresent = nav != null;
            observation.ElapsedSeconds = Time.time - _movementProofSince;
            observation.ReissueCount = _movementOrderReissues;
            // Unknown distance must never read as "arrived" (0). Only a real measured leader
            // position below may lower this.
            observation.DistanceToTarget = float.MaxValue;

            if (_leader != null)
            {
                Vector3 now = _leader.transform.position;
                observation.MovedDistance = HorizontalDistance(now, _movementProofStartPosition);
                observation.DistanceImprovement = _movementProofStartTargetDistance - HorizontalDistance(now, target);
                // Remaining distance to the ordered point. Without this the policy cannot tell a
                // leader that ARRIVED from one that never moved; see ExpeditionMovementPolicy.
                observation.DistanceToTarget = HorizontalDistance(now, target);
            }

            if (nav != null)
            {
                try
                {
                    observation.AgentEnabled = nav.enabled;
                    observation.AgentOnNavMesh = nav.isOnNavMesh;
                    observation.AgentStopped = nav.isStopped;
                    observation.PathPending = nav.pathPending;
                    observation.HasPath = nav.hasPath;
                    observation.PathInvalid = nav.hasPath && nav.pathStatus == NavMeshPathStatus.PathInvalid;
                    observation.VelocityMagnitude = nav.velocity.magnitude;
                    if (nav.enabled && nav.isOnNavMesh)
                        observation.DestinationAccepted = HorizontalDistance(nav.destination, target) <= 2.5f;
                }
                catch { }
            }
            return observation;
        }

        private static bool LeaderNoProgressExpired()
        {
            return _lastLeaderProgressAt > 0f && Time.time - _lastLeaderProgressAt >= NoProgressFailureSeconds;
        }

        private static void FailMovementOwnership(string reason)
        {
            string routeTarget = _monster == null ? _destinationName : _monsterName;
            _lastMovementDiagnostic = string.IsNullOrWhiteSpace(reason) ? "native movement ownership failed" : reason;
            Verbose("movement ownership failed for " + routeTarget + ": " + _lastMovementDiagnostic);
            _legRouteFailureReason = _lastMovementDiagnostic;
            _legRouteFailureKind = RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed;
            if (_expeditionOwned) { Report(LegEvent.RouteFailed); return; }
            Stop(SentenceCase(RouteCandidatePolicy.DescribeRouteFailure(routeTarget,
                RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed, _lastMovementDiagnostic)));
        }

        internal static string MovementDiagnostics()
        {
            string target = _monster == null && _destination != null
                ? LocalZoneRoutePlanner.FormatVector(CurrentZoneTarget())
                : (_monster == null ? "none" : LocalZoneRoutePlanner.FormatVector(_monster.transform.position));
            return "[Erenshor Expedition] movement: active=" + _active +
                " leader=" + (_leader == null ? "none" : SafeName(_leaderName)) +
                " target=" + target +
                " proof=" + (_movementProofPending ? "pending" : "settled") +
                " reissues=" + _movementOrderReissues +
                " travelOwner=" + _travelMovementOwned +
                " ownerGen=" + _movementOwnerGeneration +
                " orderGen=" + _currentOrderGeneration +
                " lastWriter=" + SafeName(_lastMovementWriter) +
                " combat=" + InCombat(_leader) +
                " crossingAttempt=" + _crossingAttemptActive +
                " crossingCount=" + _crossingAttemptCount + "/" + ExpeditionCrossingPolicy.MaximumAttempts +
                " leaderTrigger=" + _leaderCrossingTriggerEntered +
                " playerTrigger=" + _playerCrossingTriggerEntered +
                " | " + DescribeNativeMovementState(_monster == null ? CurrentZoneTarget() : _monster.transform.position) +
                " | last=" + SafeName(_lastMovementDiagnostic) +
                " | crossing=" + SafeName(_lastCrossingDiagnostic);
        }

        internal static string DescribeSelectedRouteDiagnostic()
        {
            if (!_active || _destination == null || _zoneEvaluation == null || _zoneEvaluation.Candidate == null)
                return "selected=<none> reason=no active selected current-leg route";
            RouteCandidatePolicy.Candidate candidate = _zoneEvaluation.Candidate;
            string key = _zoneRouteIndex >= 0 && _zoneRouteIndex < ZoneRouteOptions.Count
                ? ZoneRouteOptions[_zoneRouteIndex].StableKey : "runtime";
            return "selected=" + key +
                " seed=" + (string.IsNullOrWhiteSpace(candidate.SeedLabel) ? "unknown" : candidate.SeedLabel) +
                " approach=" + LocalZoneRoutePlanner.FormatVector(_zoneApproach) +
                " qualityRef=" + (string.IsNullOrWhiteSpace(candidate.ApproachQualityReferencePosition) ? "<none>" : candidate.ApproachQualityReferencePosition) +
                " quality=" + (candidate.HasApproachQuality ? candidate.ApproachQualityDistance.ToString("F2") : "n/a") +
                " route=" + candidate.RouteLength.ToString("F2") +
                " reason=" + _zoneEvaluation.Reason;
        }

        internal static bool IsExactExpeditionLeader(SimPlayer sim)
        {
            return sim != null && _active && _expeditionOwned && _leader != null && object.ReferenceEquals(sim, _leader);
        }

        internal static bool ShouldSuppressNativeDoGuard(SimPlayer sim)
        {
            ExpeditionMovementOwnershipInputs input = new ExpeditionMovementOwnershipInputs();
            input.ExpeditionActive = _active && _expeditionOwned;
            input.ExactLeader = sim != null && _leader != null && object.ReferenceEquals(sim, _leader);
            input.TravelMovementOwned = _travelMovementOwned;
            input.Combat = InCombat(sim);
            input.ExplicitHold = _expeditionHeld;
            input.Regrouping = _waitingForPlayer || _regroupAfterCombat;
            input.Paused = _pausedForCombat;
            input.TerminalCleanup = !_active;
            input.CrossingHandoff = IsCrossingHandoffPhase();
            input.NativeZoning = GameData.Zoning;
            return ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(input);
        }

        internal static void NoteNativeDoGuardSuppressed()
        {
            NoteMovementWriter("Native.DoGuard.SUPPRESSED");
            NoteMovementBoundary("Native.DoGuard.suppressed");
        }

        internal static void NoteMovementBoundary(string boundary)
        {
            if (!_active || _leader == null || !_expeditionOwned) return;
            Vector3 order = Vector3.zero;
            bool orderValid = TryCurrentOrder(out order);
            ExpeditionMovementTelemetry.RecordBoundary(boundary, _leader, order, orderValid, MovementPhaseName(),
                _travelMovementOwned, InCombat(_leader), _expeditionHeld, _waitingForPlayer || _regroupAfterCombat,
                _pausedForCombat, IsCrossingHandoffPhase(), _movementOwnerGeneration, _currentOrderGeneration,
                _lastMovementWriter, _lastMovementWriterGeneration, _lastMovementWriterAt);
        }

        internal static void NoteMovementWriter(string writer)
        {
            _lastMovementWriter = string.IsNullOrWhiteSpace(writer) ? "unknown" : writer;
            _lastMovementWriterGeneration = _currentOrderGeneration;
            _lastMovementWriterAt = Time.time;
        }

        private static void TickMovementTelemetry()
        {
            if (!_active || !_expeditionOwned || _leader == null) return;
            Vector3 order;
            bool orderValid = TryCurrentOrder(out order);
            ExpeditionMovementTelemetry.Tick(_leader, order, orderValid, MovementPhaseName(), _travelMovementOwned,
                InCombat(_leader), _expeditionHeld, _waitingForPlayer || _regroupAfterCombat, _pausedForCombat,
                IsCrossingHandoffPhase(), _movementOwnerGeneration, _currentOrderGeneration, _lastMovementWriter,
                _lastMovementWriterGeneration, _lastMovementWriterAt);
        }

        private static bool TryCurrentOrder(out Vector3 order)
        {
            order = Vector3.zero;
            if (!_active || _leader == null) return false;
            if (_monster != null)
            {
                try { order = _monster.transform.position; return true; } catch { return false; }
            }
            if (_destination == null) return false;
            order = CurrentZoneTarget();
            return true;
        }

        private static string MovementPhaseName()
        {
            if (GameData.Zoning) return "native_zoning";
            if (_leaderCrossingTriggerEntered || _playerCrossingTriggerEntered) return "crossing_trigger_handoff";
            if (_crossingAttemptActive) return "crossing_attempt";
            if (_expeditionHeld) return "held";
            if (_pausedForCombat) return "combat_yield";
            if (_waitingForPlayer) return _regroupAfterCombat ? "post_combat_regroup" : "regroup";
            if (_movementProofPending) return "movement_acquisition";
            if (_zoneWaypointIndex >= ZoneWaypoints.Count && _destination != null) return "approach";
            return "corner_progression";
        }

        private static bool IsCrossingHandoffPhase()
        {
            return _crossingAttemptActive || _leaderCrossingTriggerEntered || _playerCrossingTriggerEntered || GameData.Zoning;
        }

        private static void AcquireTravelMovementOwnership()
        {
            if (!_expeditionOwned || _leader == null) return;
            if (_travelMovementOwned) return;
            _travelMovementOwned = true;
            _movementOwnerGeneration = ExpeditionMovementOwnershipPolicy.NextGeneration(_movementOwnerGeneration);
            _currentOrderGeneration = 0;
            CaptureOwnedMovementAdapterState();
            NoteMovementWriter("Follow.OwnershipAcquire");
        }

        private static void CaptureOwnedMovementAdapterState()
        {
            ResetOwnedMovementAdapter();
            NavMeshAgent nav = ResolveLeaderAgent();
            if (nav != null)
            {
                try
                {
                    _capturedAgentSpeed = nav.speed;
                    _capturedAgentSpeedValid = _capturedAgentSpeed >= ExpeditionMovementOwnershipPolicy.MinimumUsableSpeed &&
                        !float.IsNaN(_capturedAgentSpeed) && !float.IsInfinity(_capturedAgentSpeed);
                }
                catch { }
            }
            _ownedAnimator = ResolveLeaderAnimator();
            if (_ownedAnimator != null)
            {
                try
                {
                    _capturedWalking = _ownedAnimator.GetBool("Walking");
                    _capturedPatrol = _ownedAnimator.GetBool("Patrol");
                    _capturedAnimatorState = true;
                }
                catch { _capturedAnimatorState = false; }
            }
            if (_leader != null)
            {
                _locomotionSamplePosition = _leader.transform.position;
                _locomotionSampleAt = Time.time;
            }
        }

        private static void PrepareAgentForOrder(NPC npc, Vector3 target, bool ownTravelMovement)
        {
            // Crossing/native-zone handoff deliberately uses the existing HighPriorityNavUpdate call without
            // retaining Follow's speed/stop adapter. At that boundary vanilla movement owns the actor again.
            if (!ownTravelMovement) return;
            NavMeshAgent nav = null;
            try { nav = npc == null ? null : npc.GetComponent<NavMeshAgent>(); } catch { }
            if (nav == null) nav = ResolveLeaderAgent();
            if (nav == null) return;

            float nativeRunSpeed = 0f;
            try { if (_leader != null && _leader.MyStats != null) nativeRunSpeed = _leader.MyStats.actualRunSpeed; } catch { }
            float currentSpeed = 0f;
            try { currentSpeed = nav.speed; } catch { }
            float selected = ExpeditionMovementOwnershipPolicy.SelectTravelSpeed(nativeRunSpeed, currentSpeed,
                _capturedAgentSpeedValid ? _capturedAgentSpeed : 0f);
            try
            {
                if (selected >= ExpeditionMovementOwnershipPolicy.MinimumUsableSpeed)
                {
                    nav.speed = selected;
                    if (ownTravelMovement)
                    {
                        _lastOwnedAgentSpeed = selected;
                        _setAgentSpeed = true;
                    }
                }
                if (nav.enabled && nav.isOnNavMesh) nav.isStopped = false;
            }
            catch { }
        }

        private static void SyncOwnedTravelMovement()
        {
            if (!_travelMovementOwned || !ShouldSuppressNativeDoGuard(_leader) || _leader == null) return;
            NavMeshAgent nav = ResolveLeaderAgent();
            if (nav == null) return;

            float nativeRunSpeed = 0f;
            float currentSpeed = 0f;
            try { if (_leader.MyStats != null) nativeRunSpeed = _leader.MyStats.actualRunSpeed; } catch { }
            try { currentSpeed = nav.speed; } catch { }
            float selected = ExpeditionMovementOwnershipPolicy.SelectTravelSpeed(nativeRunSpeed, currentSpeed,
                _capturedAgentSpeedValid ? _capturedAgentSpeed : 0f);
            try
            {
                if (selected >= ExpeditionMovementOwnershipPolicy.MinimumUsableSpeed && Math.Abs(nav.speed - selected) > 0.01f)
                {
                    nav.speed = selected;
                    _lastOwnedAgentSpeed = selected;
                    _setAgentSpeed = true;
                    NoteMovementWriter("Follow.SpeedSync");
                }
                if (nav.enabled && nav.isOnNavMesh && nav.isStopped)
                {
                    nav.isStopped = false;
                    NoteMovementWriter("Follow.UnstopSync");
                }
            }
            catch { }

            Animator animator = _ownedAnimator ?? ResolveLeaderAnimator();
            if (animator == null) return;
            Vector3 position = _leader.transform.position;
            float now = Time.time;
            float dt = _locomotionSampleAt > 0f ? Math.Max(0f, now - _locomotionSampleAt) : 0f;
            float delta = _locomotionSampleAt > 0f ? HorizontalDistance(position, _locomotionSamplePosition) : 0f;
            float velocity = 0f;
            float desired = 0f;
            bool stopped = false;
            try { velocity = nav.velocity.magnitude; desired = nav.desiredVelocity.magnitude; stopped = nav.isStopped; } catch { }
            Vector3 order;
            bool orderValid = TryCurrentOrder(out order);
            float distance = orderValid ? HorizontalDistance(position, order) : float.MaxValue;
            bool walking = ExpeditionMovementOwnershipPolicy.ShouldShowWalking(velocity, desired, delta, dt, distance, stopped);
            try
            {
                bool currentWalking = animator.GetBool("Walking");
                bool currentPatrol = animator.GetBool("Patrol");
                if (currentWalking != walking || currentPatrol)
                {
                    animator.SetBool("Walking", walking);
                    animator.SetBool("Patrol", false);
                    _lastOwnedWalking = walking;
                    _lastOwnedPatrol = false;
                    _setAnimatorState = true;
                    NoteMovementWriter("Follow.LocomotionSync");
                }
            }
            catch { }
            _locomotionSamplePosition = position;
            _locomotionSampleAt = now;
        }

        private static void ReleaseTravelMovementOwnership(string reason, bool restoreOwnedState)
        {
            if (!_travelMovementOwned)
            {
                if (restoreOwnedState) ResetOwnedMovementAdapter();
                return;
            }
            if (_leader != null) NoteMovementBoundary("ownership_release." + (reason ?? "unknown"));

            NavMeshAgent nav = ResolveLeaderAgent();
            if (restoreOwnedState && nav != null && _setAgentSpeed)
            {
                try
                {
                    if (ExpeditionMovementOwnershipPolicy.ShouldRestoreOwnedFloat(nav.speed, _lastOwnedAgentSpeed,
                        _capturedAgentSpeedValid ? _capturedAgentSpeed : 0f)) nav.speed = _capturedAgentSpeed;
                }
                catch { }
            }
            if (restoreOwnedState && _ownedAnimator != null && _setAnimatorState && _capturedAnimatorState)
            {
                try
                {
                    bool walking = _ownedAnimator.GetBool("Walking");
                    bool patrol = _ownedAnimator.GetBool("Patrol");
                    if (walking == _lastOwnedWalking && patrol == _lastOwnedPatrol)
                    {
                        _ownedAnimator.SetBool("Walking", _capturedWalking);
                        _ownedAnimator.SetBool("Patrol", _capturedPatrol);
                    }
                }
                catch { }
            }
            _travelMovementOwned = false;
            _currentOrderGeneration = 0;
            NoteMovementWriter("Follow.OwnershipRelease." + (reason ?? "unknown"));
            ResetOwnedMovementAdapter();
        }

        private static void ResetOwnedMovementAdapter()
        {
            _capturedAgentSpeed = 0f;
            _capturedAgentSpeedValid = false;
            _lastOwnedAgentSpeed = 0f;
            _setAgentSpeed = false;
            _ownedAnimator = null;
            _capturedWalking = false;
            _capturedPatrol = false;
            _capturedAnimatorState = false;
            _lastOwnedWalking = false;
            _lastOwnedPatrol = false;
            _setAnimatorState = false;
            _locomotionSamplePosition = Vector3.zero;
            _locomotionSampleAt = 0f;
        }

        private static Animator ResolveLeaderAnimator()
        {
            try
            {
                if (_leader != null && _leader.MyStats != null && _leader.MyStats.Myself != null)
                    return _leader.MyStats.Myself.GetMyAnim();
            }
            catch { }
            return null;
        }

        private static string DescribeNativeMovementState(Vector3 target)
        {
            NPC npc = ResolveLeaderNpc();
            NavMeshAgent nav = ResolveLeaderAgent();
            if (npc == null) return "npc=missing agent=" + (nav == null ? "missing" : "present");
            if (nav == null) return "npc=ok agent=missing";
            try
            {
                string destination = nav.enabled && nav.isOnNavMesh
                    ? HorizontalDistance(nav.destination, target).ToString("F1") + "m-from-order"
                    : "unreadable";
                return "npc=ok agent=" + (nav.enabled ? "enabled" : "disabled") +
                    " onMesh=" + nav.isOnNavMesh +
                    " stopped=" + nav.isStopped +
                    " pending=" + nav.pathPending +
                    " hasPath=" + nav.hasPath +
                    " path=" + nav.pathStatus +
                    " speed=" + nav.speed.ToString("F2") +
                    " velocity=" + nav.velocity.magnitude.ToString("F2") +
                    " desiredVelocity=" + nav.desiredVelocity.magnitude.ToString("F2") +
                    " destination=" + destination;
            }
            catch (Exception ex) { return "npc=ok agent telemetry failed (" + ex.GetType().Name + ")"; }
        }

        private static NPC ResolveLeaderNpc()
        {
            return ResolveSimNpc(_leader);
        }

        private static NPC ResolveSimNpc(SimPlayer sim)
        {
            if (sim == null) return null;
            try
            {
                NPC native = sim.GetThisNPC();
                if (native != null) return native;
            }
            catch { }
            try { return sim.MyStats == null || sim.MyStats.Myself == null ? null : sim.MyStats.Myself.MyNPC; }
            catch { return null; }
        }

        private static NavMeshAgent ResolveLeaderAgent()
        {
            NPC npc = ResolveLeaderNpc();
            try
            {
                if (npc != null)
                {
                    NavMeshAgent native = npc.GetComponent<NavMeshAgent>();
                    if (native != null) return native;
                }
            }
            catch { }
            try { return _leader == null ? null : _leader.GetComponent<NavMeshAgent>(); }
            catch { return null; }
        }

        private static string SafeName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace("\n", " ").Replace("\r", " ").Trim();
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
            string failure;
            if (TryIssueZoneTravelOrder(target, false, true, "Follow.Waypoint", out failure))
            {
                _lastLeaderProgressPosition = _leader.transform.position;
                _lastLeaderProgressAt = Time.time;
                Verbose(_zoneWaypointIndex < ZoneWaypoints.Count
                    ? "advanced to NavMesh corner " + (_zoneWaypointIndex + 1) + "/" + ZoneWaypoints.Count +
                        " at " + LocalZoneRoutePlanner.FormatVector(target)
                    : "completed NavMesh corners; continuing to crossing approach at " +
                        LocalZoneRoutePlanner.FormatVector(target));
            }
            else
            {
                _lastMovementDiagnostic = failure;
                Verbose("could not apply next route waypoint: " + failure);
            }
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
            if (_crossingAttemptActive) return _crossingAttemptTarget;
            return _zoneWaypointIndex >= 0 && _zoneWaypointIndex < ZoneWaypoints.Count
                ? ZoneWaypoints[_zoneWaypointIndex] : _zoneApproach;
        }

        // Emitted once per crossing handoff (and once when a handoff cannot produce a traversal
        // target), never per frame. Carries enough real collider/seed geometry to explain a
        // zero-accepted or far-landing approach without needing another live session.
        private static void EmitCrossingHandoffDiagnostic(string phase, float approachDistance, float triggerDistance)
        {
            try
            {
                NavMeshAgent nav = ResolveLeaderAgent();
                float velocity = 0f;
                string pathStatus = "unavailable";
                try
                {
                    if (nav != null && nav.enabled && nav.isOnNavMesh)
                    {
                        velocity = nav.velocity.magnitude;
                        pathStatus = nav.hasPath ? nav.pathStatus.ToString() : "noPath";
                    }
                }
                catch { }

                string selected = _crossingTraversalIndex >= 0 && _crossingTraversalIndex < CrossingTraversalOptions.Count
                    ? LocalZoneRoutePlanner.FormatVector(CrossingTraversalOptions[_crossingTraversalIndex].Target)
                    : "none";
                bool insideTrigger = triggerDistance <= 0.01f;

                ExpeditionPhaseTelemetry.Record("crossing_handoff",
                    "crossing=" + SafeName(_destinationName) +
                    " phase=" + phase +
                    " approach=" + LocalZoneRoutePlanner.FormatVector(_zoneApproach) +
                    " leaderDistanceToApproach=" + approachDistance.ToString("F2") + "m" +
                    " colliderBounds=" + LocalZoneRoutePlanner.DescribeCrossingColliders(_destination) +
                    " traversalCandidates=" + CrossingTraversalOptions.Count +
                    " selectedTraversalTarget=" + selected +
                    " pathStatus=" + pathStatus +
                    " endpointDistance=" + triggerDistance.ToString("F2") + "m" +
                    " insideTrigger=" + insideTrigger +
                    " agentVelocity=" + velocity.ToString("F2") +
                    " zoneChanged=false");
            }
            catch { }
        }

        private static bool HandleCrossingAttempt()
        {
            if (_leader == null || _destination == null || _monster != null) return false;

            float triggerDistance = LocalZoneRoutePlanner.DistanceToCrossing(_leader.transform.position, _destination);
            float approachDistance = HorizontalDistance(_leader.transform.position, _zoneApproach);
            bool finalRoutePhase = _zoneWaypointIndex >= ZoneWaypoints.Count;
            bool stalledNearTrigger = LeaderNoProgressExpired() &&
                triggerDistance <= ExpeditionCrossingPolicy.StalledNearTriggerDistance;
            bool approachReady = finalRoutePhase &&
                (approachDistance <= ExpeditionCrossingPolicy.ApproachReadyDistance || stalledNearTrigger);

            if (approachReady && !_approachTelemetryEmitted)
            {
                _approachTelemetryEmitted = true;
                ExpeditionPhaseTelemetry.Record("approach_reached",
                    "destination=" + SafeName(_destinationName) +
                    " approachDistance=" + approachDistance.ToString("F2") + "m" +
                    " trueTriggerDistance=" + triggerDistance.ToString("F2") + "m");
            }

            if (!_crossingAttemptActive)
            {
                if (!approachReady) return false;
                EnsureCrossingTraversalOptions();
                ExpeditionCrossingInputs begin = new ExpeditionCrossingInputs();
                begin.ApproachReady = true;
                begin.HasTraversalTarget = CrossingTraversalOptions.Count > 0;
                ExpeditionCrossingDecision beginDecision = ExpeditionCrossingPolicy.Evaluate(begin);
                EmitCrossingHandoffDiagnostic(
                    beginDecision == ExpeditionCrossingDecision.Fail ? "NoTraversalTarget" : "ApproachReachedTraversalPending",
                    approachDistance, triggerDistance);
                if (beginDecision == ExpeditionCrossingDecision.Fail)
                {
                    _lastCrossingDiagnostic = "no NavMesh traversal target was proven through the real trigger shape";
                    ExpeditionPhaseTelemetry.Record("crossing_trigger_not_entered", _lastCrossingDiagnostic);
                    FailRoute(_lastCrossingDiagnostic, RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed);
                    return true;
                }
                return StartCrossingAttempt(false);
            }

            ExpeditionCrossingInputs input = new ExpeditionCrossingInputs();
            input.AttemptActive = true;
            input.HasTraversalTarget = _crossingTraversalIndex + 1 < CrossingTraversalOptions.Count;
            input.AttemptElapsedSeconds = Time.time - _crossingAttemptSince;
            input.AttemptCount = _crossingAttemptCount;
            ExpeditionCrossingDecision decision = ExpeditionCrossingPolicy.Evaluate(input);
            if (decision == ExpeditionCrossingDecision.Waiting)
            {
                _lastLeaderProgressAt = Time.time;
                RefreshNativeNavigation(false, Vector3.zero);
                return true;
            }
            if (decision == ExpeditionCrossingDecision.RetryAttempt)
            {
                ExpeditionPhaseTelemetry.Record("crossing_trigger_not_entered",
                    "attempt=" + _crossingAttemptCount + " target=" +
                    LocalZoneRoutePlanner.FormatVector(_crossingAttemptTarget) + " elapsed=" +
                    input.AttemptElapsedSeconds.ToString("F1") + "s");
                return StartCrossingAttempt(true);
            }
            if (decision == ExpeditionCrossingDecision.Fail)
            {
                _lastCrossingDiagnostic = "verified crossing traversal did not enter the native trigger after " +
                    _crossingAttemptCount + " bounded attempt(s)";
                ExpeditionPhaseTelemetry.Record("crossing_trigger_not_entered", _lastCrossingDiagnostic);
                FailRoute(_lastCrossingDiagnostic, RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed);
                return true;
            }
            return decision != ExpeditionCrossingDecision.None;
        }

        private static void EnsureCrossingTraversalOptions()
        {
            if (CrossingTraversalOptions.Count > 0 || _leader == null || _destination == null) return;
            CrossingTraversalOptions.AddRange(LocalZoneRoutePlanner.BuildCrossingTraversalTargets(
                _leader.transform.position, _destination));
            _crossingTraversalIndex = 0;
            Verbose("built " + CrossingTraversalOptions.Count + " trigger-crossing traversal target(s) for " + _destinationName);
        }

        private static bool StartCrossingAttempt(bool advance)
        {
            if (_leader == null || _destination == null) return false;
            if (advance) _crossingTraversalIndex++;
            if (_crossingTraversalIndex < 0 || _crossingTraversalIndex >= CrossingTraversalOptions.Count)
            {
                _lastCrossingDiagnostic = "no additional trigger-crossing traversal target is available";
                FailRoute(_lastCrossingDiagnostic, RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed);
                return true;
            }
            if (_crossingAttemptCount >= ExpeditionCrossingPolicy.MaximumAttempts)
            {
                _lastCrossingDiagnostic = "crossing retry bound exhausted";
                FailRoute(_lastCrossingDiagnostic, RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed);
                return true;
            }

            LocalZoneRoutePlanner.CrossingTraversalOption option = CrossingTraversalOptions[_crossingTraversalIndex];
            string failure;
            NoteMovementBoundary("crossing_handoff.before");
            if (!TryIssueZoneTravelOrder(option.Target, false, false, "Follow.CrossingOrder", out failure))
            {
                _lastCrossingDiagnostic = "crossing movement order failed: " + failure;
                if (_crossingTraversalIndex + 1 < CrossingTraversalOptions.Count &&
                    _crossingAttemptCount + 1 < ExpeditionCrossingPolicy.MaximumAttempts)
                {
                    _crossingAttemptCount++;
                    return StartCrossingAttempt(true);
                }
                FailRoute(_lastCrossingDiagnostic, RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed);
                return true;
            }

            _crossingAttemptActive = true;
            _crossingAttemptSince = Time.time;
            _crossingAttemptCount++;
            _crossingAttemptTarget = option.Target;
            _crossingAttemptTargetValid = true;
            _lastLeaderProgressAt = Time.time;
            _lastCrossingDiagnostic = "native crossing attempt " + _crossingAttemptCount + "/" +
                ExpeditionCrossingPolicy.MaximumAttempts + " via " + option.TriggerType +
                " path=" + option.PathStatus + " start->trigger=" + option.StartDistanceToTrigger.ToString("F2") + "m";
            ExpeditionPhaseTelemetry.Record("crossing_attempt_started",
                _lastCrossingDiagnostic + " target=" + LocalZoneRoutePlanner.FormatVector(option.Target));
            NoteMovementBoundary("crossing_handoff.after");
            Verbose(_lastCrossingDiagnostic);
            return true;
        }

        private static bool HandleObservedCrossingWait()
        {
            if (!_leaderCrossingTriggerEntered && !_playerCrossingTriggerEntered) return false;

            ExpeditionCrossingInputs input = new ExpeditionCrossingInputs();
            input.LeaderTriggerEntered = _leaderCrossingTriggerEntered;
            input.PlayerTriggerEntered = _playerCrossingTriggerEntered;
            input.NativeZoning = GameData.Zoning;
            input.LeaderTriggerElapsedSeconds = _leaderCrossingTriggerEntered && _leaderCrossingTriggerAt > 0f
                ? Time.time - _leaderCrossingTriggerAt : 0f;
            input.PlayerTriggerElapsedSeconds = _playerCrossingTriggerEntered && _playerCrossingTriggerAt > 0f
                ? Time.time - _playerCrossingTriggerAt : 0f;

            ExpeditionCrossingDecision decision = ExpeditionCrossingPolicy.Evaluate(input);
            if (decision == ExpeditionCrossingDecision.NativeTransitionObserved) return true;
            if (decision == ExpeditionCrossingDecision.Waiting) return true;
            if (decision == ExpeditionCrossingDecision.Fail)
            {
                string reason = _playerCrossingTriggerEntered
                    ? "the player entered the selected native Zoneline trigger but GameData.Zoning did not begin within the bounded handoff window"
                    : "the leader entered the selected native Zoneline trigger, but the player did not begin the native zone transition within " +
                        ExpeditionCrossingPolicy.LeaderTriggerGraceSeconds.ToString("F0") + "s";
                _lastCrossingDiagnostic = reason;
                ExpeditionPhaseTelemetry.Record("crossing_transition_failed", reason);
                FailCrossingTransitionTerminal(reason);
                return true;
            }
            return false;
        }

        internal static void NoteNativeZonelineTrigger(Zoneline crossing, Collider other)
        {
            if (!_active || !_expeditionOwned || crossing == null || other == null || _destination == null ||
                crossing.RemoveParty || !object.ReferenceEquals(crossing, _destination)) return;

            bool player = false;
            try { player = other.transform != null && string.Equals(other.transform.name, "Player", StringComparison.Ordinal); }
            catch { }
            if (player)
            {
                if (_playerCrossingTriggerEntered) return;
                NoteMovementBoundary("crossing_player_trigger.before");
                _playerCrossingTriggerEntered = true;
                _playerCrossingTriggerAt = Time.time;
                _lastCrossingDiagnostic = "player entered selected native Zoneline trigger";
                ExpeditionPhaseTelemetry.Record("crossing_trigger_entered",
                    "actor=Player destination=" + SafeName(_destinationName));
                FollowController.Stop();
                NoteMovementWriter("Native.Zoneline.PlayerTrigger");
                NoteMovementBoundary("crossing_player_trigger.after");
                return;
            }

            SimPlayer sim = null;
            try { sim = other.GetComponent<SimPlayer>(); } catch { }
            if (sim == null || _leader == null || !object.ReferenceEquals(sim, _leader) || _leaderCrossingTriggerEntered) return;
            NoteMovementBoundary("crossing_leader_trigger.before");
            _leaderCrossingTriggerEntered = true;
            _leaderCrossingTriggerAt = Time.time;
            bool canContinuePlayerToProvenTarget = _crossingAttemptTargetValid;
            _crossingAttemptActive = false;
            _lastCrossingDiagnostic = "exact expedition leader entered selected native Zoneline trigger";
            ExpeditionPhaseTelemetry.Record("crossing_trigger_entered",
                "actor=Leader name=" + SafeName(_leaderName) + " destination=" + SafeName(_destinationName));
            NoteMovementWriter("Native.Zoneline.LeaderTrigger");
            NoteMovementBoundary("crossing_leader_trigger.after");

            // Native Erenshor destroys a Sim avatar when that Sim enters the Zoneline, while only the
            // player's own trigger starts the scene change. Preserve the already-active player Follow
            // owner just long enough to walk toward the SAME through-trigger target that was proven for
            // this crossing. The instant the player's real trigger fires, NoteNativeZonelineTrigger stops
            // this handoff and native GameData.Zoning owns everything.
            if (!canContinuePlayerToProvenTarget ||
                !FollowController.BeginExpeditionCrossingHandoff(_crossingAttemptTarget,
                    ExpeditionCrossingPolicy.LeaderTriggerGraceSeconds))
                FollowController.Stop();
        }

        private static void FailCrossingTransitionTerminal(string reason)
        {
            _legRouteFailureReason = reason;
            _legRouteFailureKind = RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed;
            Report(LegEvent.RouteFailed);
        }

        private static void ResetCrossingState(string diagnostic)
        {
            CrossingTraversalOptions.Clear();
            _crossingTraversalIndex = 0;
            _crossingAttemptActive = false;
            _crossingAttemptSince = 0f;
            _crossingAttemptCount = 0;
            _crossingAttemptTarget = Vector3.zero;
            _crossingAttemptTargetValid = false;
            _leaderCrossingTriggerEntered = false;
            _leaderCrossingTriggerAt = 0f;
            _playerCrossingTriggerEntered = false;
            _playerCrossingTriggerAt = 0f;
            _approachTelemetryEmitted = false;
            if (!string.IsNullOrWhiteSpace(diagnostic)) _lastCrossingDiagnostic = diagnostic;
        }

        private static bool RefreshNativeNavigation(bool force, Vector3 forcedTarget)
        {
            if (_leader == null || (!force && (_destination == null && _monster == null))) return false;
            if (!force && Time.time < _nextNativeNavRefresh) return ResolveLeaderNpc() != null;
            _nextNativeNavRefresh = Time.time + 0.4f;
            Vector3 target = force ? forcedTarget : (_monster == null ? CurrentZoneTarget() : _monster.transform.position);
            NPC npc = ResolveLeaderNpc();
            if (npc == null) return false;
            try
            {
                if (_expeditionOwned) TickMovementTelemetry();
                if (_expeditionOwned && _travelMovementOwned)
                    PrepareAgentForOrder(npc, target, true);
                npc.HighPriorityNavUpdate(target);
                if (_expeditionOwned)
                {
                    NoteMovementWriter(force ? "Follow.ForcedRefresh" : "Follow.Refresh");
                    TickMovementTelemetry();
                }
                return true;
            }
            catch { return false; }
        }

        private static bool InCombat(SimPlayer sim)
        {
            if (GameData.InCombat) return true;
            try { if (sim != null && sim.IsSimGroupInCombat()) return true; } catch { }
            NPC npc = ResolveSimNpc(sim);
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
            if (_monster != null && _lastLeaderProgressAt > 0f && Time.time - _lastLeaderProgressAt >= NoProgressFailureSeconds) return false;
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
            ReleaseTravelMovementOwnership("clear_without_restore", false);
            ExpeditionMovementTelemetry.Reset();
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
            ResetMovementProof("idle");
            ResetCrossingState("idle");
            _routeResampleAttempted = false;
            _zoneWaypointIndex = -1;
            _currentOrderGeneration = 0;
            _lastMovementWriter = "none";
            _lastMovementWriterGeneration = 0;
            _lastMovementWriterAt = 0f;
            ResetOwnedMovementAdapter();
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
