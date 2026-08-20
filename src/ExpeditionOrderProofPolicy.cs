using System;
using System.Globalization;

namespace ErenshorFollow
{
    // Pure formatter/classifier for the bounded, read-only planner-to-mover observation.
    // It deliberately has no Unity, Sim, NPC, or movement authority dependency.
    internal static class ExpeditionOrderProofPolicy
    {
        internal static string PathName(bool sampled, bool calculated, string status)
        {
            if (!sampled || !calculated || string.IsNullOrWhiteSpace(status)) return "Invalid";
            return status;
        }

        internal static string Failure(string stage, string reason)
        {
            string safeStage = string.IsNullOrWhiteSpace(stage) ? "unavailable" : stage.Trim();
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "none" : reason.Trim();
            return safeStage + ":" + safeReason;
        }

        internal static string Format(Record r)
        {
            if (r == null) return "[Expedition order proof] diagnostic_error=missing_record";
            return "[Expedition order proof] session=" + r.Session +
                " order=" + r.Order +
                " scene=" + Safe(r.Scene) +
                " leader=" + Safe(r.Leader) +
                " destinationZone=" + Safe(r.DestinationZone) +
                " crossingKey=" + Safe(r.CrossingKey) +
                " candidateId=" + Safe(r.CandidateId) +
                " selectedSeed=" + Safe(r.SelectedSeed) +
                " plannerTarget=" + Safe(r.PlannerTarget) +
                " plannerSample=" + Safe(r.PlannerSample) +
                " plannerPath=" + Safe(r.PlannerPath) +
                " plannerCorners=" + r.PlannerCorners +
                " plannerResult=" + Safe(r.PlannerResult) +
                " plannerAccepted=" + r.PlannerAccepted +
                " plannerReason=" + Safe(r.PlannerReason) +
                " movementOwnershipBefore=" + r.MovementOwnershipBefore +
                " movementOwnershipAfter=" + r.MovementOwnershipAfter +
                " moverRawTarget=" + Safe(r.MoverRawTarget) +
                " mover2mSample=" + r.Mover2mSample +
                " moverSample=" + Safe(r.MoverSample) +
                " moverPath=" + Safe(r.MoverPath) +
                " moverCorners=" + r.MoverCorners +
                " moverEndpoint=" + Safe(r.MoverEndpoint) +
                " moverEndpointDistanceToTarget=" + r.MoverEndpointDistanceToTarget.ToString("F2", CultureInfo.InvariantCulture) +
                " orderAttempted=" + r.OrderAttempted +
                " orderIssued=" + r.OrderIssued +
                " highPriorityNavTarget=" + Safe(r.HighPriorityNavTarget) +
                " failureStage=" + Safe(r.FailureStage) +
                " failureReason=" + Safe(r.FailureReason);
        }

        private static string Safe(string value) { return string.IsNullOrWhiteSpace(value) ? "unavailable" : value.Trim(); }

        internal sealed class Record
        {
            internal int Session;
            internal int Order;
            internal string Scene;
            internal string Leader;
            internal string DestinationZone;
            internal string CrossingKey;
            internal string CandidateId;
            internal string SelectedSeed;
            internal string PlannerTarget;
            internal string PlannerSample;
            internal string PlannerPath;
            internal int PlannerCorners;
            internal string PlannerResult;
            internal bool PlannerAccepted;
            internal string PlannerReason;
            internal bool MovementOwnershipBefore;
            internal bool MovementOwnershipAfter;
            internal string MoverRawTarget;
            internal bool Mover2mSample;
            internal string MoverSample;
            internal string MoverPath;
            internal int MoverCorners;
            internal string MoverEndpoint;
            internal float MoverEndpointDistanceToTarget = float.NaN;
            internal bool OrderAttempted;
            internal bool OrderIssued;
            internal string HighPriorityNavTarget;
            internal string FailureStage;
            internal string FailureReason;
        }
    }
}
