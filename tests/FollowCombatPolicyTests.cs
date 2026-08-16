using System;

namespace ErenshorFollow
{
    internal static class FollowCombatPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void LeaderOwnedFollowIsUnaffected()
        {
            Assert(FollowCombatPolicy.Evaluate(false, true, true, 0f) == FollowCombatDecision.Drive,
                "expedition/leader-owned Follow keeps its existing combat lifecycle");
        }

        private static void RealCombatYieldsNativeControl()
        {
            Assert(FollowCombatPolicy.Evaluate(true, true, false, 0f) == FollowCombatDecision.PauseForCombat,
                "direct Follow pauses immediately for real combat");
            Assert(FollowCombatPolicy.Evaluate(true, true, true, 20f) == FollowCombatDecision.PauseForCombat,
                "continuing combat stays paused regardless of stale clear time");
        }

        private static void PostCombatSafetyWindowIsBounded()
        {
            Assert(FollowCombatPolicy.Evaluate(true, false, true, 0f) == FollowCombatDecision.RecoveringAfterCombat,
                "combat clear begins safety window");
            Assert(FollowCombatPolicy.Evaluate(true, false, true, 1.99f) == FollowCombatDecision.RecoveringAfterCombat,
                "safety window does not resume early");
            Assert(FollowCombatPolicy.Evaluate(true, false, true, FollowCombatPolicy.PostCombatSafetySeconds) == FollowCombatDecision.Drive,
                "direct Follow resumes when safety window completes");
            Assert(FollowCombatPolicy.Evaluate(true, false, false, 0f) == FollowCombatDecision.Drive,
                "normal direct Follow drives when no combat pause is latched");
        }

        private static void BadClearTimesFailConservatively()
        {
            Assert(FollowCombatPolicy.Evaluate(true, false, true, float.NaN) == FollowCombatDecision.RecoveringAfterCombat,
                "NaN clear time remains in recovery");
            Assert(FollowCombatPolicy.Evaluate(true, false, true, float.PositiveInfinity) == FollowCombatDecision.RecoveringAfterCombat,
                "infinite clear time is sanitized rather than skipping safety");
            Assert(FollowCombatPolicy.Evaluate(true, false, true, -1f) == FollowCombatDecision.RecoveringAfterCombat,
                "negative clear time remains in recovery");
        }

        public static int Main()
        {
            LeaderOwnedFollowIsUnaffected();
            RealCombatYieldsNativeControl();
            PostCombatSafetyWindowIsBounded();
            BadClearTimesFailConservatively();
            Console.WriteLine("All deterministic Follow combat-policy tests passed (" + _passed + " assertions)." );
            return 0;
        }
    }
}
