using System;

namespace ErenshorFollow
{
    // Pure budget contract shared by production discovery and deterministic regression tests.
    // The historical primary pass must never consume the eight zero-sample fallback positions.
    internal static class CrossingSeedBudgetPolicy
    {
        internal const int MaxSeedsPerCrossing = 38;
        internal const int PrimarySeedBudget = 30;
        internal const int ZeroSampleFallbackBudget = 8;

        internal static bool CanAddPrimary(int currentCount)
        {
            return currentCount >= 0 && currentCount < PrimarySeedBudget;
        }

        internal static bool CanAddFallback(int currentCount)
        {
            return currentCount >= PrimarySeedBudget && currentCount < MaxSeedsPerCrossing;
        }

        internal static string FallbackLabel(int index)
        {
            if (index < 0 || index >= ZeroSampleFallbackBudget) return string.Empty;
            return "midRing" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
