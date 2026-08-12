using System;

namespace ErenshorFollow
{
    internal struct TravelOverlayPoint
    {
        internal readonly float X;
        internal readonly float Y;

        internal TravelOverlayPoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    internal struct ArrivalActionVisibility
    {
        internal readonly bool ShowCampHere;
        internal readonly bool ShowReturn;

        internal ArrivalActionVisibility(bool showCampHere, bool showReturn)
        {
            ShowCampHere = showCampHere;
            ShowReturn = showReturn;
        }

        internal bool Any { get { return ShowCampHere || ShowReturn; } }
    }

    // Unity-free layout/action policy kept separate so screen recovery and
    // verified-arrival action admission can be regression-tested deterministically.
    internal static class TravelOverlayLogic
    {
        internal static TravelOverlayPoint ClampPosition(
            float x,
            float y,
            float panelWidth,
            float panelHeight,
            float screenWidth,
            float screenHeight,
            float margin)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) x = margin;
            if (float.IsNaN(y) || float.IsInfinity(y)) y = margin;
            if (float.IsNaN(screenWidth) || float.IsInfinity(screenWidth) || screenWidth < 0f) screenWidth = 0f;
            if (float.IsNaN(screenHeight) || float.IsInfinity(screenHeight) || screenHeight < 0f) screenHeight = 0f;
            if (panelWidth < 0f) panelWidth = 0f;
            if (panelHeight < 0f) panelHeight = 0f;
            if (margin < 0f) margin = 0f;

            // Preserve the requested margin when the panel fits. On pathological tiny
            // resolutions, pin the panel to the origin so the maximum possible area
            // remains reachable instead of forcing an additional margin off-screen.
            float maxX = Math.Max(0f, screenWidth - panelWidth - margin);
            float maxY = Math.Max(0f, screenHeight - panelHeight - margin);
            float minX = Math.Min(margin, maxX);
            float minY = Math.Min(margin, maxY);
            return new TravelOverlayPoint(Clamp(x, minX, maxX), Clamp(y, minY, maxY));
        }

        internal static ArrivalActionVisibility ResolveArrivalActions(
            bool verifiedArrivalVisible,
            bool campmasterAvailable,
            bool campmasterActive,
            bool campRequestPending,
            bool canReturn)
        {
            if (!verifiedArrivalVisible) return new ArrivalActionVisibility(false, false);
            bool camp = campmasterAvailable && !campmasterActive && !campRequestPending;
            return new ArrivalActionVisibility(camp, canReturn);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
