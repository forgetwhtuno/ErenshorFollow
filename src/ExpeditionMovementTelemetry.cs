using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorFollow
{
    // Repair-build movement telemetry. Runtime sampling itself is allocation-free on ordinary frames;
    // strings/reflection-heavy details are built only at explicit boundaries, meaningful state changes,
    // or the one-second stalled heartbeat.
    internal static class ExpeditionMovementTelemetry
    {
        private const float StallHeartbeatSeconds = 1f;

        private static readonly FieldInfo RandomizeOffsetField = AccessTools.Field(typeof(SimPlayer), "randomizeOffset");
        private static readonly FieldInfo RandomizeActionsField = AccessTools.Field(typeof(SimPlayer), "randomizeActions");
        private static readonly FieldInfo PullPhaseField = AccessTools.Field(typeof(SimPlayer), "CurrentPullPhase");
        private static readonly FieldInfo SimUrgentNavField = AccessTools.Field(typeof(SimPlayer), "UrgentNavUpdate");
        private static readonly FieldInfo NpcUrgentNavField = AccessTools.Field(typeof(NPC), "UrgentNavUpdate");

        private static bool _haveSample;
        private static Vector3 _lastPosition;
        private static float _lastSampleTime;
        private static bool _lastStopped;
        private static bool _lastHasPath;
        private static bool _lastPending;
        private static int _lastPathStatus;
        private static float _lastSpeed;
        private static bool _lastWalking;
        private static bool _lastPatrol;
        private static int _lastOrderGeneration;
        private static int _lastOwnerGeneration;
        private static string _lastPhase;
        private static string _lastWriter;
        private static float _lastStallHeartbeat;

        internal static void Tick(SimPlayer leader, Vector3 order, bool orderValid, string phase,
            bool travelOwned, bool combat, bool held, bool regrouping, bool paused, bool crossing,
            int ownerGeneration, int orderGeneration, string lastWriter, int lastWriterGeneration, float lastWriterTime)
        {
            if (!ExpeditionTelemetryPolicy.EmitMovement(ErenshorFollowPlugin.VerboseDiagnostics))
            {
                ResetSamples();
                return;
            }
            if (leader == null)
            {
                ResetSamples();
                return;
            }

            NavMeshAgent nav = ResolveAgent(leader);
            Animator animator = ResolveAnimator(leader);
            Vector3 position = leader.transform.position;
            float now = Time.time;
            float delta = _haveSample ? HorizontalDistance(position, _lastPosition) : 0f;
            float sampleSeconds = _haveSample ? Math.Max(0f, now - _lastSampleTime) : 0f;
            bool stopped = ReadStopped(nav);
            bool hasPath = ReadHasPath(nav);
            bool pending = ReadPending(nav);
            int pathStatus = ReadPathStatus(nav);
            float speed = ReadSpeed(nav);
            float velocity = ReadVelocity(nav);
            float desired = ReadDesiredVelocity(nav);
            bool walking = ReadKnownAnimatorBool(animator, "Walking");
            bool patrol = ReadKnownAnimatorBool(animator, "Patrol");
            float distance = orderValid ? HorizontalDistance(position, order) : 0f;

            bool stateChanged = !_haveSample || stopped != _lastStopped || hasPath != _lastHasPath ||
                pending != _lastPending || pathStatus != _lastPathStatus || Math.Abs(speed - _lastSpeed) > 0.05f ||
                walking != _lastWalking || patrol != _lastPatrol || ownerGeneration != _lastOwnerGeneration ||
                orderGeneration != _lastOrderGeneration || !string.Equals(phase, _lastPhase, StringComparison.Ordinal) ||
                !string.Equals(lastWriter, _lastWriter, StringComparison.Ordinal);

            bool stalled = travelOwned && orderValid && !combat && !held && !regrouping && !paused && !crossing &&
                distance > 0.5f && delta < 0.03f && velocity < ExpeditionMovementOwnershipPolicy.LocomotionSpeedThreshold &&
                desired < ExpeditionMovementOwnershipPolicy.LocomotionSpeedThreshold;
            bool heartbeat = stalled && (now - _lastStallHeartbeat >= StallHeartbeatSeconds);

            if (stateChanged || heartbeat)
            {
                string reason = heartbeat && !stateChanged ? "stalled_heartbeat" : "state_change";
                Emit(reason, leader, order, orderValid, phase, travelOwned, combat, held, regrouping, paused,
                    crossing, ownerGeneration, orderGeneration, lastWriter, lastWriterGeneration, lastWriterTime,
                    position, delta, sampleSeconds, nav, animator);
                if (heartbeat) _lastStallHeartbeat = now;
            }

            _haveSample = true;
            _lastPosition = position;
            _lastSampleTime = now;
            _lastStopped = stopped;
            _lastHasPath = hasPath;
            _lastPending = pending;
            _lastPathStatus = pathStatus;
            _lastSpeed = speed;
            _lastWalking = walking;
            _lastPatrol = patrol;
            _lastOwnerGeneration = ownerGeneration;
            _lastOrderGeneration = orderGeneration;
            _lastPhase = phase;
            _lastWriter = lastWriter;
        }

        internal static void RecordBoundary(string boundary, SimPlayer leader, Vector3 order, bool orderValid,
            string phase, bool travelOwned, bool combat, bool held, bool regrouping, bool paused, bool crossing,
            int ownerGeneration, int orderGeneration, string lastWriter, int lastWriterGeneration, float lastWriterTime)
        {
            if (!ExpeditionTelemetryPolicy.EmitMovement(ErenshorFollowPlugin.VerboseDiagnostics)) return;
            if (leader == null) return;
            Vector3 position = leader.transform.position;
            float now = Time.time;
            float delta = _haveSample ? HorizontalDistance(position, _lastPosition) : 0f;
            float seconds = _haveSample ? Math.Max(0f, now - _lastSampleTime) : 0f;
            Emit(boundary, leader, order, orderValid, phase, travelOwned, combat, held, regrouping, paused,
                crossing, ownerGeneration, orderGeneration, lastWriter, lastWriterGeneration, lastWriterTime,
                position, delta, seconds, ResolveAgent(leader), ResolveAnimator(leader));
        }

        internal static void Reset()
        {
            ResetSamples();
            _lastStallHeartbeat = 0f;
        }

        private static void ResetSamples()
        {
            _haveSample = false;
            _lastPosition = Vector3.zero;
            _lastSampleTime = 0f;
            _lastStopped = false;
            _lastHasPath = false;
            _lastPending = false;
            _lastPathStatus = -1;
            _lastSpeed = 0f;
            _lastWalking = false;
            _lastPatrol = false;
            _lastOrderGeneration = 0;
            _lastOwnerGeneration = 0;
            _lastPhase = null;
            _lastWriter = null;
        }

        private static void Emit(string reason, SimPlayer leader, Vector3 order, bool orderValid, string phase,
            bool travelOwned, bool combat, bool held, bool regrouping, bool paused, bool crossing,
            int ownerGeneration, int orderGeneration, string lastWriter, int lastWriterGeneration, float lastWriterTime,
            Vector3 position, float delta, float sampleSeconds, NavMeshAgent nav, Animator animator)
        {
            try
            {
                StringBuilder text = new StringBuilder(768);
                text.Append("[Expedition movement] reason=").Append(Safe(reason));
                text.Append(" phase=").Append(Safe(phase));
                text.Append(" leader=").Append(Safe(FollowController.ReadName(leader)));
                text.Append(" tracking=").Append(TrackingIdentity(leader));
                text.Append(" instance=").Append(leader.GetInstanceID());
                text.Append(" pos=").Append(Vector(position));
                text.Append(" delta=").Append(delta.ToString("F2"));
                text.Append(" dt=").Append(sampleSeconds.ToString("F2"));
                text.Append(" distanceToOrder=").Append(orderValid ? HorizontalDistance(position, order).ToString("F2") : "n/a");
                text.Append(" ownerGen=").Append(ownerGeneration);
                text.Append(" orderGen=").Append(orderGeneration);
                text.Append(" lastWriter=").Append(Safe(lastWriter));
                text.Append(" writerGen=").Append(lastWriterGeneration);
                text.Append(" writerAge=").Append(lastWriterTime > 0f ? Math.Max(0f, Time.time - lastWriterTime).ToString("F2") : "n/a");
                text.Append(" travelOwned=").Append(travelOwned);

                AppendAgent(text, nav, order, orderValid);
                AppendNativeSimState(text, leader, nav, animator);
                text.Append(" combat=").Append(combat);
                text.Append(" held=").Append(held);
                text.Append(" regroup=").Append(regrouping);
                text.Append(" paused=").Append(paused);
                text.Append(" crossing=").Append(crossing);
                text.Append(" zoning=").Append(GameData.Zoning);

                if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogInfo(text.ToString());
            }
            catch { }
        }

        private static void AppendAgent(StringBuilder text, NavMeshAgent nav, Vector3 order, bool orderValid)
        {
            if (nav == null)
            {
                text.Append(" agent=missing");
                return;
            }
            try
            {
                text.Append(" agent=present");
                text.Append(" enabled=").Append(nav.enabled);
                text.Append(" onMesh=").Append(nav.isOnNavMesh);
                text.Append(" speed=").Append(nav.speed.ToString("F2"));
                text.Append(" stopped=").Append(nav.isStopped);
                text.Append(" velocity=").Append(nav.velocity.magnitude.ToString("F2"));
                text.Append(" desiredVelocity=").Append(nav.desiredVelocity.magnitude.ToString("F2"));
                text.Append(" hasPath=").Append(nav.hasPath);
                text.Append(" pending=").Append(nav.pathPending);
                text.Append(" pathStatus=").Append(nav.pathStatus);
                if (nav.enabled && nav.isOnNavMesh)
                {
                    text.Append(" destination=").Append(Vector(nav.destination));
                    if (orderValid) text.Append(" destinationFromOrder=").Append(HorizontalDistance(nav.destination, order).ToString("F2"));
                    text.Append(" remaining=").Append(SafeFloat(nav.remainingDistance));
                }
            }
            catch (Exception ex) { text.Append(" agentRead=").Append(ex.GetType().Name); }
        }

        private static void AppendNativeSimState(StringBuilder text, SimPlayer leader, NavMeshAgent nav, Animator animator)
        {
            try
            {
                text.Append(" GuardSpot=").Append(leader.GuardSpot);
                text.Append(" GuardPos=").Append(Vector(leader.GetGuardPos()));
            }
            catch { text.Append(" GuardState=unreadable"); }

            text.Append(" randomizeOffset=").Append(ReadVector(RandomizeOffsetField, leader));
            text.Append(" randomizeActions=").Append(ReadValue(RandomizeActionsField, leader));
            NPC npc = ResolveNpc(leader);
            object urgentOwner = SimUrgentNavField != null ? (object)leader : (object)npc;
            FieldInfo urgentField = SimUrgentNavField ?? NpcUrgentNavField;
            text.Append(" UrgentNavUpdate=").Append(ReadValue(urgentField, urgentOwner));
            text.Append(" Walking=").Append(ReadAnimatorState(animator, "Walking"));
            text.Append(" Patrol=").Append(ReadAnimatorState(animator, "Patrol"));
            text.Append(" casting=").Append(ReadAnimatorState(animator, "Casting"));
            text.Append(" sitting=").Append(ReadAnimatorState(animator, "Sitting"));
            text.Append(" pullPhase=").Append(ReadValue(PullPhaseField, leader));
            text.Append(" task=unknown(no verified readable field)");
        }

        private static string ReadAnimatorState(Animator animator, string name)
        {
            bool value;
            return TryAnimatorBool(animator, name, out value) ? value.ToString() : "unknown";
        }

        private static bool ReadKnownAnimatorBool(Animator animator, string name)
        {
            if (animator == null) return false;
            try { return animator.GetBool(name); }
            catch { return false; }
        }

        private static bool TryAnimatorBool(Animator animator, string name, out bool value)
        {
            value = false;
            if (animator == null) return false;
            try
            {
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].type == AnimatorControllerParameterType.Bool &&
                        string.Equals(parameters[i].name, name, StringComparison.Ordinal))
                    {
                        value = animator.GetBool(name);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static NavMeshAgent ResolveAgent(SimPlayer leader)
        {
            NPC npc = ResolveNpc(leader);
            try
            {
                NavMeshAgent fromNpc = npc == null ? null : npc.GetComponent<NavMeshAgent>();
                if (fromNpc != null) return fromNpc;
            }
            catch { }
            try { return leader == null ? null : leader.GetComponent<NavMeshAgent>(); }
            catch { return null; }
        }

        private static NPC ResolveNpc(SimPlayer leader)
        {
            if (leader == null) return null;
            try
            {
                NPC npc = leader.GetThisNPC();
                if (npc != null) return npc;
            }
            catch { }
            try { return leader.MyStats == null || leader.MyStats.Myself == null ? null : leader.MyStats.Myself.MyNPC; }
            catch { return null; }
        }

        private static Animator ResolveAnimator(SimPlayer leader)
        {
            try
            {
                if (leader != null && leader.MyStats != null && leader.MyStats.Myself != null)
                    return leader.MyStats.Myself.GetMyAnim();
            }
            catch { }
            return null;
        }

        private static bool ReadStopped(NavMeshAgent nav) { try { return nav != null && nav.isStopped; } catch { return false; } }
        private static bool ReadHasPath(NavMeshAgent nav) { try { return nav != null && nav.hasPath; } catch { return false; } }
        private static bool ReadPending(NavMeshAgent nav) { try { return nav != null && nav.pathPending; } catch { return false; } }
        private static int ReadPathStatus(NavMeshAgent nav) { try { return nav == null ? -1 : (int)nav.pathStatus; } catch { return -1; } }
        private static float ReadSpeed(NavMeshAgent nav) { try { return nav == null ? 0f : nav.speed; } catch { return 0f; } }
        private static float ReadVelocity(NavMeshAgent nav) { try { return nav == null ? 0f : nav.velocity.magnitude; } catch { return 0f; } }
        private static float ReadDesiredVelocity(NavMeshAgent nav) { try { return nav == null ? 0f : nav.desiredVelocity.magnitude; } catch { return 0f; } }

        private static string TrackingIdentity(SimPlayer leader)
        {
            try
            {
                SimPlayerTracking tracking = leader == null ? null : leader.MySimTracking;
                return tracking == null ? "none" : RuntimeHelpers.GetHashCode(tracking).ToString();
            }
            catch { return "unreadable"; }
        }

        private static string ReadValue(FieldInfo field, object target)
        {
            if (field == null || target == null) return "unavailable";
            try
            {
                object value = field.GetValue(target);
                return value == null ? "null" : Safe(Convert.ToString(value));
            }
            catch { return "unreadable"; }
        }

        private static string ReadVector(FieldInfo field, object target)
        {
            if (field == null || target == null) return "unavailable";
            try
            {
                object value = field.GetValue(target);
                return value is Vector3 ? Vector((Vector3)value) : (value == null ? "null" : Safe(Convert.ToString(value)));
            }
            catch { return "unreadable"; }
        }

        private static string SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? "unreadable" : value.ToString("F2");
        }

        private static string Vector(Vector3 value)
        {
            return "(" + value.x.ToString("F2") + "," + value.y.ToString("F2") + "," + value.z.ToString("F2") + ")";
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            string result = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return result.Length <= 96 ? result : result.Substring(0, 96);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
