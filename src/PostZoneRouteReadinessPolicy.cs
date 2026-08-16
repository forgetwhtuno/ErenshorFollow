namespace ErenshorFollow
{
    // Pure bound for the post-zone geometry readiness probe.  A freshly rebound SimPlayer proves
    // identity only; it does not prove that this scene's Zonelines and NavMesh have finished
    // registering.  The runtime supplies freshly observed facts on each bounded probe.
    internal enum PostZoneRouteReadinessDecision { Waiting, Ready, Failed }

    internal struct PostZoneRouteReadinessInputs
    {
        internal float ElapsedSeconds;
        internal int AttemptCount;
        internal bool AtlasRouteAvailable;
        internal bool NextLegResolved;
        internal int LiveCrossingCount;
        internal bool StartSampled;
        internal int AcceptedCandidateCount;
    }

    internal static class PostZoneRouteReadinessPolicy
    {
        internal const float ProbeIntervalSeconds = 0.50f;
        internal const float TimeoutSeconds = 8.0f;
        internal const int MaximumAttempts = 16;

        internal static PostZoneRouteReadinessDecision Evaluate(PostZoneRouteReadinessInputs input)
        {
            if (input.AtlasRouteAvailable && input.NextLegResolved && input.LiveCrossingCount > 0 &&
                input.StartSampled && input.AcceptedCandidateCount > 0)
                return PostZoneRouteReadinessDecision.Ready;
            if (input.ElapsedSeconds >= TimeoutSeconds || input.AttemptCount >= MaximumAttempts)
                return PostZoneRouteReadinessDecision.Failed;
            return PostZoneRouteReadinessDecision.Waiting;
        }

        internal static string DescribePending(PostZoneRouteReadinessInputs input)
        {
            if (!input.AtlasRouteAvailable) return "atlas route has no currently discovered live first hop";
            if (!input.NextLegResolved) return "expected next Zoneline is not currently resolved";
            if (input.LiveCrossingCount <= 0) return "expected next Zoneline has no active non-party-removing crossing";
            if (!input.StartSampled) return "leader position is not yet sampled onto the current NavMesh";
            if (input.AcceptedCandidateCount <= 0) return "no current crossing approach passed NavMesh policy";
            return "route readiness is pending";
        }
    }
}
