using System;

namespace ErenshorFollow
{
    internal static class FollowNameMatchPolicyTests
    {
        private static int _passed;
        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        public static int Main()
        {
            Assert(FollowNameMatchPolicy.Evaluate(0, 0) == FollowNameMatchDecision.None, "no match returns none");
            Assert(FollowNameMatchPolicy.Evaluate(1, 4) == FollowNameMatchDecision.Exact, "one exact match outranks partial matches");
            Assert(FollowNameMatchPolicy.Evaluate(2, 0) == FollowNameMatchDecision.Ambiguous, "duplicate exact display names are ambiguous");
            Assert(FollowNameMatchPolicy.Evaluate(0, 1) == FollowNameMatchDecision.Partial, "one partial match is accepted");
            Assert(FollowNameMatchPolicy.Evaluate(0, 2) == FollowNameMatchDecision.Ambiguous, "multiple partial matches are ambiguous");
            Assert(FollowNameMatchPolicy.Evaluate(-3, -2) == FollowNameMatchDecision.None, "negative counts sanitize to none");
            Console.WriteLine("All deterministic Follow name-resolution tests passed (" + _passed + " assertions).");
            return 0;
        }
    }
}
