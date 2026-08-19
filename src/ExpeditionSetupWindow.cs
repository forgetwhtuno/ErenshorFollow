using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ErenshorFollow
{
    // Dedicated retained-uGUI expedition planner. It owns presentation and a persistent SimPlayerTracking
    // selection only; route execution remains in ExpeditionCoordinator/LeaderController and every current
    // leg still requires a live Zoneline at Start and again after each native zone transition.
    internal static class ExpeditionSetupWindow
    {
        internal const int CanvasSortOrder = 540;
        private const float Width = 470f;
        private const float Height = 410f;
        private const float Edge = 8f;
        private const float RouteRefreshSeconds = 1f;

        private static readonly Color PanelFill = new Color32(4, 23, 32, 228);
        private static readonly Color HeaderFill = new Color32(6, 33, 43, 245);
        private static readonly Color SectionFill = new Color32(3, 18, 25, 185);
        private static readonly Color ButtonFill = new Color32(9, 43, 56, 235);
        private static readonly Color ButtonHover = new Color32(31, 97, 122, 245);
        private static readonly Color ButtonPressed = new Color32(8, 171, 219, 245);
        private static readonly Color CyanAccent = new Color32(8, 171, 219, 245);
        private static readonly Color SelectedFill = new Color32(13, 84, 107, 245);
        private static readonly Color TitleCyan = new Color32(143, 224, 255, 255);
        private static readonly Color HintCyan = new Color32(143, 199, 224, 255);

        private static readonly List<ExpeditionRouteChoice> Choices = new List<ExpeditionRouteChoice>();
        private static SimPlayerTracking _leaderTracking;
        private static string _leaderName;
        private static string _originZone;
        private static string _selectedDestination;
        private static List<string> _selectedRoute = new List<string>();
        private static bool _open;
        private static float _lastActivatedAt = -1f;
        private static float _nextRouteRefresh;
        // A definitive Start rejection is sticky: it survives the per-frame RefreshLeaderAdmission()
        // hint-text refresh until the player takes an action that could change the outcome (picks a
        // different destination, retries Start, or closes the window). Without this, the reason set by
        // StartSelected() was overwritten by the generic hint on the very next Tick() -- a one-frame
        // flash that was the actual root cause of "no visible result" on a rejected Start.
        private static string _rejectionMessage;

        private static GameObject _root, _panelObject;
        private static RectTransform _panel, _destinationContent;
        private static TextMeshProUGUI _leaderText, _destinationText, _routeText, _estimateText, _messageText;
        private static Button _startButton;

        internal static bool IsOpen { get { return _open; } }
        internal static float LastActivatedAt { get { return _lastActivatedAt; } }

        internal static bool Open(SimPlayer leader)
        {
            if (leader == null || !SuiteUiPolicy.IsGameplayReady() || EventSystem.current == null) return false;
            SimPlayerTracking tracking = SimTrackingRebind.Capture(leader);
            if (!LeaderMatches(tracking, leader))
            {
                Chat("[Erenshor Expedition] That Sim is no longer a living local party member.", "yellow");
                return false;
            }

            _leaderTracking = tracking;
            _leaderName = FollowController.ReadName(leader);
            _originZone = ActiveScene();
            _selectedDestination = null;
            _selectedRoute.Clear();
            _nextRouteRefresh = 0f;
            _rejectionMessage = null;

            if (!EnsureBuilt()) return false;
            RefreshRoutes(true);
            RefreshStaticText();
            _open = true;
            _panelObject.SetActive(true);
            PlaceCentered();
            TouchActivation();
            return true;
        }

        internal static void Tick()
        {
            if (!_open) return;
            if (!SuiteUiPolicy.IsGameplayReady() || EventSystem.current == null)
            {
                Close("gameplay not ready");
                return;
            }
            if (ExpeditionCoordinator.IsActive)
            {
                Close("expedition started elsewhere");
                return;
            }
            if (!SameZone(_originZone, ActiveScene()))
            {
                Close("zone changed while planning");
                return;
            }

            if (Time.unscaledTime >= _nextRouteRefresh)
            {
                _nextRouteRefresh = Time.unscaledTime + RouteRefreshSeconds;
                RefreshRoutes(false);
            }
            RefreshLeaderAdmission();
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
            FollowUiDragGuard.ForceReleaseIfOwned();
        }

        internal static void DisposeForLifecycle()
        {
            ForceCloseForLifecycle();
            if (_root != null) { try { UnityEngine.Object.DestroyImmediate(_root); } catch { } }
            _root = _panelObject = null;
            _panel = _destinationContent = null;
            _leaderText = _destinationText = _routeText = _estimateText = _messageText = null;
            _startButton = null;
            _lastActivatedAt = -1f;
        }

        private static void RefreshRoutes(bool rebuildAlways)
        {
            string previous = _selectedDestination;
            List<string> liveFirstHops = ExpeditionDestinationResolver.ListCanonicalNames();
            List<ExpeditionRouteChoice> current = ZoneAtlasRoutePlanner.ListReachableRoutes(_originZone, liveFirstHops);

            bool changed = rebuildAlways || current.Count != Choices.Count;
            if (!changed)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    if (!SameRouteChoice(current[i], Choices[i])) { changed = true; break; }
                }
            }

            Choices.Clear();
            Choices.AddRange(current);
            bool hadExplicitSelection = !string.IsNullOrWhiteSpace(previous);
            ExpeditionRouteChoice selected = FindChoice(previous);
            // Initial open may select the first valid row for convenience. After the player has made/seen
            // a selection, a live route disappearing must never silently switch the final destination.
            if (selected == null && ExpeditionWorkflowPolicy.ShouldAutoSelectReplacement(hadExplicitSelection) && Choices.Count > 0) selected = Choices[0];
            ApplySelection(selected);
            if (changed) RebuildDestinationList();
        }

        private static bool SameRouteChoice(ExpeditionRouteChoice a, ExpeditionRouteChoice b)
        {
            if (a == null || b == null) return a == b;
            if (!string.Equals(a.DestinationName, b.DestinationName, StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Route.Count != b.Route.Count) return false;
            for (int i = 0; i < a.Route.Count; i++)
                if (!string.Equals(a.Route[i], b.Route[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static ExpeditionRouteChoice FindChoice(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) return null;
            for (int i = 0; i < Choices.Count; i++)
                if (Choices[i].DestinationName.Equals(destination, StringComparison.OrdinalIgnoreCase)) return Choices[i];
            return null;
        }

        private static void ApplySelection(ExpeditionRouteChoice choice)
        {
            _selectedDestination = choice == null ? null : choice.DestinationName;
            _selectedRoute = choice == null ? new List<string>() : new List<string>(choice.Route);
            RefreshPreview();
        }

        private static void SelectDestination(string destination)
        {
            ExpeditionRouteChoice choice = FindChoice(destination);
            if (choice == null) return;
            // A new choice is a fresh attempt; a rejection reason tied to the old destination no longer
            // applies and must not keep shadowing the (now neutral) hint text.
            _rejectionMessage = null;
            ApplySelection(choice);
            RebuildDestinationList();
            TouchActivation();
        }

        private static void StartSelected()
        {
            if (string.IsNullOrWhiteSpace(_selectedDestination)) return;
            _rejectionMessage = null;
            // Immediate, synchronous, truthful feedback the instant the click is processed: this is not
            // a claim of success, only that an attempt is in flight. Disabling the button also prevents
            // a second click from racing a result that resolves in the same frame.
            if (_startButton != null) _startButton.interactable = false;
            SetMessage("Starting expedition to " + _selectedDestination + "...", false);

            string failure;
            ExpeditionStartOutcome outcome;
            if (!ExpeditionCoordinator.TryStartRouteExact(_leaderTracking, _selectedDestination,
                ExpeditionInitiation.ActionMenu, out failure, out outcome))
            {
                _rejectionMessage = string.IsNullOrWhiteSpace(failure) ? DefaultRejectionText(outcome) : failure;
                SetMessage(_rejectionMessage, true);
                // Refresh live route/leader state (the failure may be stale-data driven), but the setup
                // window itself always stays open on a rejection -- no silent close, no auto-retry.
                RefreshRoutes(true);
                RefreshLeaderAdmission();
                return;
            }

            // Accepted: native leg start already returned true, and TryStartPrepared has already flipped
            // the session to Traveling and announced it in chat. Hand off to the persistent status panel
            // rather than leaving the player looking at a setup window for an expedition already under way.
            Close("expedition started");
            TravelStatusOverlay.ShowExpeditionStatus();
        }

        // Fallback wording only: every current rejection path already supplies a specific failure string,
        // so this keys off the typed outcome purely as a safety net if one somehow does not.
        private static string DefaultRejectionText(ExpeditionStartOutcome outcome)
        {
            switch (outcome)
            {
                case ExpeditionStartOutcome.AlreadyActive: return "Another expedition is already active.";
                case ExpeditionStartOutcome.InvalidLeader: return "The intended leader is no longer available.";
                case ExpeditionStartOutcome.NoRoute: return "No safe route is currently available to that destination.";
                case ExpeditionStartOutcome.NotReady: return "Finish combat before starting an expedition.";
                default: return "The expedition could not start safely.";
            }
        }

        private static void RefreshLeaderAdmission()
        {
            SimPlayer avatar = SimTrackingRebind.CurrentAvatar(_leaderTracking);
            bool valid = LeaderMatches(_leaderTracking, avatar);
            if (_startButton != null) _startButton.interactable = valid && _selectedRoute.Count >= 2;
            if (!valid)
            {
                // A live blocking condition always supersedes a stale rejection reason -- it is a fresher,
                // more specific truth about why Start would fail again right now.
                _rejectionMessage = null;
                SetMessage("Leader unavailable: the exact selected Sim must still be alive, local, and in your party.", true);
            }
            else if (Choices.Count == 0)
            {
                _rejectionMessage = null;
                SetMessage("No reachable atlas destinations currently begin through a verified live zoneline.", false);
            }
            else if (_rejectionMessage != null)
            {
                // Keep showing the specific reason from the last Start attempt. This is the fix for the
                // actual reported bug: Tick() calls this every frame, and the previous version always fell
                // through to the generic hint below, so a real rejection was visible for at most one frame.
                SetMessage(_rejectionMessage, true);
            }
            else
            {
                SetMessage("Each current leg is revalidated against a live zoneline before travel.", false);
            }
        }

        private static bool LeaderMatches(SimPlayerTracking tracking, SimPlayer avatar)
        {
            if (tracking == null || avatar == null) return false;
            if (!SimTrackingRebind.AvatarMatchesTracking(tracking, avatar)) return false;
            if (!SimTrackingRebind.TrackingIsInPlayerGroup(tracking)) return false;
            if (!FollowController.IsUsableSim(avatar)) return false;
            if (CoopCompatibility.IsRemoteHuman(avatar)) return false;
            return LeaderController.IsPlayerPartySim(avatar);
        }

        private static bool EnsureBuilt()
        {
            if (_root != null && _panel != null) return true;
            try
            {
                _root = new GameObject("ErenshorFollow.ExpeditionSetupRetainedUI");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = CanvasSortOrder;
                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                _root.AddComponent<GraphicRaycaster>();

                _panelObject = MakePanel("Expedition Setup", _root.transform, CyanAccent);
                _panel = _panelObject.GetComponent<RectTransform>();
                BaseRect(_panel, Width, Height);

                RectTransform inner = MakeRect("Inner", _panel, Width - 2f, Height - 2f, 1f, 1f);
                inner.gameObject.AddComponent<Image>().color = PanelFill;

                RectTransform header = MakeRect("Header", inner, Width - 2f, 42f, 0f, Height - 44f);
                header.gameObject.AddComponent<Image>().color = HeaderFill;
                TextMeshProUGUI title = AddText(header, "EXPEDITION", 16, TextAlignmentOptions.MidlineLeft, TitleCyan, false);
                SetOffsets(title.rectTransform, 11f, 0f, -44f, 0f);
                FollowUiDragGuard drag = header.gameObject.AddComponent<FollowUiDragGuard>();
                drag.Target = _panel;
                drag.Activated = TouchActivation;
                drag.Completed = ClampPanelToScreen;
                RectTransform close = MakeRect("Close", header, 30f, 26f, Width - 38f, 8f);
                AddButton(close, "X", delegate { Close("close button"); }, false);

                RectTransform leaderLabel = MakeRect("LeaderLabel", inner, 64f, 34f, 12f, Height - 82f);
                AddText(leaderLabel, "Leader", 11, TextAlignmentOptions.TopLeft, HintCyan, false);
                RectTransform leaderValue = MakeRect("LeaderValue", inner, Width - 94f, 34f, 76f, Height - 82f);
                _leaderText = AddText(leaderValue, string.Empty, 14, TextAlignmentOptions.TopLeft, Color.white, false);

                RectTransform left = MakeRect("DestinationPane", inner, 180f, 256f, 12f, 64f);
                left.gameObject.AddComponent<Image>().color = SectionFill;
                RectTransform destinationHeader = MakeRect("DestinationHeader", left, 164f, 26f, 8f, 222f);
                AddText(destinationHeader, "DESTINATION", 11, TextAlignmentOptions.MidlineLeft, HintCyan, false);
                RectTransform viewport = MakeRect("DestinationViewport", left, 164f, 214f, 8f, 7f);
                viewport.gameObject.AddComponent<Image>().color = new Color32(2, 15, 21, 150);
                viewport.gameObject.AddComponent<RectMask2D>();
                ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
                GameObject contentObject = new GameObject("DestinationContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                _destinationContent = contentObject.GetComponent<RectTransform>();
                _destinationContent.SetParent(viewport, false);
                _destinationContent.anchorMin = new Vector2(0f, 1f);
                _destinationContent.anchorMax = new Vector2(1f, 1f);
                _destinationContent.pivot = new Vector2(0.5f, 1f);
                _destinationContent.anchoredPosition = Vector2.zero;
                _destinationContent.sizeDelta = Vector2.zero;
                VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(4, 4, 4, 4);
                layout.spacing = ExpeditionSetupLayoutPolicy.RowSpacing;
                // childControlHeight was false, which left each row's actual RectTransform height at
                // Unity's default 100px (the LayoutElement below only affects layout POSITIONING under
                // that setting, not the row's own rect size) regardless of the intended ~30px row metric.
                // That default-size rect is what rendered as the oversized destination buttons. true here
                // matches the already-working Sim Actions convention (see SimActionMenu.cs) and makes the
                // LayoutElement authoritative for the actual rendered/clickable row size.
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewport;
                scroll.content = _destinationContent;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 22f;

                RectTransform right = MakeRect("RoutePane", inner, 250f, 256f, 202f, 64f);
                right.gameObject.AddComponent<Image>().color = SectionFill;
                RectTransform destLabel = MakeRect("SelectedDestinationLabel", right, 230f, 22f, 10f, 230f);
                AddText(destLabel, "Destination", 10, TextAlignmentOptions.MidlineLeft, HintCyan, false);
                RectTransform destValue = MakeRect("SelectedDestination", right, 230f, 28f, 10f, 202f);
                _destinationText = AddText(destValue, "Select a destination", 14, TextAlignmentOptions.MidlineLeft, Color.white, false);
                RectTransform routeLabel = MakeRect("RouteLabel", right, 230f, 22f, 10f, 178f);
                AddText(routeLabel, "ROUTE", 10, TextAlignmentOptions.MidlineLeft, HintCyan, false);
                RectTransform routeValue = MakeRect("RoutePreview", right, 230f, 124f, 10f, 52f);
                _routeText = AddText(routeValue, string.Empty, 12, TextAlignmentOptions.TopLeft, Color.white, true);
                RectTransform estimate = MakeRect("Estimate", right, 230f, 22f, 10f, 28f);
                _estimateText = AddText(estimate, string.Empty, 11, TextAlignmentOptions.MidlineLeft, HintCyan, false);
                RectTransform message = MakeRect("Message", right, 230f, 24f, 10f, 2f);
                _messageText = AddText(message, string.Empty, 9, TextAlignmentOptions.TopLeft, HintCyan, true);

                RectTransform startRect = MakeRect("Start Expedition", inner, 160f, 34f, Width - 268f, 24f);
                _startButton = AddButton(startRect, "Start Expedition", StartSelected, false);
                RectTransform cancelRect = MakeRect("Cancel", inner, 86f, 34f, Width - 98f, 24f);
                AddButton(cancelRect, "Cancel", delegate { Close("cancel button"); }, false);

                _panelObject.SetActive(false);
                return true;
            }
            catch (Exception ex)
            {
                Debug("setup UI build failed: " + ex.GetType().Name);
                DisposeForLifecycle();
                return false;
            }
        }

        private static void RebuildDestinationList()
        {
            if (_destinationContent == null) return;
            for (int i = _destinationContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_destinationContent.GetChild(i).gameObject);

            if (Choices.Count == 0)
            {
                AddListSection("NO REACHABLE ROUTES");
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_destinationContent);
                return;
            }

            bool wroteNearby = false;
            bool wroteOther = false;
            for (int i = 0; i < Choices.Count; i++)
            {
                ExpeditionRouteChoice choice = Choices[i];
                if (choice.Nearby && !wroteNearby) { AddListSection("NEARBY"); wroteNearby = true; }
                if (!choice.Nearby && !wroteOther) { AddListSection("OTHER REACHABLE ZONES"); wroteOther = true; }
                string destination = choice.DestinationName;
                bool selected = destination.Equals(_selectedDestination, StringComparison.OrdinalIgnoreCase);
                RectTransform row = MakeListRow("Destination", ExpeditionSetupLayoutPolicy.DestinationRowHeight);
                AddDestinationButton(row, destination, selected, delegate { SelectDestination(destination); });
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_destinationContent);
        }

        private static void AddListSection(string text)
        {
            RectTransform row = MakeListRow("Section", ExpeditionSetupLayoutPolicy.SectionRowHeight);
            AddText(row, text, 9, TextAlignmentOptions.MidlineLeft, HintCyan, false);
        }

        private static RectTransform MakeListRow(string name, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(_destinationContent, false);
            // Explicit top-anchored, fixed-height rect: belt-and-suspenders with childControlHeight=true
            // above so a row is never left at Unity's default RectTransform size under any layout-group
            // configuration.
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            LayoutElement e = go.GetComponent<LayoutElement>();
            e.preferredHeight = height;
            e.minHeight = height;
            // No row may absorb leftover viewport space into a tall blank button (SimActionMenu applies
            // the same guard for its action rows).
            e.flexibleHeight = 0f;
            return rt;
        }

        // A selected destination gets a distinct fill/text treatment, not just the bullet glyph, so the
        // current pick reads clearly at a glance rather than requiring the player to read every label.
        private static void AddDestinationButton(RectTransform row, string destination, bool selected,
            UnityEngine.Events.UnityAction action)
        {
            Button button = AddButton(row, (selected ? "• " : string.Empty) + destination, action, false);
            if (!selected) return;
            ColorBlock colors = button.colors;
            colors.normalColor = SelectedFill;
            colors.highlightedColor = SelectedFill;
            colors.selectedColor = SelectedFill;
            button.colors = colors;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.color = TitleCyan;
        }

        private static void RefreshStaticText()
        {
            if (_leaderText != null) _leaderText.text = string.IsNullOrWhiteSpace(_leaderName) ? "Local party Sim" : _leaderName;
            RefreshPreview();
            RefreshLeaderAdmission();
        }

        private static void RefreshPreview()
        {
            if (_destinationText != null) _destinationText.text = string.IsNullOrWhiteSpace(_selectedDestination)
                ? "Select a destination" : _selectedDestination;
            if (_routeText != null)
            {
                _routeText.fontSize = _selectedRoute.Count >= 9 ? 9f : (_selectedRoute.Count >= 7 ? 10f : 12f);
                if (_selectedRoute.Count == 0) _routeText.text = "No route selected.";
                else
                {
                    System.Text.StringBuilder route = new System.Text.StringBuilder();
                    for (int i = 0; i < _selectedRoute.Count; i++)
                    {
                        if (i > 0) route.Append("\n→ ");
                        route.Append(_selectedRoute[i]);
                    }
                    _routeText.text = route.ToString();
                }
            }
            if (_estimateText != null)
            {
                int transitions = Math.Max(0, _selectedRoute.Count - 1);
                _estimateText.text = transitions == 0 ? string.Empty :
                    "Estimated: " + transitions + " zone transition" + (transitions == 1 ? string.Empty : "s");
            }
            if (_startButton != null)
                _startButton.interactable = _selectedRoute.Count >= 2 && LeaderMatches(_leaderTracking, SimTrackingRebind.CurrentAvatar(_leaderTracking));
        }

        private static void SetMessage(string value, bool warning)
        {
            if (_messageText == null) return;
            _messageText.text = value ?? string.Empty;
            _messageText.color = warning ? (Color)new Color32(255, 211, 132, 255) : HintCyan;
        }

        private static void PlaceCentered()
        {
            if (_panel == null) return;
            _panel.anchoredPosition = new Vector2(Mathf.Max(Edge, (Screen.width - Width) * 0.5f),
                Mathf.Max(Edge, (Screen.height - Height) * 0.5f));
            ClampPanelToScreen();
        }

        private static void ClampPanelToScreen()
        {
            if (_panel == null) return;
            Vector2 p = _panel.anchoredPosition;
            p.x = Mathf.Clamp(p.x, Edge, Mathf.Max(Edge, Screen.width - Width - Edge));
            p.y = Mathf.Clamp(p.y, Edge, Mathf.Max(Edge, Screen.height - Height - Edge));
            _panel.anchoredPosition = p;
        }

        private static void TouchActivation() { _lastActivatedAt = Time.unscaledTime; }

        private static void Close(string reason)
        {
            if (!_open && _leaderTracking == null) return;
            _open = false;
            _leaderTracking = null;
            _leaderName = null;
            _originZone = null;
            _selectedDestination = null;
            _selectedRoute.Clear();
            Choices.Clear();
            _nextRouteRefresh = 0f;
            _rejectionMessage = null;
            if (_panelObject != null) _panelObject.SetActive(false);
            FollowUiDragGuard.ForceReleaseIfOwned();
            Debug("setup closed; reason=" + reason);
        }

        private static string ActiveScene()
        {
            try { return SceneManager.GetActiveScene().name; }
            catch { return null; }
        }

        private static bool SameZone(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
                a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void Chat(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
        }

        private static void Debug(string message)
        {
            if (!ErenshorFollowPlugin.VerboseDiagnostics) return;
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogDebug("ExpeditionSetupWindow: " + message); } catch { }
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
            button.onClick.AddListener(delegate { TouchActivation(); action(); });
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonFill;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.selectedColor = ButtonHover;
            colors.disabledColor = new Color32(8, 31, 40, 145);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            image.color = Color.white;
            AddText(rt, label, 12, TextAlignmentOptions.Center, Color.white, false);
            return button;
        }

        private static TextMeshProUGUI AddText(RectTransform parent, string text, int size,
            TextAlignmentOptions alignment, Color color, bool wrap)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 1f);
            rt.offsetMax = new Vector2(-4f, -1f);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
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
