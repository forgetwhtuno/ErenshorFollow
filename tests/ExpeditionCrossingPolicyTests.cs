using System;

namespace ErenshorFollow
{
    internal static class ExpeditionCrossingPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static ExpeditionCrossingInputs Input()
        {
            return new ExpeditionCrossingInputs();
        }

        private static void ApproachRequiresAProvenTraversalTarget()
        {
            ExpeditionCrossingInputs input = Input();
            input.ApproachReady = true;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Fail,
                "approach without a trigger traversal fails closed");

            input.HasTraversalTarget = true;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.BeginAttempt,
                "approach with a proven trigger traversal begins crossing");
        }

        private static void CrossingRetriesAreBounded()
        {
            ExpeditionCrossingInputs input = Input();
            input.AttemptActive = true;
            input.AttemptCount = 1;
            input.HasTraversalTarget = true;
            input.AttemptElapsedSeconds = ExpeditionCrossingPolicy.AttemptTimeoutSeconds - 0.1f;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Waiting,
                "active crossing waits before timeout");

            input.AttemptElapsedSeconds = ExpeditionCrossingPolicy.AttemptTimeoutSeconds + 0.1f;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.RetryAttempt,
                "first timed-out crossing may use one bounded retry");

            input.AttemptCount = ExpeditionCrossingPolicy.MaximumAttempts;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Fail,
                "crossing retry bound fails closed");
        }

        private static void LeaderTriggerGetsNativePlayerHandoffGrace()
        {
            ExpeditionCrossingInputs input = Input();
            input.LeaderTriggerEntered = true;
            input.LeaderTriggerElapsedSeconds = ExpeditionCrossingPolicy.LeaderTriggerGraceSeconds - 0.1f;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Waiting,
                "leader trigger entry waits for native player zoning");

            input.LeaderTriggerElapsedSeconds = ExpeditionCrossingPolicy.LeaderTriggerGraceSeconds + 0.1f;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Fail,
                "leader trigger grace expires safely when player never zones");
        }

        private static void PlayerTriggerMustProduceNativeZoningQuickly()
        {
            ExpeditionCrossingInputs input = Input();
            input.PlayerTriggerEntered = true;
            input.PlayerTriggerElapsedSeconds = ExpeditionCrossingPolicy.PlayerTriggerGraceSeconds - 0.1f;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Waiting,
                "player trigger entry waits briefly for GameData.Zoning");

            input.PlayerTriggerElapsedSeconds = ExpeditionCrossingPolicy.PlayerTriggerGraceSeconds + 0.1f;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.Fail,
                "player trigger without native zoning fails closed");
        }

        private static void NativeZoningAlwaysWins()
        {
            ExpeditionCrossingInputs input = Input();
            input.NativeZoning = true;
            input.PlayerTriggerEntered = true;
            input.PlayerTriggerElapsedSeconds = 999f;
            input.LeaderTriggerEntered = true;
            input.LeaderTriggerElapsedSeconds = 999f;
            input.AttemptActive = true;
            input.AttemptElapsedSeconds = 999f;
            input.AttemptCount = 99;
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.NativeTransitionObserved,
                "native zoning observation outranks local crossing timers");
        }

        private static void IdlePolicyDoesNothingAwayFromApproach()
        {
            ExpeditionCrossingInputs input = Input();
            Assert(ExpeditionCrossingPolicy.Evaluate(input) == ExpeditionCrossingDecision.None,
                "crossing policy is inert until the verified route reaches the boundary phase");
        }

        public static int Main()
        {
            IdlePolicyDoesNothingAwayFromApproach();
            ApproachRequiresAProvenTraversalTarget();
            CrossingRetriesAreBounded();
            LeaderTriggerGetsNativePlayerHandoffGrace();
            PlayerTriggerMustProduceNativeZoningQuickly();
            NativeZoningAlwaysWins();
            Console.WriteLine("Expedition crossing policy tests passed: " + _passed);
            return 0;
        }
    }
}
