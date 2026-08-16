using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    internal static class FollowController
    {
        internal enum StopReason
        {
            None,
            Explicit,
            ManualMovement,
            RouteUnavailable,
            TargetUnavailable,
            TargetLeftParty,
            TargetRemote,
            TargetIdentityMismatch,
            ZoneRebindTimeout,
            ZoneHandoffTimeout,
            ZoneContinuationDisabled,
            PlayerUnavailable
        }

        internal enum DriveState { Idle, Waiting, Turning, Moving, PartialPathRetry, NoProgress, RecoveryRepath, PausedForCombat, RecoveringAfterCombat, AwaitingNativeZoneChange, RebindingAfterZoneChange }
        internal struct StatusSnapshot
        {
            internal readonly bool Active;
            internal readonly string TargetName;
            internal readonly DriveState State;
            internal readonly StopReason LastStopReason;

            internal StatusSnapshot(bool active, string targetName, DriveState state, StopReason lastStopReason)
            {
                Active = active;
                TargetName = targetName;
                State = state;
                LastStopReason = lastStopReason;
            }
        }

        private static readonly NavMeshPath Path = new NavMeshPath();
        private static readonly FollowIntentState<SimPlayerTracking> DirectIntent = new FollowIntentState<SimPlayerTracking>();
        private static SimPlayer _target;
        private static PlayerControl _drivenPlayer;
        private static float _nextPathTime;
        private static Vector3 _waypoint;
        private static Vector3 _nextCorner;
        private static bool _hasNextCorner;
        private static bool _hasWaypoint;
        private static bool _waitingAtTarget;
        private static Vector3 _lastProgressPosition;
        private static float _lastTargetDistance;
        private static float _lastProgressTime;
        private static float _routeFailureSince;
        private static Vector3 _lastPartialEndpoint;
        private static bool _hasPartialEndpoint;
        private static bool _active;
        private static bool _directFollowIntent;
        private static CharacterController _drivenController;
        private static int _targetValidFrame = -1;
        private static bool _targetValidCache;
        private static FollowActorEligibility _targetEligibilityCache = FollowActorEligibility.MissingOrDead;
        private static string _followScene;
        private static string _zoneOriginScene;
        private static float _rebindStartedAt;
        private static float _rebindReadySince;
        private static float _zoneHandoffStartedAt;
        private static int _recoveryAttempts;
        private static float _nextRecoveryAt;
        private static float _lastPathAttemptAt;
        private static bool _combatPaused;
        private static float _combatClearSince;
        private static bool _expeditionCrossingHandoff;
        private static Vector3 _expeditionCrossingTarget;
        private static float _expeditionCrossingUntil;

        private const float StopDistance = 3.0f;
        private const float ResumeDistance = 4.5f;
        private const float ProgressDistance = 0.2f;
        private const float RouteRetrySeconds = 3f;
        // Match the already-proven Expedition transition lifecycle rather than assuming sceneLoaded means
        // the game's group/avatar state is ready on that frame.
        private const float RebindSettleSeconds = 2.5f;
        private const float RebindTimeoutSeconds = 60f;

        internal static bool Active { get { return _active; } }
        internal static string TargetName { get; private set; }
        internal static StopReason LastStopReason { get; private set; }
        internal static DriveState State { get; private set; }

        // Runs once per frame from the plugin's Update loop. Direct Follow has one special exception to
        // ordinary target invalidation: a verified GameData.Zoning lifecycle suspends the scene-bound
        // avatar while preserving its SimPlayerTracking identity. Leader/Expedition-owned Follow remains
        // under its existing owner and does not enter this state machine.
        internal static void Tick()
        {
            if (!_active) return;

            if (_directFollowIntent && DirectIntent.Phase == FollowIntentPhase.Rebinding)
            {
                TickRebind();
                return;
            }

            if (GameData.Zoning)
            {
                if (_directFollowIntent)
                {
                    if (!CrossZoneContinuationEnabled())
                    {
                        Notify("[Erenshor Follow] Direct cross-zone continuation is experimental and currently OFF. Following stopped for this native zone change.", "yellow");
                        Stop(StopReason.ZoneContinuationDisabled);
                    }
                    else if (!BeginZoneRebind())
                    {
                        Notify("[Erenshor Follow] The target has no persistent Sim tracking identity, so Follow cannot safely continue across this zone change.", "yellow");
                        Stop(StopReason.TargetUnavailable);
                    }
                }
                // Do not validate or drive a scene-bound avatar while the game owns a zone transition.
                // Expedition/Lead will tear down its own Follow leg through its existing lifecycle.
                return;
            }

            if (_expeditionCrossingHandoff)
            {
                // The expedition leader may already have been destroyed by its own native Zoneline
                // trigger. During this tiny bounded gap, Continue-To-Crossing drives only the player
                // toward the exact traversal target that was already proven through that live trigger.
                // It never infers a zone or survives into GameData.Zoning.
                if (Time.time >= _expeditionCrossingUntil || GameData.InCombat || !IsLocalPlayerAvailable())
                {
                    Stop(GameData.InCombat ? StopReason.Explicit : StopReason.RouteUnavailable);
                }
                return;
            }

            // Only a transition that Follow actually observed through GameData.Zoning preserves intent.
            // A surprise scene replacement is ordinary target loss, not permission to search by name.
            if (_directFollowIntent && !string.IsNullOrWhiteSpace(_followScene) && !SameScene(ActiveScene(), _followScene))
            {
                Notify("[Erenshor Follow] Following stopped after an unexpected scene change.", "yellow");
                Stop(StopReason.TargetUnavailable);
                return;
            }

            if (_directFollowIntent && State == DriveState.AwaitingNativeZoneChange)
            {
                TickZoneHandoff();
                return;
            }

            if (!IsTargetValid())
            {
                if (_directFollowIntent && TryBeginZoneHandoff()) return;
                StopInvalidTarget();
                return;
            }

            if (_directFollowIntent && !IsLocalPlayerAvailable())
            {
                Notify("[Erenshor Follow] Following stopped because the player is unavailable or dead.", "yellow");
                Stop(StopReason.PlayerUnavailable);
                return;
            }

            if (_directFollowIntent && UpdateDirectCombatPause()) return;
        }

        internal static StatusSnapshot GetStatusSnapshot()
        {
            return new StatusSnapshot(_active, TargetName, State, LastStopReason);
        }

        internal static void Start(SimPlayer target, string name)
        {
            // Switching a live follow target must release the previous CharacterController/moving
            // state before ownership is rebound to the new Sim.
            if (FollowStartTransitionPolicy.ShouldReleaseMovementBeforeStart(_active)) RestoreMovementState();
            _drivenPlayer = null;
            _drivenController = null;
            _target = target;
            TargetName = string.IsNullOrWhiteSpace(name) ? (target == null || target.gameObject == null ? "the selected Sim" : target.gameObject.name) : name;
            _nextPathTime = 0f;
            _hasWaypoint = false;
            _hasNextCorner = false;
            _waitingAtTarget = false;
            _lastProgressPosition = Vector3.zero;
            _lastTargetDistance = float.MaxValue;
            _lastProgressTime = 0f;
            _routeFailureSince = 0f;
            _lastPartialEndpoint = Vector3.zero;
            _hasPartialEndpoint = false;
            LastStopReason = StopReason.None;
            State = DriveState.Idle;
            _active = target != null;
            _targetValidFrame = -1;
            _followScene = ActiveScene();
            _zoneOriginScene = null;
            _rebindStartedAt = 0f;
            _rebindReadySince = 0f;
            _zoneHandoffStartedAt = 0f;
            _recoveryAttempts = 0;
            _nextRecoveryAt = 0f;
            _lastPathAttemptAt = 0f;
            _combatPaused = false;
            _combatClearSince = 0f;
            _expeditionCrossingHandoff = false;
            _expeditionCrossingTarget = Vector3.zero;
            _expeditionCrossingUntil = 0f;

            // LeaderController sets its leg active before it calls FollowController.Start(), so this cleanly
            // distinguishes ordinary /efollow from the existing Lead/Expedition movement substrate without
            // adding another ownership API or changing leader routing.
            _directFollowIntent = _active && !LeaderController.LegActive;
            if (_directFollowIntent)
                DirectIntent.Begin(SimTrackingRebind.Capture(target));
            else
                DirectIntent.Cancel();
        }

        internal static bool BeginExpeditionCrossingHandoff(Vector3 target, float durationSeconds)
        {
            if (!_active || !LeaderController.ExpeditionOwned || GameData.Zoning) return false;
            if (float.IsNaN(target.x) || float.IsNaN(target.y) || float.IsNaN(target.z) ||
                float.IsInfinity(target.x) || float.IsInfinity(target.y) || float.IsInfinity(target.z)) return false;
            if (durationSeconds <= 0f) return false;

            _target = null;
            _targetValidFrame = -1;
            _targetValidCache = false;
            _targetEligibilityCache = FollowActorEligibility.MissingOrDead;
            _directFollowIntent = false;
            DirectIntent.Cancel();
            _expeditionCrossingHandoff = true;
            _expeditionCrossingTarget = target;
            _expeditionCrossingUntil = Time.time + durationSeconds;
            _hasWaypoint = false;
            _hasNextCorner = false;
            _waitingAtTarget = false;
            _nextPathTime = 0f;
            _routeFailureSince = 0f;
            _lastProgressTime = 0f;
            _recoveryAttempts = 0;
            _nextRecoveryAt = 0f;
            State = DriveState.Moving;
            return true;
        }

        internal static void Stop(StopReason reason = StopReason.Explicit)
        {
            // Once native zoning has begun, do not call CharacterController.SimpleMove or animation
            // helpers on scene-bound PlayerControl objects. Erenshor owns that teardown completely.
            // Clearing our references is sufficient; the LandMovement prefix no longer suppresses vanilla.
            bool nativeZoning = false;
            try { nativeZoning = GameData.Zoning; } catch { }
            if (!nativeZoning) RestoreMovementState();
            _active = false;
            LastStopReason = reason;
            _target = null;
            TargetName = null;
            _hasWaypoint = false;
            _hasNextCorner = false;
            _waitingAtTarget = false;
            _lastProgressPosition = Vector3.zero;
            _lastTargetDistance = float.MaxValue;
            _lastProgressTime = 0f;
            _routeFailureSince = 0f;
            _lastPartialEndpoint = Vector3.zero;
            _hasPartialEndpoint = false;
            _drivenPlayer = null;
            _drivenController = null;
            _targetValidFrame = -1;
            _directFollowIntent = false;
            _followScene = null;
            _zoneOriginScene = null;
            _rebindStartedAt = 0f;
            _rebindReadySince = 0f;
            _zoneHandoffStartedAt = 0f;
            _recoveryAttempts = 0;
            _nextRecoveryAt = 0f;
            _lastPathAttemptAt = 0f;
            _combatPaused = false;
            _combatClearSince = 0f;
            _expeditionCrossingHandoff = false;
            _expeditionCrossingTarget = Vector3.zero;
            _expeditionCrossingUntil = 0f;
            DirectIntent.Cancel();
            State = DriveState.Idle;
        }

        internal static bool TryDrive(PlayerControl player)
        {
            if (!_active) return false;

            // GameData.Zoning is set before the scene swap. Never suppress native PlayerControl movement
            // or touch the stale target while Erenshor is transitioning scenes.
            if (GameData.Zoning)
            {
                if (_directFollowIntent && DirectIntent.Phase != FollowIntentPhase.Rebinding)
                {
                    if (!CrossZoneContinuationEnabled())
                    {
                        Notify("[Erenshor Follow] Direct cross-zone continuation is experimental and currently OFF. Following stopped for this native zone change.", "yellow");
                        Stop(StopReason.ZoneContinuationDisabled);
                    }
                    else if (!BeginZoneRebind())
                    {
                        Notify("[Erenshor Follow] The target has no persistent Sim tracking identity, so Follow cannot safely continue across this zone change.", "yellow");
                        Stop(StopReason.TargetUnavailable);
                    }
                }
                return false;
            }
            if (_directFollowIntent && DirectIntent.Phase == FollowIntentPhase.Rebinding) return false;
            if (_directFollowIntent && State == DriveState.AwaitingNativeZoneChange) return false;

            bool crossingHandoff = _expeditionCrossingHandoff;
            if (crossingHandoff)
            {
                if (Time.time >= _expeditionCrossingUntil || GameData.InCombat)
                {
                    Stop(GameData.InCombat ? StopReason.Explicit : StopReason.RouteUnavailable);
                    return false;
                }
            }
            else if (!IsTargetValid())
            {
                if (_directFollowIntent && TryBeginZoneHandoff()) return false;
                StopInvalidTarget();
                return false;
            }
            // Ordinary Follow does not own combat movement. Yield the PlayerControl patch completely
            // while either the player/group or followed Sim is in real combat, then wait a short safety
            // window before resuming. If the player is still holding movement when that window ends,
            // the existing manual-takeover rule cancels Follow instead of fighting the input.
            if (_directFollowIntent && UpdateDirectCombatPause()) return false;

            if (player == null || player.Myself == null || !player.Myself.Alive)
            {
                if (_directFollowIntent)
                    Notify("[Erenshor Follow] Following stopped because the player is unavailable or dead.", "yellow");
                Stop(StopReason.PlayerUnavailable);
                return false;
            }
            if (ManualMovementKeyHeld())
            {
                Stop(StopReason.ManualMovement);
                return false;
            }

            if (_drivenPlayer != player) _drivenController = null;
            _drivenPlayer = player;
            if (_drivenController == null) _drivenController = player.GetComponent<CharacterController>();
            CharacterController controller = _drivenController;
            if (controller == null)
            {
                Stop(StopReason.RouteUnavailable);
                return false;
            }
            if (!player.CanMove)
            {
                State = DriveState.Waiting;
                SetMoving(player, false);
                controller.SimpleMove(Vector3.zero);
                ResetProgress(player.transform.position, float.MaxValue);
                return true;
            }
            Vector3 from = player.transform.position;
            Vector3 to = crossingHandoff ? _expeditionCrossingTarget : _target.transform.position;
            Vector3 flat = to - from;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (!crossingHandoff && _waitingAtTarget && distance < ResumeDistance)
            {
                State = DriveState.Waiting;
                SetMoving(player, false);
                controller.SimpleMove(Vector3.zero);
                ResetProgress(from, distance);
                return true;
            }
            float stopDistance = crossingHandoff ? 0.20f : StopDistance;
            if (distance <= stopDistance)
            {
                _waitingAtTarget = true;
                State = DriveState.Waiting;
                SetMoving(player, false);
                controller.SimpleMove(Vector3.zero);
                ResetProgress(from, distance);
                return true;
            }
            if (_waitingAtTarget) ResetProgress(from, distance);
            _waitingAtTarget = false;

            if (_lastProgressTime <= 0f) ResetProgress(from, distance);
            else if (HorizontalDistance(from, _lastProgressPosition) > ProgressDistance || distance < _lastTargetDistance - ProgressDistance)
                ResetProgress(from, distance);

            if (Time.time >= _nextPathTime || !_hasWaypoint || HorizontalDistance(from, _waypoint) < 0.6f)
            {
                _nextPathTime = Time.time + 0.35f;
                _lastPathAttemptAt = Time.time;
                NavMeshHit fromHit = new NavMeshHit();
                NavMeshHit toHit = new NavMeshHit();
                bool sampled = NavMesh.SamplePosition(from, out fromHit, 7f, NavMesh.AllAreas) &&
                                NavMesh.SamplePosition(to, out toHit, 8f, NavMesh.AllAreas);
                if (sampled && NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, Path) &&
                    Path.status != NavMeshPathStatus.PathInvalid && Path.corners != null && Path.corners.Length > 1)
                {
                    _waypoint = Path.corners[1];
                    _hasNextCorner = Path.corners.Length > 2;
                    _nextCorner = _hasNextCorner ? Path.corners[2] : Vector3.zero;
                    _hasWaypoint = true;
                    if (Path.status == NavMeshPathStatus.PathComplete)
                    {
                        _routeFailureSince = 0f;
                        _hasPartialEndpoint = false;
                    }
                    else
                    {
                        Vector3 endpoint = Path.corners[Path.corners.Length - 1];
                        bool changed = !_hasPartialEndpoint || HorizontalDistance(endpoint, _lastPartialEndpoint) > 0.75f;
                        _lastPartialEndpoint = endpoint;
                        _hasPartialEndpoint = true;
                        if (changed || HorizontalDistance(from, _lastProgressPosition) > ProgressDistance)
                            _routeFailureSince = Time.time;
                        else if (_routeFailureSince <= 0f)
                            _routeFailureSince = Time.time;
                        State = DriveState.PartialPathRetry;
                    }
                }
                else
                {
                    _hasWaypoint = false;
                    if (_routeFailureSince <= 0f) _routeFailureSince = Time.time;
                    State = DriveState.PartialPathRetry;
                }
            }
            float noProgressSeconds = Time.time - _lastProgressTime;
            bool noProgress = noProgressSeconds >= RouteRetrySeconds;
            bool routeProblem = _routeFailureSince > 0f;
            if (noProgress) State = DriveState.NoProgress;

            FollowStuckRecoveryDecision recovery = FollowStuckRecoveryPolicy.Evaluate(
                noProgressSeconds,
                routeProblem,
                _recoveryAttempts,
                Time.time >= _nextRecoveryAt);
            if (recovery == FollowStuckRecoveryDecision.Repath)
            {
                _recoveryAttempts++;
                _nextRecoveryAt = Time.time + FollowStuckRecoveryPolicy.RecoveryRetrySeconds;
                _hasWaypoint = false;
                _hasNextCorner = false;
                _nextPathTime = 0f;
                State = DriveState.RecoveryRepath;
                SetMoving(player, false);
                controller.SimpleMove(Vector3.zero);
                return true;
            }
            if (recovery == FollowStuckRecoveryDecision.Stop)
            {
                Notify("[Erenshor Follow] Could not make progress after " + _recoveryAttempts +
                    " bounded repath attempts. Following " + TargetName + " stopped.", "yellow");
                Stop(StopReason.RouteUnavailable);
                return false;
            }
            if (!_hasWaypoint)
            {
                SetMoving(player, false);
                controller.SimpleMove(Vector3.zero);
                return true;
            }
            Vector3 destination = _waypoint;
            // CharacterController movement tends to catch its shoulder on a NavMesh corner.
            // Once close, steer slightly beyond that corner so the player rounds it instead of
            // repeatedly walking into the same wall until the stall guard stops the follow.
            if (_hasNextCorner && HorizontalDistance(from, _waypoint) < 1.8f)
            {
                Vector3 exit = _nextCorner - _waypoint;
                exit.y = 0f;
                if (exit.sqrMagnitude > 0.04f)
                    destination = _waypoint + exit.normalized * Math.Min(1.25f, exit.magnitude * 0.5f);
            }
            Vector3 direction = destination - from;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
            {
                State = DriveState.NoProgress;
                SetMoving(player, false);
                controller.SimpleMove(Vector3.zero);
                return true;
            }
            direction.Normalize();
            float speed = player.Myself.MyStats == null ? 3.5f : player.Myself.MyStats.actualRunSpeed;
            if (speed < 1f) speed = 3.5f;
            float turnAngle = Vector3.Angle(player.transform.forward, direction);
            State = turnAngle > 35f ? DriveState.Turning : (Path.status == NavMeshPathStatus.PathPartial ? DriveState.PartialPathRetry : DriveState.Moving);
            player.transform.rotation = Quaternion.RotateTowards(player.transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);
            controller.SimpleMove(direction * speed);
            SetMoving(player, true);
            try { player.UpdateAnimRun(); } catch { }
            return true;
        }

        internal static string ReadName(SimPlayer sim)
        {
            if (sim == null) return string.Empty;
            string[] candidates = { "PlayerName", "MyName", "CharacterName", "CharName", "SimName", "Name" };
            foreach (string candidate in candidates)
            {
                try
                {
                    FieldInfo field = sim.GetType().GetField(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(sim) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                    PropertyInfo property = sim.GetType().GetProperty(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null && property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(sim, null) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                }
                catch { }
            }
            return sim.gameObject == null ? string.Empty : sim.gameObject.name;
        }

        // Shared with ExpeditionCoordinator so a resume issued while a movement key is still down is
        // refused outright instead of starting and being cancelled again on the very next frame.
        internal static bool ManualMovementKeyHeld()
        {
            try
            {
                return Input.GetKey(InputManager.Forward) || Input.GetKey(InputManager.Backward) ||
                       Input.GetKey(InputManager.Left) || Input.GetKey(InputManager.Right) ||
                       Input.GetKey(InputManager.StrafeL) || Input.GetKey(InputManager.StrafeR) ||
                       Input.GetKey(InputManager.Jump);
            }
            catch { return false; }
        }

        internal static bool IsUsableSim(SimPlayer sim)
        {
            return sim != null && sim.gameObject != null && sim.gameObject.activeInHierarchy &&
                   sim.MyStats != null && sim.MyStats.Myself != null && sim.MyStats.Myself.Alive;
        }

        private static bool ManualCancelInputPressed()
        {
            try
            {
                return Input.GetKeyDown(InputManager.Forward) || Input.GetKeyDown(InputManager.Backward) ||
                       Input.GetKeyDown(InputManager.Left) || Input.GetKeyDown(InputManager.Right) ||
                       Input.GetKeyDown(InputManager.StrafeL) || Input.GetKeyDown(InputManager.StrafeR) ||
                       Input.GetKeyDown(InputManager.Jump) || Input.GetMouseButtonDown(0);
            }
            catch { return false; }
        }

        private static bool TryBeginZoneHandoff()
        {
            if (!_active || !_directFollowIntent || !CrossZoneContinuationEnabled()) return false;
            SimPlayerTracking tracking = DirectIntent.Identity;
            if (tracking == null || !SimTrackingRebind.TrackingIsInPlayerGroup(tracking)) return false;

            // A dead/disabled avatar is ordinary target loss. The narrow handoff grace exists only when
            // the persistent party identity still exists but Erenshor currently exposes no scene avatar,
            // which is the ordering seen when a Sim zones just ahead of the player.
            SimPlayer avatar = SimTrackingRebind.CurrentAvatar(tracking);
            if (avatar != null) return false;

            RestoreMovementState();
            _target = null;
            _drivenPlayer = null;
            _drivenController = null;
            _targetValidFrame = -1;
            _zoneHandoffStartedAt = Time.time;
            _combatPaused = false;
            _combatClearSince = 0f;
            ResetRouteStateForRebind();
            State = DriveState.AwaitingNativeZoneChange;
            Notify("[Erenshor Follow] " + TargetName + " entered a transition or left the local actor set. Keep moving normally through the native crossing if you intend to follow; Follow will stop if the player does not zone.", "yellow");
            return true;
        }

        private static void TickZoneHandoff()
        {
            SimPlayerTracking tracking = DirectIntent.Identity;
            SimPlayer avatar = SimTrackingRebind.CurrentAvatar(tracking);

            FollowZoneHandoffPolicy.Inputs input = new FollowZoneHandoffPolicy.Inputs();
            input.ContinuationEnabled = CrossZoneContinuationEnabled();
            input.HasPersistentIdentity = tracking != null;
            input.NativeZoning = GameData.Zoning;
            input.TrackingInGroup = tracking != null && SimTrackingRebind.TrackingIsInPlayerGroup(tracking);
            input.AvatarPresent = avatar != null;
            input.SameIdentity = avatar != null && SimTrackingRebind.AvatarMatchesTracking(tracking, avatar);
            input.AvatarUsable = avatar != null && IsUsableSim(avatar);
            input.LivePartyMember = avatar != null && LeaderController.IsPlayerPartySim(avatar);
            input.RemoteAuthority = avatar != null && CoopCompatibility.IsRemoteHuman(avatar);
            input.TimedOut = _zoneHandoffStartedAt > 0f &&
                Time.time - _zoneHandoffStartedAt >= FollowZoneHandoffPolicy.NativeZoneGraceSeconds;

            FollowZoneHandoffFailure failure;
            FollowZoneHandoffDecision decision = FollowZoneHandoffPolicy.Evaluate(input, out failure);
            if (decision == FollowZoneHandoffDecision.Wait)
            {
                State = DriveState.AwaitingNativeZoneChange;
                return;
            }
            if (decision == FollowZoneHandoffDecision.EnterRebind)
            {
                if (!BeginZoneRebind()) StopZoneHandoff(FollowZoneHandoffFailure.MissingIdentity);
                return;
            }
            if (decision == FollowZoneHandoffDecision.ResumeLocal)
            {
                _target = avatar;
                _zoneHandoffStartedAt = 0f;
                _targetValidFrame = -1;
                ResetRouteStateAfterRebind();
                State = DriveState.Idle;
                Notify("[Erenshor Follow] Reacquired " + TargetName + " in the current zone. Following resumed.", "lightblue");
                return;
            }
            StopZoneHandoff(failure);
        }

        private static void StopZoneHandoff(FollowZoneHandoffFailure failure)
        {
            StopReason reason = StopReason.TargetUnavailable;
            string detail;
            switch (failure)
            {
                case FollowZoneHandoffFailure.ContinuationDisabled:
                    reason = StopReason.ZoneContinuationDisabled;
                    detail = "cross-zone continuation was disabled before native zoning began";
                    break;
                case FollowZoneHandoffFailure.LeftParty:
                    reason = StopReason.TargetLeftParty;
                    detail = "left the real party before native player zoning began";
                    break;
                case FollowZoneHandoffFailure.IdentityMismatch:
                    reason = StopReason.TargetIdentityMismatch;
                    detail = "was replaced by a different SimPlayerTracking identity";
                    break;
                case FollowZoneHandoffFailure.RemoteAuthority:
                    reason = StopReason.TargetRemote;
                    detail = "became remote-authority";
                    break;
                case FollowZoneHandoffFailure.Timeout:
                    reason = StopReason.ZoneHandoffTimeout;
                    detail = "left the local actor set but the player never entered a native zone transition";
                    break;
                case FollowZoneHandoffFailure.MissingIdentity:
                    detail = "no longer has a persistent Sim tracking identity";
                    break;
                default:
                    detail = "is unavailable or dead";
                    break;
            }
            string name = string.IsNullOrWhiteSpace(TargetName) ? "The follow target" : TargetName;
            Notify("[Erenshor Follow] Following stopped: " + name + " " + detail + ".", "yellow");
            Stop(reason);
        }

        private static bool BeginZoneRebind()
        {
            if (!_active || !_directFollowIntent) return false;
            if (DirectIntent.Phase == FollowIntentPhase.Rebinding) return true;
            if (!FollowRebindPolicy.CanSuspendForZone(true, GameData.Zoning, DirectIntent.Identity != null, CrossZoneContinuationEnabled())) return false;
            if (!DirectIntent.BeginRebinding()) return false;

            // Do not call SimpleMove or any other PlayerControl movement method after GameData.Zoning
            // has begun. Releasing our references is enough; vanilla LandMovement is no longer suppressed.
            _target = null;
            _drivenPlayer = null;
            _drivenController = null;
            _targetValidFrame = -1;
            _zoneOriginScene = string.IsNullOrWhiteSpace(_followScene) ? ActiveScene() : _followScene;
            _rebindStartedAt = Time.time;
            _rebindReadySince = 0f;
            _zoneHandoffStartedAt = 0f;
            _combatPaused = false;
            _combatClearSince = 0f;
            ResetRouteStateForRebind();
            State = DriveState.RebindingAfterZoneChange;
            return true;
        }

        private static void TickRebind()
        {
            if (!CrossZoneContinuationEnabled())
            {
                Notify("[Erenshor Follow] Following stopped because experimental cross-zone continuation was disabled during the native transition.", "yellow");
                Stop(StopReason.ZoneContinuationDisabled);
                return;
            }

            SimPlayerTracking tracking = DirectIntent.Identity;
            if (tracking == null)
            {
                StopRebind(FollowRebindFailure.IdentityMismatch);
                return;
            }

            string scene = ActiveScene();
            bool sceneChanged = !SameScene(scene, _zoneOriginScene);
            // A key/button newly pressed on the far side is an explicit takeover. GetKeyDown avoids
            // misreading a movement key that was merely held through the fade as a cancellation.
            if (!GameData.Zoning && sceneChanged && ManualCancelInputPressed())
            {
                Stop(StopReason.ManualMovement);
                return;
            }
            bool gameReady = !GameData.Zoning && sceneChanged && GameData.SimMngr != null &&
                             GameData.SimPlayerGrouping != null && GameData.GroupMembers != null;

            if (!gameReady)
                _rebindReadySince = 0f;
            else if (_rebindReadySince <= 0f)
                _rebindReadySince = Time.time;

            bool settled = gameReady && _rebindReadySince > 0f && Time.time - _rebindReadySince >= RebindSettleSeconds;
            SimPlayer avatar = settled ? SimTrackingRebind.CurrentAvatar(tracking) : null;

            FollowRebindInputs input = new FollowRebindInputs();
            input.Zoning = GameData.Zoning;
            input.SceneChanged = sceneChanged;
            input.GameReady = gameReady;
            input.Settled = settled;
            input.TrackingInGroup = settled && SimTrackingRebind.TrackingIsInPlayerGroup(tracking);
            input.AvatarPresent = avatar != null;
            input.SameTracking = avatar != null && SimTrackingRebind.AvatarMatchesTracking(tracking, avatar);
            input.AvatarUsable = avatar != null && IsUsableSim(avatar);
            input.LivePartyMember = avatar != null && LeaderController.IsPlayerPartySim(avatar);
            input.RemoteAuthority = avatar != null && CoopCompatibility.IsRemoteHuman(avatar);
            input.TimedOut = _rebindStartedAt > 0f && Time.time - _rebindStartedAt >= RebindTimeoutSeconds;

            FollowRebindFailure failure;
            FollowRebindDecision decision = FollowRebindPolicy.Evaluate(input, out failure);
            if (decision == FollowRebindDecision.Waiting)
            {
                State = DriveState.RebindingAfterZoneChange;
                return;
            }
            if (decision == FollowRebindDecision.Stop)
            {
                StopRebind(failure);
                return;
            }

            _target = avatar;
            _followScene = scene;
            _zoneOriginScene = null;
            _rebindStartedAt = 0f;
            _rebindReadySince = 0f;
            _zoneHandoffStartedAt = 0f;
            _combatPaused = false;
            _combatClearSince = 0f;
            _targetValidFrame = -1;
            ResetRouteStateAfterRebind();
            if (!DirectIntent.ResumeAfterRebind())
            {
                StopRebind(FollowRebindFailure.IdentityMismatch);
                return;
            }
            State = DriveState.Idle;
            Notify("[Erenshor Follow] Reacquired " + TargetName + " after the zone change. Following resumed.", "lightblue");
        }

        private static void StopRebind(FollowRebindFailure failure)
        {
            StopReason reason = StopReason.TargetUnavailable;
            string detail;
            switch (failure)
            {
                case FollowRebindFailure.Timeout:
                    reason = StopReason.ZoneRebindTimeout;
                    detail = "could not be rebound to a live party avatar before the zone-rebind timeout";
                    break;
                case FollowRebindFailure.LeftParty:
                    reason = StopReason.TargetLeftParty;
                    detail = "is no longer in your real group after the zone change";
                    break;
                case FollowRebindFailure.IdentityMismatch:
                    reason = StopReason.TargetIdentityMismatch;
                    detail = "rebound to an avatar whose SimPlayerTracking identity did not match";
                    break;
                case FollowRebindFailure.RemoteAuthority:
                    reason = StopReason.TargetRemote;
                    detail = "is controlled by a remote COOP client after the zone change";
                    break;
                default:
                    reason = StopReason.TargetUnavailable;
                    detail = "is unavailable or dead after the zone change";
                    break;
            }
            string name = string.IsNullOrWhiteSpace(TargetName) ? "The follow target" : TargetName;
            Notify("[Erenshor Follow] Following stopped: " + name + " " + detail + ".", "yellow");
            // Once the new scene is stable, explicitly clear any residual player movement/animation state.
            // If the timeout fired while Erenshor is still zoning, leave PlayerControl entirely to the game.
            if (!GameData.Zoning)
            {
                try { _drivenPlayer = GameData.PlayerControl; } catch { _drivenPlayer = null; }
                _drivenController = null;
            }
            Stop(reason);
        }

        internal static bool IsFollowingTarget(SimPlayer sim)
        {
            if (!_active || !_directFollowIntent || sim == null) return false;
            if (_target != null && object.ReferenceEquals(_target, sim)) return true;
            SimPlayerTracking expected = DirectIntent.Identity;
            if (expected == null) return false;
            return FollowRebindPolicy.SameIdentity(expected, SimTrackingRebind.Capture(sim));
        }

        internal static bool CrossZoneContinuationEnabled()
        {
            try
            {
                return ErenshorFollowPlugin.Instance != null &&
                       ErenshorFollowPlugin.Instance.Settings != null &&
                       ErenshorFollowPlugin.Instance.Settings.ExperimentalCrossZoneFollow;
            }
            catch { return false; }
        }

        private static bool UpdateDirectCombatPause()
        {
            if (!_directFollowIntent) return false;

            bool combat = IsDirectCombatActive();
            float clearSeconds = 0f;
            if (_combatPaused && !combat)
            {
                if (_combatClearSince <= 0f) _combatClearSince = Time.time;
                clearSeconds = Math.Max(0f, Time.time - _combatClearSince);
            }

            FollowCombatDecision decision = FollowCombatPolicy.Evaluate(
                true, combat, _combatPaused, clearSeconds);

            if (decision == FollowCombatDecision.PauseForCombat)
            {
                if (!_combatPaused)
                {
                    _combatPaused = true;
                    _combatClearSince = 0f;
                    ResetRouteStateForRebind();
                }
                else
                {
                    _combatClearSince = 0f;
                }
                State = DriveState.PausedForCombat;
                return true;
            }

            if (decision == FollowCombatDecision.RecoveringAfterCombat)
            {
                State = DriveState.RecoveringAfterCombat;
                return true;
            }

            if (_combatPaused)
            {
                _combatPaused = false;
                _combatClearSince = 0f;
                ResetRouteStateAfterRebind();
                State = DriveState.Idle;
            }
            return false;
        }

        private static bool IsLocalPlayerAvailable()
        {
            try
            {
                PlayerControl player = GameData.PlayerControl;
                return player != null && player.Myself != null && player.Myself.Alive;
            }
            catch { return false; }
        }

        private static bool IsDirectCombatActive()
        {
            try { if (GameData.InCombat) return true; } catch { }
            SimPlayer sim = _target;
            if (sim == null) return false;
            try { if (sim.IsSimGroupInCombat()) return true; } catch { }
            try
            {
                NPC npc = sim.MyStats == null || sim.MyStats.Myself == null ? null : sim.MyStats.Myself.MyNPC;
                if (npc != null && npc.CurrentAggroTarget != null) return true;
            }
            catch { }
            return false;
        }

        internal static string DiagnosticsStatus()
        {
            string scene = ActiveScene() ?? "?";
            SimPlayerTracking tracking = DirectIntent.Identity;
            SimPlayer avatar = _target;
            bool avatarPresent = avatar != null;
            bool trackingInGroup = tracking != null && SimTrackingRebind.TrackingIsInPlayerGroup(tracking);
            bool sameTracking = tracking != null && avatar != null && SimTrackingRebind.AvatarMatchesTracking(tracking, avatar);
            bool party = avatar != null && LeaderController.IsPlayerPartySim(avatar);
            bool remote = avatar != null && CoopCompatibility.IsRemoteHuman(avatar);
            bool zoning = false;
            try { zoning = GameData.Zoning; } catch { }
            string pathAge = _lastPathAttemptAt <= 0f ? "never" : Math.Max(0f, Time.time - _lastPathAttemptAt).ToString("F1") + "s";
            string phase = !_active ? "idle" :
                (State == DriveState.AwaitingNativeZoneChange ? "awaiting-native-zone" :
                (_directFollowIntent ? DirectIntent.Phase.ToString() : "owner=lead/expedition"));

            return "[Erenshor Follow] state=" + State +
                   " target=" + (string.IsNullOrWhiteSpace(TargetName) ? "-" : TargetName) +
                   " tracking=" + (tracking == null ? "no" : "yes") +
                   " avatar=" + (avatarPresent ? "yes" : "no") +
                   " identity=" + (tracking == null || avatar == null ? "pending" : (sameTracking ? "match" : "MISMATCH")) +
                   " party=" + (tracking == null && avatar == null ? "n/a" : ((trackingInGroup || party) ? "yes" : "no")) +
                   " authority=" + (avatar == null ? "n/a" : (remote ? "remote" : "local")) +
                   " scene=" + scene +
                   " zoning=" + (zoning ? "yes" : "no") +
                   " phase=" + phase +
                   " lastRepath=" + pathAge +
                   " recovery=" + _recoveryAttempts + "/" + FollowStuckRecoveryPolicy.MaxRepathAttempts +
                   " crossZone=" + (CrossZoneContinuationEnabled() ? "ON" : "OFF") +
                   " stop=" + LastStopReason;
        }

        private static void ResetRouteStateForRebind()
        {
            _nextPathTime = 0f;
            _hasWaypoint = false;
            _hasNextCorner = false;
            _waitingAtTarget = false;
            _lastProgressPosition = Vector3.zero;
            _lastTargetDistance = float.MaxValue;
            _lastProgressTime = 0f;
            _routeFailureSince = 0f;
            _lastPartialEndpoint = Vector3.zero;
            _hasPartialEndpoint = false;
            _recoveryAttempts = 0;
            _nextRecoveryAt = 0f;
        }

        private static void ResetRouteStateAfterRebind()
        {
            ResetRouteStateForRebind();
        }

        // Cached per-frame: this is called once from Tick() and again from TryDrive() in the same
        // frame. Follow owns only verified local-party Sims; an unloaded/dead, remote-authority, or
        // no-longer-party target fails closed instead of continuing against a stale actor reference.
        private static bool IsTargetValid()
        {
            int frame = Time.frameCount;
            if (_targetValidFrame != frame)
            {
                _targetValidFrame = frame;
                bool usable = IsUsableSim(_target);
                bool remote = usable && CoopCompatibility.IsRemoteHuman(_target);
                bool party = usable && !remote && LeaderController.IsPlayerPartySim(_target);
                _targetEligibilityCache = FollowActorEligibilityPolicy.Evaluate(usable, remote, party);
                _targetValidCache = _targetEligibilityCache == FollowActorEligibility.Eligible;
            }
            return _targetValidCache;
        }

        private static void StopInvalidTarget()
        {
            StopReason reason = StopReason.TargetUnavailable;
            string detail = "is unavailable or dead";
            if (_directFollowIntent && DirectIntent.Identity != null &&
                !SimTrackingRebind.TrackingIsInPlayerGroup(DirectIntent.Identity))
            {
                reason = StopReason.TargetLeftParty;
                detail = "is no longer in your current party";
            }
            else if (_targetEligibilityCache == FollowActorEligibility.RemoteAuthority)
            {
                reason = StopReason.TargetRemote;
                detail = "is controlled by a remote COOP client";
            }
            else if (_targetEligibilityCache == FollowActorEligibility.LeftParty)
            {
                reason = StopReason.TargetLeftParty;
                detail = "is no longer in your current party";
            }
            if (_directFollowIntent)
            {
                string name = string.IsNullOrWhiteSpace(TargetName) ? "The follow target" : TargetName;
                Notify("[Erenshor Follow] Following stopped: " + name + " " + detail + ".", "yellow");
            }
            Stop(reason);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static void ResetProgress(Vector3 position, float targetDistance)
        {
            _lastProgressPosition = position;
            _lastTargetDistance = targetDistance;
            _lastProgressTime = Time.time;
            _routeFailureSince = 0f;
            _recoveryAttempts = 0;
            _nextRecoveryAt = 0f;
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

        private static void RestoreMovementState()
        {
            PlayerControl player = _drivenPlayer;
            if (player == null) return;
            try
            {
                CharacterController controller = _drivenController != null ? _drivenController : player.GetComponent<CharacterController>();
                if (controller != null) controller.SimpleMove(Vector3.zero);
            }
            catch { }
            SetMoving(player, false);
            try { player.UpdateAnimRun(); } catch { }
        }

        private static void Notify(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
        }

        // "moving" is a private field on PlayerControl, so a reflection write is unavoidable, but
        // the FieldInfo lookup itself is cached instead of repeating it on every driven frame.
        private static FieldInfo _movingField;
        private static bool _movingFieldResolved;

        private static void SetMoving(PlayerControl player, bool moving)
        {
            try
            {
                if (!_movingFieldResolved)
                {
                    _movingField = player.GetType().GetField("moving", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _movingFieldResolved = true;
                }
                if (_movingField != null) _movingField.SetValue(player, moving);
            }
            catch { }
            try
            {
                Animator animator = player.Myself == null ? null : player.Myself.GetMyAnim();
                if (animator != null) animator.SetBool("Walking", moving);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "LandMovement")]
    internal static class FollowMovementPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerControl __instance)
        {
            try
            {
                if (!FollowController.Active) return true;
                return !FollowController.TryDrive(__instance);
            }
            catch
            {
                FollowController.Stop();
                return true;
            }
        }
    }
}
