using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ErenshorFollow
{
    // Compact retained-uGUI travel surface. Ordinary Follow/Lead remain passive status + Stop; Expedition
    // gets a persistent control HUD with Pause/Resume/Cancel and verified-arrival Return/Camp actions.
    // Hiding/closing this UI never cancels expedition runtime state.
    internal static class TravelStatusOverlay
    {
        private enum OverlayMode { None, Follow, Lead, Expedition, Arrival }

        internal const int ExpeditionCanvasSortOrder = 520;
        private const int SortingOrder = ExpeditionCanvasSortOrder;
        private const float Width = 304f;
        private const float NormalHeight = 88f;
        private const float ExpeditionHeight = 148f;
        private const float ArrivalHeight = 140f;
        private const float ScreenMargin = 8f;
        private const float DefaultNormalizedX = 0.015f;
        private const float DefaultNormalizedY = 0.78f;
        private const float FollowWaitingVisibleSeconds = 1.5f;

        private static readonly Color PanelFill = new Color32(4, 23, 32, 218);
        private static readonly Color HeaderFill = new Color32(6, 33, 43, 240);
        private static readonly Color CyanAccent = new Color32(8, 171, 219, 240);
        private static readonly Color TitleCyan = new Color32(143, 224, 255, 255);
        private static readonly Color HintCyan = new Color32(143, 199, 224, 255);
        private static readonly Color ButtonFill = new Color32(9, 43, 56, 230);
        private static readonly Color ButtonHover = new Color32(31, 97, 122, 245);
        private static readonly Color CancelFill = new Color32(73, 54, 30, 235);

        private static GameObject _root, _panelObject;
        private static RectTransform _panel, _inner, _header;
        private static RectTransform _stopRect, _pauseRect, _cancelRect, _campRect, _returnRect, _closeRect;
        private static TextMeshProUGUI _title, _state;
        private static Button _stopButton, _pauseButton, _cancelButton, _campButton, _returnButton, _closeButton;
        private static bool _built;
        private static float _screenW, _screenH;
        private static float _followWaitingSince = -1f;
        private static int _lastSeenSessionId;
        private static int _manualHiddenSessionId;
        private static int _recentArrivalSessionId;
        private static string _recentArrivalLeader;
        private static string _recentArrivalDestination;
        private static string _recentArrivalScene;
        private static bool _arrivalWasLive;
        private static bool _campRequestQueued;
        private static bool _showingCloseableSurface;
        private static float _lastActivatedAt = -1f;

        // Legacy offset values remain load-compatible but retained uGUI intentionally does not
        // reinterpret top-origin pixel offsets as normalized bottom-left coordinates.
        internal static float OffsetX;
        internal static float OffsetY;

        internal static bool IsCloseableVisible
        {
            get { return _showingCloseableSurface && _panelObject != null && _panelObject.activeSelf; }
        }

        internal static bool HasExpeditionStatus
        {
            get
            {
                ExpeditionStatusSnapshot snapshot = ExpeditionCoordinator.GetStatusSnapshot();
                return snapshot.SessionId > 0 || _recentArrivalSessionId > 0;
            }
        }

        internal static float LastActivatedAt { get { return _lastActivatedAt; } }

        internal static void Tick()
        {
            LeaderController.StatusSnapshot lead = LeaderController.GetStatusSnapshot();
            FollowController.StatusSnapshot follow = FollowController.GetStatusSnapshot();
            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            UpdateArrivalCache(expedition);
            OverlayMode mode = ResolveMode(expedition, lead, follow);

            if (mode == OverlayMode.None)
            {
                Hide();
                FollowUiDragGuard.ForceReleaseIfOwned();
                return;
            }

            bool expeditionSurface = mode == OverlayMode.Expedition || mode == OverlayMode.Arrival;
            if (!expeditionSurface && !SuiteUiPolicy.IsGameplayReady())
            {
                Hide();
                FollowUiDragGuard.ForceReleaseIfOwned();
                return;
            }

            int surfaceSessionId = expedition.SessionId > 0 ? expedition.SessionId : _recentArrivalSessionId;
            if (expeditionSurface && surfaceSessionId > 0 && _manualHiddenSessionId == surfaceSessionId)
            {
                Hide();
                return;
            }

            if (!EnsureBuilt()) return;

            bool arrived = mode == OverlayMode.Arrival || (mode == OverlayMode.Expedition && expedition.State == ExpeditionState.Arrived);
            bool campAvailable = arrived && CampmasterIntegrationBridge.IsAvailable;
            bool campActive = campAvailable && CampmasterIntegrationBridge.IsHuntCampActive;
            bool canReturn = arrived && ExpeditionCoordinator.CanReturn();
            ExpeditionUiActionVisibility actions = ExpeditionWorkflowPolicy.ResolveStatusActions(
                expedition.State, expedition.Active, arrived, campAvailable, campActive, _campRequestQueued, canReturn);

            float height = arrived ? ArrivalHeight : (mode == OverlayMode.Expedition ? ExpeditionHeight : NormalHeight);
            Resize(height);
            ApplyPositionIfNeeded();
            RefreshContent(mode, expedition, lead, follow, campActive);
            SetButtonState(mode, expedition, actions, arrived);
            SetInteractiveState(EventSystem.current != null);
            _showingCloseableSurface = expeditionSurface;
            _panelObject.SetActive(true);
        }

        internal static void ShowExpeditionStatus()
        {
            ExpeditionStatusSnapshot snapshot = ExpeditionCoordinator.GetStatusSnapshot();
            int id = snapshot.SessionId > 0 ? snapshot.SessionId : _recentArrivalSessionId;
            if (id > 0 && _manualHiddenSessionId == id) _manualHiddenSessionId = 0;
            TouchActivation();
            Tick();
        }

        internal static bool CloseForSharedQuickClose()
        {
            if (!IsCloseableVisible) return false;
            HideExpeditionStatus();
            return true;
        }

        internal static void HideExpeditionStatus()
        {
            ExpeditionStatusSnapshot snapshot = ExpeditionCoordinator.GetStatusSnapshot();
            int id = snapshot.SessionId > 0 ? snapshot.SessionId : _recentArrivalSessionId;
            if (id > 0) _manualHiddenSessionId = id;
            Hide();
            FollowUiDragGuard.ForceReleaseIfOwned();
        }

        internal static void CancelDragGesture()
        {
            FollowUiDragGuard.ForceReleaseIfOwned();
        }

        // Context/setup UI is destroyed/hidden on readiness loss, but Expedition Status intentionally
        // remains eligible to render while GameData.Zoning is true so the player can see the transition.
        internal static void HideForGameplayTransition()
        {
            FollowUiDragGuard.ForceReleaseIfOwned();
            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            if (!expedition.Active) Hide();
        }

        internal static void ResetForLifecycle()
        {
            FollowUiDragGuard.ForceReleaseIfOwned();
            if (_root != null) { try { UnityEngine.Object.DestroyImmediate(_root); } catch { } }
            _root = _panelObject = null;
            _panel = _inner = _header = null;
            _stopRect = _pauseRect = _cancelRect = _campRect = _returnRect = _closeRect = null;
            _title = _state = null;
            _stopButton = _pauseButton = _cancelButton = _campButton = _returnButton = _closeButton = null;
            _built = false;
            _followWaitingSince = -1f;
            _lastSeenSessionId = 0;
            _manualHiddenSessionId = 0;
            ClearArrivalCache();
            _arrivalWasLive = false;
            _showingCloseableSurface = false;
            _lastActivatedAt = -1f;
            _screenW = _screenH = 0f;
        }

        private static bool EnsureBuilt()
        {
            if (_built) return true;
            try
            {
                _root = new GameObject("ErenshorFollow.TravelStatusRetainedUI");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                _root.AddComponent<GraphicRaycaster>();

                _panelObject = MakePanel("Travel Status", _root.transform, CyanAccent);
                _panel = _panelObject.GetComponent<RectTransform>();
                BaseRect(_panel, Width, NormalHeight);
                _inner = MakeRect("Inner", _panel, Width - 2f, NormalHeight - 2f, 1f, 1f);
                _inner.gameObject.AddComponent<Image>().color = PanelFill;

                _header = MakeRect("Header", _inner, Width - 2f, 30f, 0f, NormalHeight - 32f);
                _header.gameObject.AddComponent<Image>().color = HeaderFill;
                _title = AddText(_header, string.Empty, 13, TextAlignmentOptions.MidlineLeft, TitleCyan, false);
                SetOffsets(_title.rectTransform, 9f, 0f, -44f, 0f);
                FollowUiDragGuard drag = _header.gameObject.AddComponent<FollowUiDragGuard>();
                drag.Target = _panel;
                drag.Activated = TouchActivation;
                drag.Completed = PersistPosition;

                _closeRect = MakeRect("Close", _header, 30f, 24f, Width - 40f, 3f);
                _closeButton = AddButton(_closeRect, "X", HideExpeditionStatus, false);

                RectTransform stateRect = MakeRect("State", _inner, Width - 18f, 58f, 9f, 45f);
                _state = AddText(stateRect, string.Empty, 12, TextAlignmentOptions.TopLeft, HintCyan, true);

                _stopRect = MakeRect("Stop", _inner, 62f, 30f, Width - 74f, 10f);
                _stopButton = AddButton(_stopRect, "Stop", delegate
                {
                    ErenshorFollowPlugin.StopAllTravel("[Erenshor Travel] Travel stopped.", "yellow");
                }, true);

                _pauseRect = MakeRect("Pause", _inner, 82f, 30f, 9f, 10f);
                _pauseButton = AddButton(_pauseRect, "Pause", TogglePause, false);
                _cancelRect = MakeRect("Cancel", _inner, 82f, 30f, 99f, 10f);
                _cancelButton = AddButton(_cancelRect, "Cancel", delegate
                {
                    ExpeditionCoordinator.Cancel("you called it off.");
                }, true);
                _campRect = MakeRect("Camp Here", _inner, 100f, 30f, 9f, 10f);
                _campButton = AddButton(_campRect, "Camp Here", TryCampHere, false);
                _returnRect = MakeRect("Return", _inner, 84f, 30f, 117f, 10f);
                _returnButton = AddButton(_returnRect, "Return", delegate
                {
                    // Preserve the verified-arrival card if return validation fails. A successful
                    // return immediately creates a new expedition session and supersedes it.
                    ExpeditionCoordinator.TryReturn();
                }, false);

                _panelObject.SetActive(false);
                _screenW = Screen.width;
                _screenH = Screen.height;
                ApplyStoredPosition();
                _built = true;
                return true;
            }
            catch
            {
                ResetForLifecycle();
                return false;
            }
        }

        private static void TogglePause()
        {
            ExpeditionStatusSnapshot snapshot = ExpeditionCoordinator.GetStatusSnapshot();
            if (!snapshot.Active) return;
            if (snapshot.State == ExpeditionState.Paused) ExpeditionCoordinator.Resume();
            else ExpeditionCoordinator.Pause(ExpeditionPauseReason.PlayerRequest);
        }

        private static void TryCampHere()
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
        }

        private static void SetButtonState(OverlayMode mode, ExpeditionStatusSnapshot expedition,
            ExpeditionUiActionVisibility actions, bool arrived)
        {
            bool expeditionSurface = mode == OverlayMode.Expedition && expedition.Active;

            SetActive(_stopRect, mode == OverlayMode.Follow || mode == OverlayMode.Lead);
            SetActive(_closeRect, mode == OverlayMode.Expedition || mode == OverlayMode.Arrival);
            SetActive(_pauseRect, expeditionSurface && (actions.ShowPause || actions.ShowResume));
            SetActive(_cancelRect, expeditionSurface && actions.ShowCancel);
            SetActive(_campRect, arrived && actions.ShowCampHere);
            SetActive(_returnRect, arrived && actions.ShowReturn);

            if (_pauseButton != null)
            {
                SetButtonLabel(_pauseButton, actions.ShowResume ? "Resume" : "Pause");
                _pauseButton.interactable = actions.ShowPause || actions.ShowResume;
            }
            if (_returnRect != null)
                _returnRect.anchoredPosition = new Vector2(arrived && actions.ShowCampHere ? 117f : 9f, 10f);
        }

        private static void SetInteractiveState(bool interactive)
        {
            if (_pauseButton != null && _pauseRect != null && _pauseRect.gameObject.activeSelf)
                _pauseButton.interactable = interactive && _pauseButton.interactable;
            if (_cancelButton != null) _cancelButton.interactable = interactive;
            if (_campButton != null) _campButton.interactable = interactive;
            if (_returnButton != null) _returnButton.interactable = interactive;
            if (_closeButton != null) _closeButton.interactable = interactive;
            if (_stopButton != null) _stopButton.interactable = interactive;
        }

        private static void RefreshContent(OverlayMode mode, ExpeditionStatusSnapshot expedition,
            LeaderController.StatusSnapshot lead, FollowController.StatusSnapshot follow, bool campActive)
        {
            string title;
            string state;
            if (mode == OverlayMode.Arrival)
            {
                title = "EXPEDITION COMPLETE";
                state = "Arrived in " + SafeName(_recentArrivalDestination) +
                    (campActive ? "\nHunt camp active" : (_campRequestQueued ? "\nCamp request queued" : string.Empty));
            }
            else if (mode == OverlayMode.Expedition)
            {
                title = expedition.Objective == ExpeditionObjective.Return ? "RETURN EXPEDITION" : "EXPEDITION";
                state = ExpeditionStateText(expedition);
            }
            else if (mode == OverlayMode.Lead)
            {
                title = SafeName(lead.LeaderName) + " leading to " + SafeName(lead.DestinationName);
                state = LeadStateText(lead.State);
            }
            else
            {
                bool rebinding = follow.State == FollowController.DriveState.RebindingAfterZoneChange;
                title = rebinding ? "Follow target: " + SafeName(follow.TargetName) : "Following " + SafeName(follow.TargetName);
                state = FollowStateText(follow.State);
            }

            if (_title != null && _title.text != title) _title.text = title;
            if (_state != null && _state.text != state) _state.text = state;
        }

        private static string ExpeditionStateText(ExpeditionStatusSnapshot expedition)
        {
            string leaderRoute = SafeName(expedition.LeaderName) + " → " + SafeName(expedition.DestinationName);
            string remaining = expedition.RemainingTransitions > 0
                ? expedition.RemainingTransitions + " zone" + (expedition.RemainingTransitions == 1 ? string.Empty : "s") + " remaining"
                : string.Empty;

            switch (expedition.State)
            {
                case ExpeditionState.Transitioning:
                    return leaderRoute + "\nChanging zones...  Reacquiring " + SafeName(expedition.LeaderName) + "..." +
                        (string.IsNullOrWhiteSpace(remaining) ? string.Empty : "\n" + remaining);
                case ExpeditionState.Paused:
                    return leaderRoute + "\nPaused" + NextLine(expedition.NextZone, remaining);
                case ExpeditionState.CombatInterrupted:
                    return leaderRoute + "\nCombat in progress — Erenshor has control" +
                        (string.IsNullOrWhiteSpace(remaining) ? string.Empty : "\n" + remaining);
                case ExpeditionState.Regrouping:
                    return leaderRoute + "\nRegrouping" + NextLine(expedition.NextZone, remaining);
                case ExpeditionState.Arrived:
                    return "Arrived in " + SafeName(expedition.DestinationName);
                default:
                    return leaderRoute + "\nTraveling" + NextLine(expedition.NextZone, remaining);
            }
        }

        private static string NextLine(string next, string remaining)
        {
            string value = string.IsNullOrWhiteSpace(next) ? string.Empty : "\nNext: " + next;
            if (!string.IsNullOrWhiteSpace(remaining)) value += "\n" + remaining;
            return value;
        }

        private static OverlayMode ResolveMode(ExpeditionStatusSnapshot expedition,
            LeaderController.StatusSnapshot lead, FollowController.StatusSnapshot follow)
        {
            if (ExpeditionWorkflowPolicy.ShouldAutoShowForSession(_lastSeenSessionId, expedition.SessionId))
            {
                _lastSeenSessionId = expedition.SessionId;
                _manualHiddenSessionId = 0;
                TouchActivation();
            }

            if (expedition.Active || expedition.State == ExpeditionState.Arrived)
            {
                _followWaitingSince = -1f;
                return OverlayMode.Expedition;
            }
            if (_recentArrivalSessionId > 0 && SameScene(_recentArrivalScene, ActiveScene()))
            {
                _followWaitingSince = -1f;
                return OverlayMode.Arrival;
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
                return OverlayMode.None;
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
            if (expedition.State == ExpeditionState.Arrived && expedition.SessionId > 0)
            {
                if (!_arrivalWasLive || _recentArrivalSessionId != expedition.SessionId)
                {
                    _recentArrivalSessionId = expedition.SessionId;
                    _recentArrivalLeader = expedition.LeaderName;
                    _recentArrivalDestination = expedition.DestinationName;
                    _recentArrivalScene = ActiveScene();
                    _campRequestQueued = false;
                    _manualHiddenSessionId = 0;
                    TouchActivation();
                }
                _arrivalWasLive = true;
                return;
            }
            _arrivalWasLive = false;
            if (expedition.Active)
            {
                // A new outbound/return session supersedes any prior arrival card.
                if (_recentArrivalSessionId != expedition.SessionId) ClearArrivalCache();
                return;
            }
            if (_recentArrivalSessionId > 0 && !SameScene(_recentArrivalScene, ActiveScene())) ClearArrivalCache();
        }

        private static void ClearArrivalCacheVisualOnly()
        {
            _recentArrivalSessionId = 0;
            _recentArrivalLeader = null;
            _recentArrivalDestination = null;
            _recentArrivalScene = null;
            _campRequestQueued = false;
        }

        private static void ClearArrivalCache()
        {
            ClearArrivalCacheVisualOnly();
            _manualHiddenSessionId = 0;
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
                case FollowController.DriveState.RecoveryRepath: return "Repathing safely";
                case FollowController.DriveState.PausedForCombat: return "Paused for real combat";
                case FollowController.DriveState.RecoveringAfterCombat: return "Combat clear - resuming soon";
                case FollowController.DriveState.AwaitingNativeZoneChange: return "Target zoned - waiting for native player transition";
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

        private static void Resize(float height)
        {
            if (_panel == null || _inner == null) return;
            if (Mathf.Abs(_panel.sizeDelta.y - height) < 0.1f) return;
            _panel.sizeDelta = new Vector2(Width, height);
            _inner.sizeDelta = new Vector2(Width - 2f, height - 2f);
            if (_header != null) _header.anchoredPosition = new Vector2(0f, height - 32f);
            RectTransform stateRect = _inner.Find("State") as RectTransform;
            if (stateRect != null)
            {
                bool tall = height > NormalHeight + 1f;
                stateRect.sizeDelta = new Vector2(Width - 18f, tall ? 60f : 34f);
                stateRect.anchoredPosition = new Vector2(9f, tall ? 48f : 9f);
            }
            ClampPanel();
        }

        private static void ApplyPositionIfNeeded()
        {
            if (_screenW == Screen.width && _screenH == Screen.height) return;
            _screenW = Screen.width;
            _screenH = Screen.height;
            ApplyStoredPosition();
        }

        private static void ApplyStoredPosition()
        {
            if (_panel == null) return;
            FollowSettings settings = ErenshorFollowPlugin.Instance == null ? null : ErenshorFollowPlugin.Instance.Settings;
            float storedX = settings == null ? FollowUiPositionPolicy.Unset : settings.OverlayPositionX;
            float storedY = settings == null ? FollowUiPositionPolicy.Unset : settings.OverlayPositionY;
            float x = FollowUiPositionPolicy.ResolveAxis(storedX, DefaultNormalizedX, Screen.width, _panel.sizeDelta.x);
            float y = FollowUiPositionPolicy.ResolveAxis(storedY, DefaultNormalizedY, Screen.height, _panel.sizeDelta.y);
            _panel.anchoredPosition = new Vector2(x, y);
            ClampPanel();
        }

        private static void ClampPanel()
        {
            if (_panel == null) return;
            TravelOverlayPoint p = TravelOverlayLogic.ClampPosition(_panel.anchoredPosition.x, _panel.anchoredPosition.y,
                _panel.sizeDelta.x, _panel.sizeDelta.y, Screen.width, Screen.height, ScreenMargin);
            _panel.anchoredPosition = new Vector2(p.X, p.Y);
        }

        private static void PersistPosition()
        {
            if (_panel == null || ErenshorFollowPlugin.Instance == null || ErenshorFollowPlugin.Instance.Settings == null) return;
            ClampPanel();
            FollowSettings settings = ErenshorFollowPlugin.Instance.Settings;
            settings.OverlayPositionX = FollowUiPositionPolicy.NormalizeAxis(_panel.anchoredPosition.x, Screen.width);
            settings.OverlayPositionY = FollowUiPositionPolicy.NormalizeAxis(_panel.anchoredPosition.y, Screen.height);
            ErenshorFollowPlugin.Instance.SavePersistedSettings();
        }

        private static void Hide()
        {
            _showingCloseableSurface = false;
            if (_panelObject != null) _panelObject.SetActive(false);
        }

        private static void TouchActivation() { _lastActivatedAt = Time.unscaledTime; }

        private static string ActiveScene()
        {
            try { return SceneManager.GetActiveScene().name; }
            catch { return null; }
        }

        private static bool SameScene(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
                a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeName(string value) { return string.IsNullOrWhiteSpace(value) ? "destination" : value; }

        private static void Chat(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
        }

        private static void SetActive(RectTransform rt, bool active)
        {
            if (rt != null) rt.gameObject.SetActive(active);
        }

        private static void SetButtonLabel(Button button, string text)
        {
            if (button == null) return;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && label.text != text) label.text = text;
        }

        private static GameObject MakePanel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.interactable = true;
            group.blocksRaycasts = true;
            return go;
        }

        private static void BaseRect(RectTransform rt, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);
        }

        private static RectTransform MakeRect(string name, Transform parent, float width, float height, float x, float y)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            BaseRect(rt, width, height);
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        private static Button AddButton(RectTransform rt, string text, UnityEngine.Events.UnityAction action, bool caution)
        {
            Image image = rt.gameObject.AddComponent<Image>();
            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(delegate { TouchActivation(); action(); });
            ColorBlock colors = button.colors;
            colors.normalColor = caution ? CancelFill : ButtonFill;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = CyanAccent;
            colors.selectedColor = ButtonHover;
            colors.disabledColor = new Color32(8, 31, 40, 145);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            image.color = Color.white;
            AddText(rt, text, 12, TextAlignmentOptions.Center, Color.white, false);
            return button;
        }

        private static TextMeshProUGUI AddText(RectTransform parent, string text, int size,
            TextAlignmentOptions align, Color color, bool wrap)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = align;
            label.color = color;
            label.raycastTarget = false;
            label.enableWordWrapping = wrap;
            label.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            return label;
        }

        private static void SetOffsets(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }
    }
}
