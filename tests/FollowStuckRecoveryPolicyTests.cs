using System;

namespace ErenshorFollow
{
    internal static class FollowStuckRecoveryPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static FollowStuckRecoveryDecision Decide(float seconds, bool routeProblem, int attempts, bool due)
        {
            return FollowStuckRecoveryPolicy.Evaluate(seconds, routeProblem, attempts, due);
        }

        private static void HealthyMovementDoesNothing()
        {
            Assert(Decide(1f, false, 0, true) == FollowStuckRecoveryDecision.None,
                "healthy progress does not repath");
            Assert(Decide(1f, true, 0, true) == FollowStuckRecoveryDecision.None,
                "brief route issue does not hammer NavMesh");
        }

        private static void RouteProblemRecoversEarlierThanGenericStall()
        {
            Assert(Decide(2.1f, true, 0, true) == FollowStuckRecoveryDecision.Repath,
                "verified route problem permits bounded early repath");
            Assert(Decide(2.1f, false, 0, true) == FollowStuckRecoveryDecision.None,
                "ordinary movement gets longer progress grace");
            Assert(Decide(3.1f, false, 0, true) == FollowStuckRecoveryDecision.Repath,
                "ordinary no-progress eventually repaths");
        }

        private static void RetrySpacingIsRespected()
        {
            Assert(Decide(6f, true, 1, false) == FollowStuckRecoveryDecision.None,
                "repath retry waits for spacing deadline");
            Assert(Decide(6f, true, 1, true) == FollowStuckRecoveryDecision.Repath,
                "repath retry is allowed once spacing deadline arrives");
        }

        private static void RecoveryIsBoundedAndStopsCleanly()
        {
            Assert(Decide(8.9f, true, FollowStuckRecoveryPolicy.MaxRepathAttempts, true) == FollowStuckRecoveryDecision.None,
                "maximum attempts do not stop before bounded timeout");
            Assert(Decide(9.0f, true, FollowStuckRecoveryPolicy.MaxRepathAttempts, true) == FollowStuckRecoveryDecision.Stop,
                "bounded timeout stops after maximum attempts");
            Assert(Decide(20f, false, FollowStuckRecoveryPolicy.MaxRepathAttempts, true) == FollowStuckRecoveryDecision.Stop,
                "long generic stall also stops safely");
        }

        private static void BadInputsFailConservatively()
        {
            Assert(Decide(float.NaN, true, 0, true) == FollowStuckRecoveryDecision.None,
                "NaN duration is sanitized instead of causing a recovery storm");
            Assert(Decide(float.PositiveInfinity, false, 0, true) == FollowStuckRecoveryDecision.None,
                "infinite duration is sanitized");
            Assert(Decide(-5f, false, -4, true) == FollowStuckRecoveryDecision.None,
                "negative duration and attempts are sanitized");
        }

        public static int Main()
        {
            HealthyMovementDoesNothing();
            RouteProblemRecoversEarlierThanGenericStall();
            RetrySpacingIsRespected();
            RecoveryIsBoundedAndStopsCleanly();
            BadInputsFailConservatively();
            Console.WriteLine("All deterministic Follow stuck-recovery tests passed (" + _passed + " assertions)." );
            return 0;
        }
    }
}
