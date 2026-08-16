using System;

namespace ErenshorFollow
{
    internal enum FollowStuckRecoveryDecision
    {
        None,
        Repath,
        Stop
    }

    // Unity-free bounded recovery policy for ordinary direct Follow. Runtime code owns the actual
    // NavMesh query; this policy only decides when another query is justified and when to fail closed.
    internal static class FollowStuckRecoveryPolicy
    {
        internal const float RouteProblemRecoverySeconds = 2.0f;
        internal const float NoProgressRecoverySeconds = 3.0f;
        internal const float RecoveryRetrySeconds = 1.5f;
        internal const float StopAfterSeconds = 9.0f;
        internal const int MaxRepathAttempts = 3;

        internal static FollowStuckRecoveryDecision Evaluate(
            float noProgressSeconds,
            bool routeProblem,
            int recoveryAttempts,
            bool retryDue)
        {
            if (float.IsNaN(noProgressSeconds) || float.IsInfinity(noProgressSeconds) || noProgressSeconds < 0f)
                noProgressSeconds = 0f;
            if (recoveryAttempts < 0) recoveryAttempts = 0;

            float firstRecoveryAt = routeProblem ? RouteProblemRecoverySeconds : NoProgressRecoverySeconds;

            if (noProgressSeconds >= StopAfterSeconds && recoveryAttempts >= MaxRepathAttempts)
                return FollowStuckRecoveryDecision.Stop;

            if (noProgressSeconds >= firstRecoveryAt &&
                recoveryAttempts < MaxRepathAttempts &&
                retryDue)
                return FollowStuckRecoveryDecision.Repath;

            return FollowStuckRecoveryDecision.None;
        }
    }
}
