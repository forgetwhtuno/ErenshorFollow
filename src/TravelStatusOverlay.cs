using HarmonyLib;
using UnityEngine;

namespace ErenshorFollow
{
    internal static class TravelStatusOverlay
    {
        private enum OverlayMode { None, Follow, Lead, Expedition, Arrival }

        private static readonly GUIContent TitleContent = new GUIContent();
        private static readonly GUIContent StateContent = new GUIContent();
        private static readonly GUIContent StopContent = new GUIContent("Stop");
        private static readonly GUIContent CampContent = new GUIContent("Camp Here");
        private static readonly GUIContent ReturnContent = new GUIContent("Return");
        private static Rect _bounds;
        private static GUIStyle _boxStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _stateStyle;
        private static GUIStyle _buttonStyle;
        private static OverlayMode _lastMode;
        private static string _lastName;
        private static string _lastDestination;
        private static FollowController.DriveState _lastFollowState;
        private static LeaderController.TravelState _lastLeadState;
        private static ExpeditionState _lastExpeditionState;
        private static float _followWaitingSince = -1f;

        private static FollowConfigEntry<float> _positionX;
        private static FollowConfigEntry<float> _positionY;
        private static bool _positionConfigBound;
        private static bool _dragging;
        private static int _dragControlId;
        private static Vector2 _dragOffset;
        private static float _dragX;
        private static float _dragY;

        private static string _recentArrivalLeader;
        private static string _recentArrivalDestination;
        private static float _recentArrivalUntil = -1f;
        private static bool _arrivalWasLive;
        private static bool _campRequestQueued;

        // Retained only so current main's Awake() remains source-compatible with existing config files.
        // New placement is persisted through OverlayPositionX/Y, not runtime-only offsets.
        internal static float OffsetX;
        internal static float OffsetY;

        private const float Width = 258f;
        private const float NormalHeight = 76f;
        private const float ArrivalHeight = 112f;
        private const float ScreenMargin = 8f;
        private const float DefaultX = 18f;
        private const float DefaultY = 140f;
        private const float LegacyRightMargin = 18f;
        private const float LegacyTopMargin = 104f;
        private const float FollowWaitingVisibleSeconds = 1.5f;
        private const float ArrivalActionVisibleSeconds = 12f;

        internal static void Draw()
        {
            LeaderController.StatusSnapshot lead = LeaderController.GetStatusSnapshot();
            FollowController.StatusSnapshot follow = FollowController.GetStatusSnapshot();
            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            UpdateArrivalCache(expedition);
            OverlayMode mode = ResolveMode(expedition, lead, follow);
            if (mode == OverlayMode.None)
            {
                _lastMode = OverlayMode.None;
                return;
            }

            bool arrived = mode == OverlayMode.Arrival ||
                           (mode == OverlayMode.Expedition && expedition.State == ExpeditionState.Arrived);
            bool campAvailable = arrived && CampmasterIntegrationBridge.IsAvailable;
            bool campActive = campAvailable && CampmasterIntegrationBridge.IsHuntCampActive;
            bool canReturn = arrived && ExpeditionCoordinator.CanReturn();
            ArrivalActionVisibility actions = TravelOverlayLogic.ResolveArrivalActions(
                arrived, campAvailable, campActive, _campRequestQueued, canReturn);

            UpdateBounds(actions.Any ? ArrivalHeight : NormalHeight);
            HandleDrag(actions.Any ? ArrivalHeight : NormalHeight);
            EnsureStyles();
            RefreshContent(mode, expedition, lead, follow);
            if (arrived && campActive) StateContent.text = "Arrived - hunt camp active";
            else if (arrived && _campRequestQueued) StateContent.text = "Arrived - camp request queued";

            int previousDepth = GUI.depth;
            try
            {
                GUI.depth = -40;
                GUI.Box(_bounds, GUIContent.none, _boxStyle);
                GUI.Label(new Rect(_bounds.x + 10f, _bounds.y + 7f, Width - 84f, 24f), TitleContent, _titleStyle);
                GUI.Label(new Rect(_bounds.x + 10f, _bounds.y + 35f, Width - 82f, 24f), StateContent, _stateStyle);

                if (arrived)
                {
                    DrawArrivalActions(actions);
                }
                else if (GUI.Button(new Rect(_bounds.x + Width - 66f, _bounds.y + 23f, 56f, 30f), StopContent, _buttonStyle))
                {
                    ErenshorFollowPlugin.StopAllTravel("[Erenshor Travel] Travel stopped.", "yellow");
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        internal static bool PointerIsOverOverlay()
        {
            // A drag owns the gesture until mouse-up even if the pointer has left the panel.
            // Ordinary clicks that did not begin on the panel are still untouched.
            if (_dragging) return true;

            LeaderController.StatusSnapshot lead = LeaderController.GetStatusSnapshot();
            FollowController.StatusSnapshot follow = FollowController.GetStatusSnapshot();
            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            UpdateArrivalCache(expedition);
            OverlayMode mode = ResolveMode(expedition, lead, follow);
            if (mode == OverlayMode.None) return false;

            bool arrived = mode == OverlayMode.Arrival ||
                           (mode == OverlayMode.Expedition && expedition.State == ExpeditionState.Arrived);
            bool campAvailable = arrived && CampmasterIntegrationBridge.IsAvailable;
            bool campActive = campAvailable && CampmasterIntegrationBridge.IsHuntCampActive;
            bool canReturn = arrived && ExpeditionCoordinator.CanReturn();
            ArrivalActionVisibility actions = TravelOverlayLogic.ResolveArrivalActions(
                arrived, campAvailable, campActive, _campRequestQueued, canReturn);
            UpdateBounds(actions.Any ? ArrivalHeight : NormalHeight);

            Vector3 mouse = Input.mousePosition;
            return _bounds.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }

        private static void DrawArrivalActions(ArrivalActionVisibility actions)
        {
            float x = _bounds.x + 10f;
            float y = _bounds.y + 70f;

            if (actions.ShowCampHere)
            {
                if (GUI.Button(new Rect(x, y, 94f, 30f), CampContent, _buttonStyle))
                {
                    string failure;
                    if (CampmasterIntegrationBridge.TryDeclareHere(out failure))
                    {
                        _campRequestQueued = true;
                        Chat("[Erenshor Expedition] Campmaster handoff queued for this verified arrival.", "lightblue");
                    }
                    else
                    {
                        Chat("[Erenshor Expedition] Could not declare this camp" +
                             (string.IsNullOrWhiteSpace(failure) ? "." : ": " + failure), "yellow");
                    }
                    return;
                }
                x += 102f;
            }

            if (actions.ShowReturn && GUI.Button(new Rect(x, y, 82f, 30f), ReturnContent, _buttonStyle))
            {
                ClearArrivalCache();
                ExpeditionCoordinator.TryReturn();
            }
        }

        private static void RefreshContent(OverlayMode mode, ExpeditionStatusSnapshot expedition,
            LeaderController.StatusSnapshot lead, FollowController.StatusSnapshot follow)
        {
            if (mode == OverlayMode.Arrival)
            {
                TitleContent.text = SafeName(_recentArrivalLeader) + " led to " + SafeName(_recentArrivalDestination);
                StateContent.text = "Arrived";
                _lastName = _recentArrivalLeader;
                _lastDestination = _recentArrivalDestination;
                _lastExpeditionState = ExpeditionState.Arrived;
                _lastMode = mode;
                return;
            }
            if (mode == OverlayMode.Expedition)
            {
                if (expedition.State == ExpeditionState.Arrived)
                    TitleContent.text = SafeName(expedition.LeaderName) + " led to " + SafeName(expedition.DestinationName);
                else if (_lastMode != mode || _lastName != expedition.LeaderName || _lastDestination != expedition.DestinationName)
                    TitleContent.text = SafeName(expedition.LeaderName) + " leading to " + SafeName(expedition.DestinationName);
                if (_lastMode != mode || _lastExpeditionState != expedition.State)
                    StateContent.text = ExpeditionCoordinator.DescribeState(expedition.State);
                _lastName = expedition.LeaderName;
                _lastDestination = expedition.DestinationName;
                _lastExpeditionState = expedition.State;
                _lastMode = mode;
                return;
            }
            if (mode == OverlayMode.Lead)
            {
                if (_lastMode != mode || _lastName != lead.LeaderName || _lastDestination != lead.DestinationName)
                    TitleContent.text = SafeName(lead.LeaderName) + " leading to " + SafeName(lead.DestinationName);
                if (_lastMode != mode || _lastLeadState != lead.State)
                    StateContent.text = LeadStateText(lead.State);
                _lastName = lead.LeaderName;
                _lastDestination = lead.DestinationName;
                _lastLeadState = lead.State;
            }
            else
            {
                bool rebinding = follow.State == FollowController.DriveState.RebindingAfterZoneChange;
                if (_lastMode != mode || _lastName != follow.TargetName || _lastFollowState != follow.State)
                    TitleContent.text = rebinding ? "Follow target: " + SafeName(follow.TargetName) : "Following " + SafeName(follow.TargetName);
                if (_lastMode != mode || _lastFollowState != follow.State)
                    StateContent.text = FollowStateText(follow.State);
                _lastName = follow.TargetName;
                _lastDestination = null;
                _lastFollowState = follow.State;
            }
            _lastMode = mode;
        }

        private static OverlayMode ResolveMode(ExpeditionStatusSnapshot expedition,
            LeaderController.StatusSnapshot lead, FollowController.StatusSnapshot follow)
        {
            // An expedition owns the leg beneath it, so it outranks the raw Lead view of the same trip.
            // Arrived is only produced by ExpeditionCoordinator after a real zone transition into the
            // verified destination, so it is safe to seed the short post-arrival action window from it.
            if (expedition.Active || expedition.State == ExpeditionState.Arrived)
            {
                _followWaitingSince = -1f;
                return OverlayMode.Expedition;
            }
            if (lead.Active)
            {
                _followWaitingSince = -1f;
                ClearArrivalCache();
                return OverlayMode.Lead;
            }
            if (!follow.Active)
            {
                _followWaitingSince = -1f;
                return _recentArrivalUntil >= Time.unscaledTime ? OverlayMode.Arrival : OverlayMode.None;
            }
            if (follow.State != FollowController.DriveState.Waiting)
            {
                _followWaitingSince = -1f;
                ClearArrivalCache();
                return OverlayMode.Follow;
            }
            if (_followWaitingSince < 0f) _followWaitingSince = Time.unscaledTime;
            if (Time.unscaledTime - _followWaitingSince <= FollowWaitingVisibleSeconds)
            {
                ClearArrivalCache();
                return OverlayMode.Follow;
            }
            return OverlayMode.None;
        }

        private static void UpdateArrivalCache(ExpeditionStatusSnapshot expedition)
        {
            if (expedition.State == ExpeditionState.Arrived)
            {
                if (!_arrivalWasLive)
                {
                    _recentArrivalLeader = expedition.LeaderName;
                    _recentArrivalDestination = expedition.DestinationName;
                    _recentArrivalUntil = Time.unscaledTime + ArrivalActionVisibleSeconds;
                    _campRequestQueued = false;
                }
                _arrivalWasLive = true;
                return;
            }

            _arrivalWasLive = false;
            if (expedition.Active)
            {
                ClearArrivalCache();
                return;
            }
            if (_recentArrivalUntil >= 0f && _recentArrivalUntil < Time.unscaledTime)
                ClearArrivalCache();
        }

        private static void ClearArrivalCache()
        {
            _recentArrivalLeader = null;
            _recentArrivalDestination = null;
            _recentArrivalUntil = -1f;
            _campRequestQueued = false;
        }

        private static string FollowStateText(FollowController.DriveState state)
        {
            switch (state)
            {
                case FollowController.DriveState.Moving: return "Moving";
                case FollowController.DriveState.Turning: return "Turning";
                case FollowController.DriveState.Waiting: return "Waiting in follow range";
                case FollowController.DriveState.PartialPathRetry: return "Retrying partial route";
                case FollowController.DriveState.NoProgress: return "No progress - retrying";
                case FollowController.DriveState.RebindingAfterZoneChange: return "Rebinding after zone change";
                default: return "Starting";
            }
        }

        private static string LeadStateText(LeaderController.TravelState state)
        {
            switch (state)
            {
                case LeaderController.TravelState.Moving: return "Moving";
                case LeaderController.TravelState.PausedForCombat: return "Paused for real combat";
                case LeaderController.TravelState.ResumingAfterCombat: return "Combat clear - resuming soon";
                case LeaderController.TravelState.WaitingForPlayer: return "Waiting for the group to catch up";
                case LeaderController.TravelState.Regrouping: return "Regrouping after combat";
                case LeaderController.TravelState.Held: return "Held";
                case LeaderController.TravelState.PartialRouteRetry: return "Retrying partial route";
                case LeaderController.TravelState.NoProgress: return "No progress - retrying";
                default: return "Starting";
            }
        }

        private static string SafeName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "destination" : value;
        }

        private static void EnsurePositionConfig()
        {
            if (_positionConfigBound || ErenshorFollowPlugin.Instance == null || ErenshorFollowPlugin.Instance.Settings == null) return;
            try
            {
                FollowSettings settings = ErenshorFollowPlugin.Instance.Settings;
                _positionX = new FollowConfigEntry<float>(() => settings.OverlayPositionX, v => settings.OverlayPositionX = v);
                _positionY = new FollowConfigEntry<float>(() => settings.OverlayPositionY, v => settings.OverlayPositionY = v);
                _positionConfigBound = true;

                // If a user previously adjusted the old offset knobs, and the native position config
                // is still at its fresh default (never explicitly moved), migrate that old runtime
                // position once. Fresh installs start upper-left, safely away from Erenshor's normal
                // upper-right minimap.
                bool stillAtFreshDefault = Mathf.Abs(_positionX.Value - DefaultX) < 0.01f && Mathf.Abs(_positionY.Value - DefaultY) < 0.01f;
                if (stillAtFreshDefault && (Mathf.Abs(OffsetX) > 0.01f || Mathf.Abs(OffsetY) > 0.01f))
                {
                    float firstX = Screen.width - Width - LegacyRightMargin - OffsetX;
                    float firstY = LegacyTopMargin + OffsetY;
                    TravelOverlayPoint migrated = TravelOverlayLogic.ClampPosition(
                        firstX, firstY, Width, NormalHeight, Screen.width, Screen.height, ScreenMargin);
                    PersistPosition(migrated.X, migrated.Y);
                }
            }
            catch { }
        }

        private static void UpdateBounds(float height)
        {
            EnsurePositionConfig();
            float x = _dragging ? _dragX : (_positionConfigBound ? _positionX.Value : DefaultX);
            float y = _dragging ? _dragY : (_positionConfigBound ? _positionY.Value : DefaultY);
            TravelOverlayPoint clamped = TravelOverlayLogic.ClampPosition(
                x, y, Width, height, Screen.width, Screen.height, ScreenMargin);
            _bounds = new Rect(clamped.X, clamped.Y, Width, height);

            // Resolution changes or stale saved coordinates recover automatically and are persisted once.
            if (!_dragging && _positionConfigBound &&
                (Mathf.Abs(clamped.X - x) > 0.01f || Mathf.Abs(clamped.Y - y) > 0.01f))
            {
                PersistPosition(clamped.X, clamped.Y);
            }
        }

        private static void HandleDrag(float height)
        {
            Event evt = Event.current;
            if (evt == null) return;
            int controlId = GUIUtility.GetControlID(0x45F0110, FocusType.Passive);
            Rect handle = new Rect(_bounds.x + 6f, _bounds.y + 4f, Width - 82f, 28f);

            if (evt.type == EventType.MouseDown && evt.button == 0 && handle.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _dragControlId = controlId;
                _dragging = true;
                _dragOffset = evt.mousePosition - new Vector2(_bounds.x, _bounds.y);
                _dragX = _bounds.x;
                _dragY = _bounds.y;
                evt.Use();
                return;
            }

            if (GUIUtility.hotControl != controlId) return;
            if (evt.type == EventType.MouseDrag && _dragging)
            {
                TravelOverlayPoint clamped = TravelOverlayLogic.ClampPosition(
                    evt.mousePosition.x - _dragOffset.x,
                    evt.mousePosition.y - _dragOffset.y,
                    Width,
                    height,
                    Screen.width,
                    Screen.height,
                    ScreenMargin);
                _dragX = clamped.X;
                _dragY = clamped.Y;
                _bounds = new Rect(_dragX, _dragY, Width, height);
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                GUIUtility.hotControl = 0;
                if (_dragging) PersistPosition(_dragX, _dragY);
                _dragging = false;
                _dragControlId = 0;
                evt.Use();
            }
        }

        internal static void CancelDragGesture()
        {
            if (_dragControlId != 0 && GUIUtility.hotControl == _dragControlId)
                GUIUtility.hotControl = 0;
            _dragControlId = 0;
            _dragging = false;
        }

        internal static void ResetForLifecycle()
        {
            CancelDragGesture();
            _bounds = new Rect();
            _lastMode = OverlayMode.None;
            _lastName = null;
            _lastDestination = null;
            _followWaitingSince = -1f;
            _positionX = null;
            _positionY = null;
            _positionConfigBound = false;
            _recentArrivalLeader = null;
            _recentArrivalDestination = null;
            _recentArrivalUntil = -1f;
            _arrivalWasLive = false;
            _campRequestQueued = false;
            _boxStyle = null;
            _titleStyle = null;
            _stateStyle = null;
            _buttonStyle = null;
        }

        private static void PersistPosition(float x, float y)
        {
            if (!_positionConfigBound) return;
            try
            {
                _positionX.Value = x;
                _positionY.Value = y;
                if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.SavePersistedSettings();
            }
            catch { }
        }

        private static void EnsureStyles()
        {
            if (_boxStyle != null) return;
            _boxStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, clipping = TextClipping.Clip };
            _stateStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, clipping = TextClipping.Clip };
            _buttonStyle = new GUIStyle(GUI.skin.button);
        }

        private static void Chat(string message, string color)
        {
            try
            {
                if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "LeftClick")]
    internal static class TravelOverlayLeftClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try { return !TravelStatusOverlay.PointerIsOverOverlay(); }
            catch { return true; }
        }
    }
}
