using System;
using Lunaris.Config;

namespace ErenshorFollow
{
    // Thin native-Lunaris settings holder plus a small ConfigEntry<T>-compatible wrapper so
    // TravelStatusOverlay's existing .Value call sites needed no changes after the BepInEx
    // ConfigFile.Bind migration. All 5 existing settings are preserved verbatim
    // (section/key/default/description).
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

        // Defaults mirror TravelStatusOverlay's DefaultX/DefaultY constants. Under a fresh native
        // Lunaris config these are the actual first-run position; the legacy
        // OverlayOffsetX/Y-derived screen-relative migration in TravelStatusOverlay.EnsurePositionConfig
        // is preserved unchanged but will not fire against a fresh config, since OverlayOffsetX/Y also
        // start at their fresh Lunaris defaults (0f) in that case.
        [Config("OverlayPositionX", "UI", "Travel panel X position in IMGUI screen coordinates. Drag the panel header to update it.")]
        public float OverlayPositionX = 18f;

        [Config("OverlayPositionY", "UI", "Travel panel Y position in IMGUI screen coordinates. Drag the panel header to update it.")]
        public float OverlayPositionY = 140f;
    }
}
