using System;

namespace ErenshorFollow
{
    // Unity-free sizing policy for the contextual Sim Actions surface. The menu is deliberately
    // content-driven: a handful of MMO actions should never reserve a generic panel-sized body.
    internal static class SimActionMenuLayoutPolicy
    {
        internal const int UiRevision = 2;
        internal const float Width = 236f;
        internal const float HeaderHeight = 46f;
        internal const float ActionRowHeight = 28f;
        internal const float SectionRowHeight = 18f;
        internal const float RowSpacing = 3f;
        internal const float ContentPadding = 6f;
        internal const float ChromeHeight = 60f;
        internal const float MinimumPanelHeight = 108f;
        internal const float MaximumPanelHeight = 320f;
        internal const float MinimumViewportHeight = 40f;

        internal static float ResolvePanelHeight(int actionRows, int sectionRows, float screenHeight)
        {
            if (actionRows < 0) actionRows = 0;
            if (sectionRows < 0) sectionRows = 0;
            int rows = actionRows + sectionRows;
            float content = ContentPadding;
            content += actionRows * ActionRowHeight;
            content += sectionRows * SectionRowHeight;
            if (rows > 1) content += (rows - 1) * RowSpacing;

            float screenCap = float.IsNaN(screenHeight) || float.IsInfinity(screenHeight)
                ? MaximumPanelHeight
                : Math.Max(MinimumPanelHeight, screenHeight - 16f);
            float cap = Math.Min(MaximumPanelHeight, screenCap);
            float desired = ChromeHeight + content;
            if (desired < MinimumPanelHeight) return MinimumPanelHeight;
            return desired > cap ? cap : desired;
        }

        internal static float ResolveViewportHeight(float panelHeight)
        {
            return Math.Max(MinimumViewportHeight, panelHeight - ChromeHeight);
        }
    }
}
