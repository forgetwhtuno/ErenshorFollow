using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ErenshorFollow
{
    // Retained contextual Sim Actions UI. Native clicks are observed after Erenshor resolves a
    // target; the patches never suppress native clicks. uGUI/EventSystem owns pointer containment.
    internal static class SimActionMenu
    {
        internal const int CanvasSortOrder = 530;
        private const float Width = SimActionMenuLayoutPolicy.Width;
        private const float MinHeight = SimActionMenuLayoutPolicy.MinimumPanelHeight;
        private const float Edge = 8f;
        private const string UiSurfaceId = "follow-sim-actions-r2";

        private static readonly Color PanelFill = new Color32(4, 23, 32, 220);
        private static readonly Color HeaderFill = new Color32(6, 33, 43, 240);
        private static readonly Color ButtonFill = new Color32(9, 43, 56, 230);
        private static readonly Color ButtonHover = new Color32(31, 97, 122, 245);
        private static readonly Color ButtonPressed = new Color32(8, 171, 219, 245);
        private static readonly Color CyanAccent = new Color32(8, 171, 219, 245);
        private static readonly Color TitleCyan = new Color32(143, 224, 255, 255);
        private static readonly Color HintCyan = new Color32(143, 199, 224, 255);
        private static readonly Color StopFill = new Color32(73, 54, 30, 235);

        private static SimPlayer _selected;
        private static string _selectedName;
        private static bool _open;
        private static float _lastActivatedAt = -1f;
        private static bool _nativeLeftClickActive;
        private static SimPlayer _nativeLeftClickTarget;
        private static GameObject _root;
        private static GameObject _panelObject;
        private static RectTransform _panel;
        private static RectTransform _content;
        private static int _lastUiSignature;
        private static int _actionRowCount;
        private static int _sectionRowCount;

        internal static bool IsOpen { get { return _open; } }
        internal static float LastActivatedAt { get { return _lastActivatedAt; } }

        internal static string DiagnosticStatus()
        {
            string selected = string.IsNullOrWhiteSpace(_selectedName) ? "(none)" : _selectedName;
            string eventSystem = EventSystem.current == null ? "missing" : EventSystem.current.name;
            return "[Erenshor Follow] customSimActions=" + (_open ? "open" : "closed") +
                " selected=" + selected +
                " uiRevision=" + SimActionMenuLayoutPolicy.UiRevision +
                " surface=" + UiSurfaceId +
                " rowHeight=" + SimActionMenuLayoutPolicy.ActionRowHeight.ToString("0") +
                " eventSystem=" + eventSystem +
                ". Note: Erenshor's native Attack/Assist/Pull/Guard party-command stack is a separate game UI.";
        }

        internal static void Tick()
        {
            if (!_open) return;
            if (!SuiteUiPolicy.IsGameplayReady() || !IsSelectedEligible())
            {
                Close("selected Sim became invalid or left party");
                return;
            }
            if (EventSystem.current == null)
            {
                Close("EventSystem unavailable");
                return;
            }

            int signature = ComputeUiSignature();
            if (signature != _lastUiSignature) RebuildContent();
            ClampPanelToScreen();
        }

        internal static bool CloseForSharedQuickClose()
        {
            if (!_open) return false;
            Close("shared quick close");
            return true;
        }

        internal static void ForceCloseForLifecycle()
        {
            Close("gameplay not ready");
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            FollowUiDragGuard.ForceReleaseIfOwned();
        }

        internal static void DisposeForLifecycle()
        {
            ForceCloseForLifecycle();
            if (_root != null) { try { UnityEngine.Object.DestroyImmediate(_root); } catch { } }
            _root = null;
            _panelObject = null;
            _panel = null;
            _content = null;
            _lastActivatedAt = -1f;
            _lastUiSignature = 0;
        }

        // Observation-only bracket around native LeftClick. It never returns false or consumes the
        // click; retained uGUI is responsible for native UI containment.
        internal static void BeginNativeLeftClick()
        {
            _nativeLeftClickTarget = null;
            _nativeLeftClickActive = !PointerIsOverUi();
            if (_open && _nativeLeftClickActive) Close("outside world click");
        }

        internal static void ObserveNativeTarget(Character character)
        {
            if (!_nativeLeftClickActive || character == null) return;
            SimPlayer sim = null;
            try { sim = character.GetComponent<SimPlayer>(); } catch { }
            _nativeLeftClickTarget = sim;
        }

        internal static void CompleteNativeLeftClick()
        {
            if (!_nativeLeftClickActive) return;
            SimPlayer selected = _nativeLeftClickTarget;
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            if (selected != null) TryOpen(selected, "native left click");
        }

        private static bool PointerIsOverUi()
        {
            try { return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }
            catch { return false; }
        }

        private static void TryOpen(SimPlayer candidate, string source)
        {
            if (candidate == null) return;
            FollowActorEligibility eligibility = Evaluate(candidate);
            if (eligibility != FollowActorEligibility.Eligible)
            {
                Debug("menu not opened; eligibility=" + eligibility.ToString());
                return;
            }
            if (EventSystem.current == null)
            {
                Debug("menu not opened; EventSystem unavailable");
                return;
            }

            _selected = candidate;
            _selectedName = FollowController.ReadName(candidate);
            RefreshActionState();
            if (!EnsureBuilt()) return;
            RebuildContent();
            PlaceNearPointer();
            _open = true;
            _panelObject.SetActive(true);
            TouchActivation();
            Debug("menu opened for " + _selectedName + " via " + source);
        }

        private static FollowActorEligibility Evaluate(SimPlayer sim)
        {
            bool usable = FollowController.IsUsableSim(sim);
            bool remote = usable && CoopCompatibility.IsRemoteHuman(sim);
            bool party = usable && !remote && LeaderController.IsPlayerPartySim(sim);
            return FollowActorEligibilityPolicy.Evaluate(usable, remote, party);
        }

        private static bool IsSelectedEligible()
        {
            return Evaluate(_selected) == FollowActorEligibility.Eligible;
        }

        private static void RefreshActionState()
        {
            // Normal expedition destination discovery lives in ExpeditionSetupWindow. Sim Actions stays
            // intentionally small and never snapshots adjacent-only destinations anymore.
        }

        private static int ComputeUiSignature()
        {
            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            unchecked
            {
                int h = expedition.Active ? 17 : 3;
                h = h * 31 + (int)expedition.State;
                h = h * 31 + (TravelStatusOverlay.HasExpeditionStatus ? 1 : 0);
                h = h * 31 + (FollowController.IsFollowingTarget(_selected) ? 1 : 0);
                h = h * 31 + (CanChallengeSelectedDuel() ? 1 : 0);
                return h;
            }
        }

        private static bool EnsureBuilt()
        {
            if (_root != null && _panel != null) return true;
            try
            {
                _root = new GameObject("ErenshorFollow.SimActionsRetainedUI");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = CanvasSortOrder;
                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                _root.AddComponent<GraphicRaycaster>();

                _panelObject = MakePanel("Sim Actions", _root.transform, CyanAccent);
                _panel = _panelObject.GetComponent<RectTransform>();
                BaseRect(_panel, Width, MinHeight);

                RectTransform inner = MakeRect("Inner", _panel, Width - 2f, MinHeight - 2f, 1f, 1f);
                Image innerImage = inner.gameObject.AddComponent<Image>();
                innerImage.color = PanelFill;

                RectTransform header = MakeRect("Header", inner, Width - 2f, SimActionMenuLayoutPolicy.HeaderHeight, 0f,
                    MinHeight - SimActionMenuLayoutPolicy.HeaderHeight - 2f);
                header.gameObject.AddComponent<Image>().color = HeaderFill;
                TextMeshProUGUI title = AddText(header, "SIM ACTIONS", 16, TextAlignmentOptions.TopLeft, TitleCyan);
                SetOffsets(title.rectTransform, 9f, 22f, -38f, -4f);
                TextMeshProUGUI name = AddText(header, string.Empty, 14, TextAlignmentOptions.BottomLeft, Color.white);
                name.gameObject.name = "Selected Name";
                SetOffsets(name.rectTransform, 9f, 4f, -38f, -23f);
                FollowUiDragGuard drag = header.gameObject.AddComponent<FollowUiDragGuard>();
                drag.Target = _panel;
                drag.Activated = TouchActivation;
                drag.Completed = ClampPanelToScreen;
                RectTransform close = MakeRect("Close", header, 28f, 24f, Width - 34f,
                    SimActionMenuLayoutPolicy.HeaderHeight - 30f);
                AddButton(close, "X", delegate { Close("close button"); }, false);

                RectTransform viewport = MakeRect("Viewport", inner, Width - 14f,
                    SimActionMenuLayoutPolicy.ResolveViewportHeight(MinHeight), 6f, 6f);
                Image viewportImage = viewport.gameObject.AddComponent<Image>();
                viewportImage.color = new Color32(3, 18, 25, 170);
                viewport.gameObject.AddComponent<RectMask2D>();
                ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
                GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                _content = contentObject.GetComponent<RectTransform>();
                _content.SetParent(viewport, false);
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.anchoredPosition = Vector2.zero;
                _content.sizeDelta = Vector2.zero;
                VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(4, 4, 3, 3);
                layout.spacing = SimActionMenuLayoutPolicy.RowSpacing;
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewport;
                scroll.content = _content;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 22f;
                _panelObject.SetActive(false);
                return true;
            }
            catch (Exception ex)
            {
                Debug("retained UI build failed: " + ex.GetType().Name);
                DisposeForLifecycle();
                return false;
            }
        }

        private static void RebuildContent()
        {
            if (_content == null || _panel == null) return;
            RefreshActionState();
            _actionRowCount = 0;
            _sectionRowCount = 0;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_content.GetChild(i).gameObject);

            TextMeshProUGUI selectedName = FindText(_panel, "Selected Name");
            if (selectedName != null) selectedName.text = string.IsNullOrWhiteSpace(_selectedName) ? "LOCAL PARTY SIM" : _selectedName;

            ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
            if (!expedition.Active)
            {
                if (FollowController.IsFollowingTarget(_selected))
                {
                    AddAction("Stop Following", delegate
                    {
                        FollowController.Stop();
                        Say("[Erenshor Follow] Following stopped.", "yellow");
                        Close("Stop follow action");
                    }, true);
                }
                else
                {
                    AddAction("Follow " + _selectedName, delegate
                    {
                        LeaderController.Stop(null);
                        FollowController.Start(_selected, _selectedName);
                        Say("[Erenshor Follow] Following " + _selectedName + ". Press a movement key to stop.", "lightblue");
                        Close("Follow action");
                    }, false);
                }

                AddAction("Create Expedition", delegate
                {
                    SimPlayer leader = _selected;
                    Close("open expedition setup");
                    if (!ExpeditionSetupWindow.Open(leader))
                        Say("[Erenshor Expedition] Expedition setup could not open for that Sim.", "yellow");
                }, false);

                if (TravelStatusOverlay.HasExpeditionStatus)
                    AddAction("Open Expedition Status", delegate
                    {
                        TravelStatusOverlay.ShowExpeditionStatus();
                        Close("open expedition status");
                    }, false);

                if (CanChallengeSelectedDuel())
                {
                    AddAction("Challenge to Practice Duel", delegate
                    {
                        if (!TryStartDuel()) Say("[Practice Duel] That Sim is no longer eligible for a practice duel.", "yellow");
                        Close("Duel action");
                    }, false);
                }
            }
            else
            {
                AddSection("EXPEDITION  •  " + SafeText(expedition.LeaderName) + " → " + SafeText(expedition.DestinationName));
                AddSection(ExpeditionCoordinator.DescribeState(expedition.State));
                AddAction("Open Expedition Status", delegate
                {
                    TravelStatusOverlay.ShowExpeditionStatus();
                    Close("open expedition status");
                }, false);
            }

            FollowController.StatusSnapshot follow = FollowController.GetStatusSnapshot();
            LeaderController.StatusSnapshot lead = LeaderController.GetStatusSnapshot();
            bool selectedIsCurrentFollow = FollowController.IsFollowingTarget(_selected);
            if (!selectedIsCurrentFollow && FollowStartTransitionPolicy.ShouldOfferGenericStop(expedition.Active, follow.Active, lead.Active))
            {
                AddAction("Stop current follow / lead", delegate
                {
                    ErenshorFollowPlugin.StopAllTravel("[Erenshor Travel] Travel stopped.", "yellow");
                    Close("Stop action");
                }, true);
            }
            AddAction("Cancel", delegate { Close("Cancel button"); }, false);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            float height = SimActionMenuLayoutPolicy.ResolvePanelHeight(_actionRowCount, _sectionRowCount, Screen.height);
            ResizePanel(height);
            _lastUiSignature = ComputeUiSignature();
        }

        private static void ResizePanel(float height)
        {
            if (_panel == null) return;
            _panel.sizeDelta = new Vector2(Width, height);
            RectTransform inner = _panel.Find("Inner") as RectTransform;
            if (inner == null) return;
            inner.sizeDelta = new Vector2(Width - 2f, height - 2f);
            RectTransform header = inner.Find("Header") as RectTransform;
            if (header != null) header.anchoredPosition = new Vector2(0f,
                height - SimActionMenuLayoutPolicy.HeaderHeight - 2f);
            RectTransform viewport = inner.Find("Viewport") as RectTransform;
            if (viewport != null) viewport.sizeDelta = new Vector2(Width - 14f,
                SimActionMenuLayoutPolicy.ResolveViewportHeight(height));
        }

        private static void AddSection(string text)
        {
            _sectionRowCount++;
            RectTransform row = MakeLayoutRow("Section", SimActionMenuLayoutPolicy.SectionRowHeight);
            AddText(row, text ?? string.Empty, 11, TextAlignmentOptions.MidlineLeft, HintCyan);
        }

        private static void AddAction(string label, UnityEngine.Events.UnityAction action, bool caution)
        {
            _actionRowCount++;
            RectTransform row = MakeLayoutRow("Action", SimActionMenuLayoutPolicy.ActionRowHeight);
            AddButton(row, label, delegate { TouchActivation(); action(); }, caution);
        }

        private static RectTransform MakeLayoutRow(string name, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(_content, false);
            LayoutElement element = go.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            // Be explicit: the VerticalLayoutGroup may control row height, but no row is allowed to
            // absorb leftover viewport space. This keeps the live revision-2 menu at its intended
            // 28px action-row metric even on Unity layouts that otherwise honor flexibleHeight.
            element.flexibleHeight = 0f;
            return rt;
        }

        private static void PlaceNearPointer()
        {
            if (_panel == null) return;
            Vector3 mouse = Input.mousePosition;
            _panel.anchoredPosition = new Vector2(mouse.x + 6f, mouse.y - _panel.sizeDelta.y * 0.30f);
            ClampPanelToScreen();
        }

        private static void ClampPanelToScreen()
        {
            if (_panel == null) return;
            Vector2 size = _panel.sizeDelta;
            Vector2 p = _panel.anchoredPosition;
            p.x = Mathf.Clamp(p.x, Edge, Mathf.Max(Edge, Screen.width - size.x - Edge));
            p.y = Mathf.Clamp(p.y, Edge, Mathf.Max(Edge, Screen.height - size.y - Edge));
            _panel.anchoredPosition = p;
        }

        private static void TouchActivation()
        {
            _lastActivatedAt = Time.unscaledTime;
        }

        private static bool CanChallengeSelectedDuel()
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_selectedName)) return false;
            try
            {
                Type api = FindDuelControlApi();
                if (api == null) return false;
                MethodInfo getBasicState = api.GetMethod("GetBasicState", BindingFlags.Public | BindingFlags.Static);
                if (getBasicState == null) return false;
                object state = getBasicState.Invoke(null, null);
                if (state == null) return false;
                FieldInfo canStartField = state.GetType().GetField("CanStart", BindingFlags.Public | BindingFlags.Instance);
                if (canStartField != null && !Convert.ToBoolean(canStartField.GetValue(state))) return false;
                FieldInfo activeField = state.GetType().GetField("Active", BindingFlags.Public | BindingFlags.Instance);
                if (activeField != null && Convert.ToBoolean(activeField.GetValue(state))) return false;
                FieldInfo namesField = state.GetType().GetField("EligibleNames", BindingFlags.Public | BindingFlags.Instance);
                string[] names = namesField == null ? null : namesField.GetValue(state) as string[];
                if (names == null) return false;
                for (int i = 0; i < names.Length; i++)
                    if (string.Equals(names[i], _selectedName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        private static bool TryStartDuel()
        {
            try
            {
                Type api = FindDuelControlApi();
                if (api == null) return false;
                MethodInfo challenge = api.GetMethod("TryChallenge", BindingFlags.Public | BindingFlags.Static);
                if (challenge == null) return false;
                object accepted = challenge.Invoke(null, new object[] { _selectedName });
                return accepted is bool && (bool)accepted;
            }
            catch (Exception ex)
            {
                Say("[Practice Duel] Could not request duel: " + ex.GetType().Name + ".", "yellow");
            }
            return false;
        }

        private static Type FindDuelControlApi()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type api = assemblies[i] == null ? null : assemblies[i].GetType("ErenshorDuel.DuelControlApi", false);
                if (api == null) continue;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                FieldInfo version = api.GetField("ApiVersion", flags);
                PropertyInfo available = api.GetProperty("IsAvailable", flags);
                if (version == null || version.FieldType != typeof(int) ||
                    available == null || available.PropertyType != typeof(bool))
                    continue;

                int apiVersion;
                bool isAvailable;
                try
                {
                    apiVersion = (int)version.GetValue(null);
                    isAvailable = (bool)available.GetValue(null, null);
                }
                catch { continue; }

                if (apiVersion == 1 && isAvailable) return api;
            }
            return null;
        }

        private static void Close(string reason)
        {
            if (!_open && _selected == null) return;
            string previous = _selectedName;
            _open = false;
            _selected = null;
            _selectedName = null;
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            if (_panelObject != null) _panelObject.SetActive(false);
            FollowUiDragGuard.ForceReleaseIfOwned();
            Debug("menu closed" + (string.IsNullOrWhiteSpace(previous) ? string.Empty : " for " + previous) + "; reason=" + reason);
        }

        private static string SafeText(string value) { return string.IsNullOrWhiteSpace(value) ? "?" : value; }

        private static void Debug(string message)
        {
            if (!ErenshorFollowPlugin.VerboseDiagnostics) return;
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogDebug("SimActionMenu: " + message); } catch { }
        }

        private static void Say(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
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

        private static Button AddButton(RectTransform rt, string label, UnityEngine.Events.UnityAction action, bool caution)
        {
            Image image = rt.gameObject.GetComponent<Image>();
            if (image == null) image = rt.gameObject.AddComponent<Image>();
            Button button = rt.gameObject.GetComponent<Button>();
            if (button == null) button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.normalColor = caution ? StopFill : ButtonFill;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.selectedColor = ButtonHover;
            colors.disabledColor = new Color32(8, 31, 40, 145);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            image.color = colors.normalColor;
            AddText(rt, label, 13, TextAlignmentOptions.Center, Color.white);
            return button;
        }

        private static TextMeshProUGUI AddText(RectTransform parent, string text, int size, TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(5f, 1f);
            rt.offsetMax = new Vector2(-5f, -1f);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private static void SetOffsets(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }

        private static TextMeshProUGUI FindText(RectTransform parent, string objectName)
        {
            if (parent == null) return null;
            TextMeshProUGUI[] labels = parent.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != null && labels[i].gameObject.name == objectName) return labels[i];
            return null;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "LeftClick")]
    internal static class SimActionMenuLeftClickPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { SimActionMenu.BeginNativeLeftClick(); } catch { }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try { SimActionMenu.CompleteNativeLeftClick(); } catch { }
        }
    }

    [HarmonyPatch(typeof(Character), "TargetMe")]
    internal static class SimActionMenuTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            try { SimActionMenu.ObserveNativeTarget(__instance); } catch { }
        }
    }
}
