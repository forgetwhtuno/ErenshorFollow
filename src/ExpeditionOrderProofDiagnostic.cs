using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    // READ-ONLY one-shot observer at the existing planner -> movement-order boundary. It never owns
    // movement and it never calls a Sim/NPC movement API; all NavMesh operations are queries only.
    internal static class ExpeditionOrderProofDiagnostic
    {
        private const float MoverSampleRadius = 2f;
        private static readonly NavMeshPath ProbePath = new NavMeshPath();
        private static int _nextOrderId = 1;

        internal static ExpeditionOrderProofPolicy.Record Begin(SimPlayer leader, Vector3 target,
            LocalZoneRoutePlanner.RouteOption option, string destinationZone, bool ownershipBefore)
        {
            ExpeditionOrderProofPolicy.Record r = new ExpeditionOrderProofPolicy.Record();
            try
            {
                r.Session = ExpeditionPhaseTelemetry.CurrentSessionId;
                r.Order = _nextOrderId++;
                r.Scene = SceneManager.GetActiveScene().name;
                r.Leader = leader == null ? "unavailable" : FollowController.ReadName(leader);
                r.DestinationZone = destinationZone;
                r.CrossingKey = option == null ? "unavailable" : option.StableKey;
                r.CandidateId = option == null ? "unavailable" : option.StableKey;
                RouteCandidatePolicy.Evaluation evaluation = option == null ? null : option.Evaluation;
                RouteCandidatePolicy.Candidate candidate = evaluation == null ? null : evaluation.Candidate;
                r.SelectedSeed = candidate == null ? "unavailable" : candidate.SeedLabel;
                r.PlannerTarget = LocalZoneRoutePlanner.FormatVector(target);
                r.PlannerSample = option == null ? "unavailable" : LocalZoneRoutePlanner.FormatVector(option.Approach);
                r.PlannerPath = candidate == null ? "unavailable" : candidate.Path.ToString();
                r.PlannerCorners = candidate == null ? 0 : candidate.CornerCount;
                r.PlannerResult = evaluation == null ? "unavailable" : evaluation.Acceptance.ToString();
                r.PlannerAccepted = evaluation != null && evaluation.Accepted;
                r.PlannerReason = evaluation == null ? "unavailable" : evaluation.Reason;
                r.MovementOwnershipBefore = ownershipBefore;
                r.MoverRawTarget = LocalZoneRoutePlanner.FormatVector(target);
                r.HighPriorityNavTarget = LocalZoneRoutePlanner.FormatVector(target);
                r.FailureStage = "none";
                r.FailureReason = "none";
            }
            catch (Exception ex)
            {
                r.FailureStage = "diagnostic_begin";
                r.FailureReason = "exception:" + ex.GetType().Name;
            }
            return r;
        }

        internal static void ProbeMover(ExpeditionOrderProofPolicy.Record r, NPC npc, Vector3 target)
        {
            if (r == null) return;
            try
            {
                if (npc == null) { r.FailureStage = "mover_probe"; r.FailureReason = "npc_owner_missing"; return; }
                NavMeshHit targetHit;
                r.Mover2mSample = NavMesh.SamplePosition(target, out targetHit, MoverSampleRadius, NavMesh.AllAreas);
                if (!r.Mover2mSample) { r.FailureStage = "mover_probe"; r.FailureReason = "mover_sample_2m_failed"; return; }
                r.MoverSample = LocalZoneRoutePlanner.FormatVector(targetHit.position);
                NavMeshPath path = ProbePath;
                bool calculated = NavMesh.CalculatePath(npc.transform.position, targetHit.position, NavMesh.AllAreas, path);
                r.MoverPath = ExpeditionOrderProofPolicy.PathName(true, calculated, calculated ? path.status.ToString() : null);
                r.MoverCorners = path.corners == null ? 0 : path.corners.Length;
                if (r.MoverCorners > 0)
                {
                    Vector3 endpoint = path.corners[r.MoverCorners - 1];
                    r.MoverEndpoint = LocalZoneRoutePlanner.FormatVector(endpoint);
                    r.MoverEndpointDistanceToTarget = Vector3.Distance(endpoint, targetHit.position);
                }
                if (!calculated || path.status == NavMeshPathStatus.PathInvalid)
                {
                    r.FailureStage = "mover_probe";
                    r.FailureReason = "mover_path_invalid";
                }
                else if (path.status == NavMeshPathStatus.PathPartial)
                {
                    r.FailureStage = "mover_probe";
                    r.FailureReason = "mover_path_partial_complete_required";
                }
            }
            catch (Exception ex)
            {
                r.FailureStage = "mover_probe";
                r.FailureReason = "exception:" + ex.GetType().Name;
            }
        }

        internal static void Complete(ExpeditionOrderProofPolicy.Record r, bool ownershipAfter,
            bool attempted, bool issued, string failureStage, string failureReason)
        {
            try
            {
                if (r == null) return;
                r.MovementOwnershipAfter = ownershipAfter;
                r.OrderAttempted = attempted;
                r.OrderIssued = issued;
                if (!string.IsNullOrWhiteSpace(failureStage)) r.FailureStage = failureStage;
                if (!string.IsNullOrWhiteSpace(failureReason)) r.FailureReason = failureReason;
                if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogInfo(ExpeditionOrderProofPolicy.Format(r));
            }
            catch (Exception ex)
            {
                try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogError("[Expedition order proof] diagnostic_error=" + ex.GetType().Name); }
                catch { }
            }
        }
    }
}
