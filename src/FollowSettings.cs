using System;
using Lunaris.Config;

namespace ErenshorFollow
{
    // Native-Lunaris settings holder. Legacy overlay offsets remain load-compatible; retained uGUI
    // stores only normalized bottom-left coordinates and safely rejects old pixel values.
    internal sealed class FollowConfigEntry<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        internal FollowConfigEntry(Func<T> get, Action<T> set)
        {
            _get = get;
            _set = set;
        }

        internal T Value
        {
            get { return _get(); }
            set { _set(value); }
        }
    }

    internal sealed class FollowSettings
    {
        [Config("OverlayOffsetX", "UI", "Pixels to shift the travel status overlay left from its default top-right position (increase if it overlaps the minimap).")]
        public float OverlayOffsetX = 0f;

        [Config("OverlayOffsetY", "UI", "Pixels to shift the travel status overlay down from its default top-right position.")]
        public float OverlayOffsetY = 0f;

        [Config("Verbose", "Diagnostics", "Enable detailed click/route diagnostics. Normal play keeps high-frequency action-menu logging quiet.")]
        public bool DiagnosticsVerbose = false;


        [Config("ExperimentalCrossZoneFollow", "Follow", "OFF by default until live verification on the current game build. When enabled, direct Follow may preserve a SimPlayerTracking identity through a native player zone transition and reacquire that same party Sim on the far side. Never initiates zoning or teleports.")]
        public bool ExperimentalCrossZoneFollow = false;

        [Config("OverlayPositionX", "UI", "Retained-uGUI travel panel horizontal position normalized 0..1 from bottom-left. Legacy pixel values recover to the safe default.")]
        public float OverlayPositionX = -1f;

        [Config("OverlayPositionY", "UI", "Retained-uGUI travel panel vertical position normalized 0..1 from bottom-left. Legacy pixel values recover to the safe default.")]
        public float OverlayPositionY = -1f;
    }
}
