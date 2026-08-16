namespace ErenshorFollow
{
    internal enum FollowNameMatchDecision
    {
        None,
        Exact,
        Partial,
        Ambiguous
    }

    // Pure command-name resolution policy. Sim Actions are object-specific, but typed commands need
    // deterministic behavior when party Sims have overlapping or identical display names.
    internal static class FollowNameMatchPolicy
    {
        internal static FollowNameMatchDecision Evaluate(int exactMatches, int partialMatches)
        {
            if (exactMatches < 0) exactMatches = 0;
            if (partialMatches < 0) partialMatches = 0;
            if (exactMatches > 1) return FollowNameMatchDecision.Ambiguous;
            if (exactMatches == 1) return FollowNameMatchDecision.Exact;
            if (partialMatches > 1) return FollowNameMatchDecision.Ambiguous;
            if (partialMatches == 1) return FollowNameMatchDecision.Partial;
            return FollowNameMatchDecision.None;
        }
    }
}
