using System;

namespace ErenshorFollow
{
    internal enum FollowCombatDecision
    {
        Drive,
        PauseForCombat,
        RecoveringAfterCombat
    }

    // Direct Follow yields PlayerControl entirely to Erenshor during real combat. Expedition/Lead has
    // its own richer combat lifecycle; this policy is intentionally only for ordinary direct Follow.
    internal static class FollowCombatPolicy
    {
        internal const float PostCombatSafetySeconds = 2.0f;

        internal static FollowCombatDecision Evaluate(
            bool directFollow,
            bool combatActive,
            bool combatPauseLatched,
            float clearSeconds)
        {
            if (!directFollow) return FollowCombatDecision.Drive;
            if (combatActive) return FollowCombatDecision.PauseForCombat;
            if (!combatPauseLatched) return FollowCombatDecision.Drive;

            if (float.IsNaN(clearSeconds) || float.IsInfinity(clearSeconds) || clearSeconds < 0f)
                clearSeconds = 0f;

            return clearSeconds < PostCombatSafetySeconds
                ? FollowCombatDecision.RecoveringAfterCombat
                : FollowCombatDecision.Drive;
        }
    }
}
