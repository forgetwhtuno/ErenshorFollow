using System;

namespace ErenshorFollow
{
    internal enum ExpeditionCrossingDecision
    {
        None,
        BeginAttempt,
        Waiting,
        RetryAttempt,
        NativeTransitionObserved,
        Fail
    }

    internal struct ExpeditionCrossingInputs
    {
        internal bool ApproachReady;
        internal bool AttemptActive;
        internal bool HasTraversalTarget;
        internal bool LeaderTriggerEntered;
        internal bool PlayerTriggerEntered;
        internal bool NativeZoning;
        internal float AttemptElapsedSeconds;
        internal float LeaderTriggerElapsedSeconds;
        internal float PlayerTriggerElapsedSeconds;
        internal int AttemptCount;
    }

    // Pure bounded policy. Runtime code supplies real trigger/NavMesh observations; this class never
    // authorizes zoning or movement on its own.
    internal static class ExpeditionCrossingPolicy
    {
        internal const int MaximumAttempts = 2;
        internal const float AttemptTimeoutSeconds = 6f;
        internal const float LeaderTriggerGraceSeconds = 12f;
        internal const float PlayerTriggerGraceSeconds = 3f;
        internal const float ApproachReadyDistance = 1.75f;
        internal const float StalledNearTriggerDistance = 5.5f;

        internal static ExpeditionCrossingDecision Evaluate(ExpeditionCrossingInputs input)
        {
            if (input.NativeZoning) return ExpeditionCrossingDecision.NativeTransitionObserved;

            if (input.PlayerTriggerEntered)
                return input.PlayerTriggerElapsedSeconds <= PlayerTriggerGraceSeconds
                    ? ExpeditionCrossingDecision.Waiting
                    : ExpeditionCrossingDecision.Fail;

            if (input.LeaderTriggerEntered)
                return input.LeaderTriggerElapsedSeconds <= LeaderTriggerGraceSeconds
                    ? ExpeditionCrossingDecision.Waiting
                    : ExpeditionCrossingDecision.Fail;

            if (input.AttemptActive)
            {
                if (input.AttemptElapsedSeconds < AttemptTimeoutSeconds)
                    return ExpeditionCrossingDecision.Waiting;
                return input.AttemptCount < MaximumAttempts && input.HasTraversalTarget
                    ? ExpeditionCrossingDecision.RetryAttempt
                    : ExpeditionCrossingDecision.Fail;
            }

            if (!input.ApproachReady) return ExpeditionCrossingDecision.None;
            return input.HasTraversalTarget
                ? ExpeditionCrossingDecision.BeginAttempt
                : ExpeditionCrossingDecision.Fail;
        }
    }
}
