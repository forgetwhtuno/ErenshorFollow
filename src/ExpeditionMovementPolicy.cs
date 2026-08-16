namespace ErenshorFollow
{
    internal enum ExpeditionMovementDecision
    {
        Waiting,
        ProgressObserved,
        ReissueNativeOrder,
        TryNextRouteCandidate,
        FailMovementOwnership
    }

    internal enum ExpeditionMovementIssue
    {
        None,
        CombatControl,
        NpcUnavailable,
        AgentUnavailable,
        AgentDisabled,
        AgentOffNavMesh,
        AgentStopped,
        DestinationNotApplied,
        PathInvalid,
        NoProgress
    }

    internal struct ExpeditionMovementObservation
    {
        internal bool CombatControl;
        internal bool NpcResolved;
        internal bool AgentPresent;
        internal bool AgentEnabled;
        internal bool AgentOnNavMesh;
        internal bool AgentStopped;
        internal bool PathPending;
        internal bool HasPath;
        internal bool PathInvalid;
        internal bool DestinationAccepted;
        internal float ElapsedSeconds;
        internal float MovedDistance;
        internal float DistanceImprovement;
        internal float VelocityMagnitude;
        internal int ReissueCount;
    }

    // Pure startup/ownership policy. Complete NavMesh preflight proves geometry only; this policy
    // separately proves that the selected Sim's native movement controller actually accepted and began
    // executing the expedition order. Candidate geometry is retried only for an invalid path. A stopped,
    // missing, or non-executing movement owner fails as an ownership problem instead of cycling through
    // every otherwise-identical approach point.
    internal static class ExpeditionMovementPolicy
    {
        internal const float ObservationGraceSeconds = 1.25f;
        internal const float FailureSeconds = 4.75f;
        internal const float ProgressDistance = 0.45f;
        internal const float ProgressImprovement = 0.35f;
        internal const float MovingVelocity = 0.10f;
        internal const int MaximumReissues = 2;

        internal static ExpeditionMovementDecision Evaluate(ExpeditionMovementObservation observation,
            out ExpeditionMovementIssue issue)
        {
            issue = ExpeditionMovementIssue.None;
            if (observation.CombatControl)
            {
                issue = ExpeditionMovementIssue.CombatControl;
                return ExpeditionMovementDecision.Waiting;
            }

            if (observation.MovedDistance >= ProgressDistance ||
                observation.DistanceImprovement >= ProgressImprovement ||
                observation.VelocityMagnitude >= MovingVelocity)
                return ExpeditionMovementDecision.ProgressObserved;

            if (observation.ElapsedSeconds < ObservationGraceSeconds)
                return ExpeditionMovementDecision.Waiting;

            if (observation.PathInvalid)
            {
                issue = ExpeditionMovementIssue.PathInvalid;
                return ExpeditionMovementDecision.TryNextRouteCandidate;
            }

            if (!observation.NpcResolved)
            {
                issue = ExpeditionMovementIssue.NpcUnavailable;
                return ExpeditionMovementDecision.FailMovementOwnership;
            }

            if (observation.AgentPresent)
            {
                if (!observation.AgentEnabled)
                {
                    issue = ExpeditionMovementIssue.AgentDisabled;
                    return ExpeditionMovementDecision.FailMovementOwnership;
                }
                if (!observation.AgentOnNavMesh)
                {
                    issue = ExpeditionMovementIssue.AgentOffNavMesh;
                    return ExpeditionMovementDecision.FailMovementOwnership;
                }
            }

            if (observation.ElapsedSeconds < FailureSeconds && observation.ReissueCount < MaximumReissues)
            {
                if (observation.AgentPresent && observation.AgentStopped)
                    issue = ExpeditionMovementIssue.AgentStopped;
                else if (observation.AgentPresent && !observation.PathPending && !observation.HasPath &&
                         !observation.DestinationAccepted)
                    issue = ExpeditionMovementIssue.DestinationNotApplied;
                else if (!observation.AgentPresent)
                    issue = ExpeditionMovementIssue.AgentUnavailable;
                else
                    issue = ExpeditionMovementIssue.NoProgress;
                return ExpeditionMovementDecision.ReissueNativeOrder;
            }

            if (observation.AgentPresent && observation.AgentStopped)
                issue = ExpeditionMovementIssue.AgentStopped;
            else if (observation.AgentPresent && !observation.DestinationAccepted)
                issue = ExpeditionMovementIssue.DestinationNotApplied;
            else if (!observation.AgentPresent)
                issue = ExpeditionMovementIssue.AgentUnavailable;
            else
                issue = ExpeditionMovementIssue.NoProgress;
            return ExpeditionMovementDecision.FailMovementOwnership;
        }

        internal static string Describe(ExpeditionMovementIssue issue)
        {
            switch (issue)
            {
                case ExpeditionMovementIssue.CombatControl: return "native combat has movement priority";
                case ExpeditionMovementIssue.NpcUnavailable: return "the selected Sim has no resolvable native NPC movement owner";
                case ExpeditionMovementIssue.AgentUnavailable: return "native NPC order exists but NavMeshAgent telemetry is unavailable";
                case ExpeditionMovementIssue.AgentDisabled: return "the native NavMeshAgent is disabled";
                case ExpeditionMovementIssue.AgentOffNavMesh: return "the native NavMeshAgent is not on the NavMesh";
                case ExpeditionMovementIssue.AgentStopped: return "the native NavMeshAgent remains stopped";
                case ExpeditionMovementIssue.DestinationNotApplied: return "the native NavMeshAgent did not retain the expedition destination";
                case ExpeditionMovementIssue.PathInvalid: return "the native NavMeshAgent reported PathInvalid";
                case ExpeditionMovementIssue.NoProgress: return "the native movement owner made no useful progress";
                default: return "native movement is pending";
            }
        }
    }
}
