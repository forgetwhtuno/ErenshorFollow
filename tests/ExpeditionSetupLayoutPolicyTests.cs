using System;

namespace ErenshorFollow
{
    internal static class ExpeditionSetupLayoutPolicyTests
    {
        private static int _passed;
        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        // Test #1: destination row target height bounded around 28-32 px. This is the constant the
        // oversized-row defect actually traces back to: the row rect was never explicitly sized, so
        // Unity's default RectTransform size (100px) rendered instead of this value.
        private static void RowMetricsAreCompact()
        {
            Assert(ExpeditionSetupLayoutPolicy.DestinationRowHeight >= 28f && ExpeditionSetupLayoutPolicy.DestinationRowHeight <= 32f,
                "destination row height is bounded to the requested 28-32px MMO row metric");
            Assert(ExpeditionSetupLayoutPolicy.RowSpacing >= 2f && ExpeditionSetupLayoutPolicy.RowSpacing <= 4f,
                "destination row spacing is bounded to the requested 2-4px gap");
            Assert(ExpeditionSetupLayoutPolicy.SectionRowHeight > 0f && ExpeditionSetupLayoutPolicy.SectionRowHeight < ExpeditionSetupLayoutPolicy.DestinationRowHeight,
                "section header rows stay shorter than an actual destination row");
        }

        public static int Main()
        {
            RowMetricsAreCompact();
            Console.WriteLine("Expedition setup layout policy tests passed: " + _passed);
            return 0;
        }
    }
}
