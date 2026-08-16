using System;

namespace ErenshorFollow
{
    internal static class SimActionMenuLayoutPolicyTests
    {
        private static int _passed;
        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void SmallMenusStayCompact()
        {
            float twoActions = SimActionMenuLayoutPolicy.ResolvePanelHeight(2, 0, 1080f);
            float threeActions = SimActionMenuLayoutPolicy.ResolvePanelHeight(3, 0, 1080f);
            Assert(twoActions < 170f, "two-action context menu stays compact");
            Assert(threeActions < 205f, "three-action context menu stays compact");
            Assert(threeActions > twoActions, "height grows with action count");
            float threeActionContent = SimActionMenuLayoutPolicy.ContentPadding +
                (3f * SimActionMenuLayoutPolicy.ActionRowHeight) +
                (2f * SimActionMenuLayoutPolicy.RowSpacing);
            Assert(SimActionMenuLayoutPolicy.ResolveViewportHeight(threeActions) >= threeActionContent,
                "ordinary three-action menu does not require decorative scrolling");
        }

        private static void LargerMenusGrowThenCap()
        {
            float medium = SimActionMenuLayoutPolicy.ResolvePanelHeight(6, 2, 1080f);
            float huge = SimActionMenuLayoutPolicy.ResolvePanelHeight(30, 4, 1080f);
            Assert(medium > 250f && medium < SimActionMenuLayoutPolicy.MaximumPanelHeight,
                "medium action surface grows to its content");
            Assert(Math.Abs(huge - SimActionMenuLayoutPolicy.MaximumPanelHeight) < 0.01f,
                "large action surface caps and relies on scrolling");
        }

        private static void SmallScreensRemainUsable()
        {
            float height = SimActionMenuLayoutPolicy.ResolvePanelHeight(20, 2, 150f);
            Assert(height <= 134.01f, "small screen cap is respected");
            Assert(SimActionMenuLayoutPolicy.ResolveViewportHeight(height) >= SimActionMenuLayoutPolicy.MinimumViewportHeight,
                "viewport keeps a usable minimum");
        }

        public static int Main()
        {
            SmallMenusStayCompact();
            LargerMenusGrowThenCap();
            SmallScreensRemainUsable();
            Console.WriteLine("Sim Actions layout tests passed: " + _passed);
            return 0;
        }
    }
}
