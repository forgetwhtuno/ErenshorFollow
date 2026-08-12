using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ErenshorFollow
{
    internal static class SimActionMenu
    {
        private static SimPlayer _selected;
        private static string _selectedName;
        private static List<string> _selectedZones = new List<string>();
        private static Vector2 _zoneScroll;
        private static bool _canReturn;
        private static string _returnZone;
        private static Rect _window;
        private static bool _open;
        private static bool _nativeLeftClickActive;
        private static SimPlayer _nativeLeftClickTarget;
        private static bool _suppressLeftClickUntilRelease;
        private static GUIStyle _windowStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _nameStyle;
        private static GUIStyle _hintStyle;
        private static GUIStyle _buttonStyle;
        private static GUIStyle _stopButtonStyle;
        private static GUIStyle _closeButtonStyle;
        private static Texture2D _panelTexture;
        private static Texture2D _buttonTexture;
        private static Texture2D _buttonHoverTexture;
        private static Texture2D _stopTexture;
        private static Texture2D _closeTexture;
        private const int WindowId = 764291;
        private const int MaxVisibleZoneRows = 5;
        private const float ZoneRowHeight = 31f;

        internal static void Tick()
        {
            if (_suppressLeftClickUntilRelease && !Input.GetMouseButton(0))
                _suppressLeftClickUntilRelease = false;

            if (_open && Input.GetKeyDown(KeyCode.Escape))
            {
                Close("escape", false);
                return;
            }
            if (Input.GetMouseButtonDown(2) && !PointerIsOverUi())
            {
                Debug("click seen: middle button");
                SimPlayer hit;
                TryOpen(TryPickSim(out hit) ? hit : null, "middle click");
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                Debug("click seen: F8 target fallback");
                SimPlayer targeted;
                TryOpen(TryGetCurrentTargetSim(out targeted) ? targeted : null, "F8");
            }
        }

        internal static void Draw()
        {
            if (!_open) return;
            EnsureStyles();
            Event current = Event.current;
            if (current != null && current.type == EventType.MouseDown && current.button == 0 && !_window.Contains(current.mousePosition))
            {
                Debug("click seen: IMGUI outside window at " + current.mousePosition);
                Close("outside click", true);
                current.Use();
                return;
            }
            if (!FollowController.IsUsableSim(_selected) || CoopCompatibility.IsRemoteHuman(_selected) ||
                !LeaderController.IsPlayerPartySim(_selected))
            {
                Close("selected Sim became invalid or left party", false);
                return;
            }
            _window = GUI.Window(WindowId, _window, DrawWindow, GUIContent.none, _windowStyle);
        }

        private static void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            GUILayout.BeginVertical();
            try
            {
                GUILayout.Label("SIM ACTIONS", _titleStyle);
                GUILayout.Label(_selectedName, _nameStyle);
                GUILayout.Label("choose a command", _hintStyle);
                GUILayout.Space(5f);
                if (GUILayout.Button("Follow " + _selectedName, _buttonStyle, GUILayout.Height(29f)))
                {
                    LeaderController.Stop(null);
                    FollowController.Start(_selected, _selectedName);
                    Say("[Erenshor Follow] Following " + _selectedName + ". Press WASD, Space, or click to stop.", "lightblue");
                    Close("Follow action", true);
                    return;
                }
                if (GUILayout.Button("Challenge to friendly duel", _buttonStyle, GUILayout.Height(29f)))
                {
                    if (!TryStartDuel()) Say("[Practice Duel] Install Erenshor Practice Duels to use this action.", "yellow");
                    Close("Duel action", true);
                    return;
                }

                ExpeditionStatusSnapshot expedition = ExpeditionCoordinator.GetStatusSnapshot();
                if (expedition.Active)
                {
                    if (DrawExpeditionControls(expedition)) return;
                }
                else
                {
                    List<string> zones = _selectedZones;
                    if (zones.Count > 0)
                    {
                        GUILayout.Space(3f);
                        GUILayout.Label("START EXPEDITION", _hintStyle);
                        bool scroll = zones.Count > MaxVisibleZoneRows;
                        if (scroll)
                            _zoneScroll = GUILayout.BeginScrollView(_zoneScroll, false, true, GUILayout.Height(MaxVisibleZoneRows * ZoneRowHeight));
                        try
                        {
                            for (int i = 0; i < zones.Count; i++)
                            {
                                string zone = zones[i];
                                if (GUILayout.Button(zone, _buttonStyle, GUILayout.Height(27f)))
                                {
                                    LeaderController.StartSmart(_selected, zone);
                                    Close("Expedition action", true);
                                    return;
                                }
                            }
                        }
                        finally
                        {
                            if (scroll) GUILayout.EndScrollView();
                        }
                    }
                    if (_canReturn && GUILayout.Button("Return to " + _returnZone, _buttonStyle, GUILayout.Height(27f)))
                    {
                        ExpeditionCoordinator.TryReturn();
                        Close("Return action", true);
                        return;
                    }
                }
                GUILayout.Space(4f);
                if (GUILayout.Button("Stop follow / lead", _stopButtonStyle, GUILayout.Height(27f)))
                {
                    ErenshorFollowPlugin.StopAllTravel("[Erenshor Travel] Travel stopped.", "yellow");
                    Close("Stop action", true);
                    return;
                }
                if (GUILayout.Button("Cancel", _closeButtonStyle, GUILayout.Height(26f)))
                {
                    Close("Cancel button", true);
                    return;
                }
            }
            finally
            {
                GUILayout.EndVertical();
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
        }

        private static bool DrawExpeditionControls(ExpeditionStatusSnapshot expedition)
        {
            GUILayout.Space(3f);
            GUILayout.Label("EXPEDITION", _hintStyle);
            GUILayout.Label(SafeText(expedition.LeaderName) + " -> " + SafeText(expedition.DestinationName), _nameStyle);
            GUILayout.Label(ExpeditionCoordinator.DescribeState(expedition.State).ToLowerInvariant(), _hintStyle);

            if (expedition.State == ExpeditionState.Paused)
            {
                if (GUILayout.Button("Resume expedition", _buttonStyle, GUILayout.Height(27f)))
                {
                    ExpeditionCoordinator.Resume();
                    Close("Expedition resume", true);
                    return true;
                }
            }
            else if (GUILayout.Button("Pause expedition", _buttonStyle, GUILayout.Height(27f)))
            {
                ExpeditionCoordinator.Pause(ExpeditionPauseReason.PlayerRequest);
                Close("Expedition pause", true);
                return true;
            }
            if (_canReturn && GUILayout.Button("Return to " + _returnZone, _buttonStyle, GUILayout.Height(27f)))
            {
                ExpeditionCoordinator.TryReturn();
                Close("Expedition return", true);
                return true;
            }
            if (GUILayout.Button("Cancel expedition", _stopButtonStyle, GUILayout.Height(27f)))
            {
                ExpeditionCoordinator.Cancel("you called it off.");
                Close("Expedition cancel", true);
                return true;
            }
            return false;
        }

        private static string SafeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "?" : value;
        }

        private static void EnsureStyles()
        {
            if (_windowStyle != null) return;
            Color cyanEdge = new Color(0.03f, 0.67f, 0.86f, 0.95f);
            Color softEdge = new Color(0.13f, 0.55f, 0.68f, 0.90f);
            _panelTexture = FramedTexture(new Color(0.015f, 0.09f, 0.125f, 0.72f), cyanEdge);
            _buttonTexture = FramedTexture(new Color(0.035f, 0.17f, 0.22f, 0.78f), softEdge);
            _buttonHoverTexture = FramedTexture(new Color(0.12f, 0.38f, 0.48f, 0.90f), cyanEdge);
            _stopTexture = FramedTexture(new Color(0.19f, 0.15f, 0.09f, 0.82f), new Color(0.65f, 0.49f, 0.27f, 0.92f));
            _closeTexture = FramedTexture(new Color(0.025f, 0.13f, 0.17f, 0.88f), cyanEdge);

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _panelTexture;
            _windowStyle.border = new RectOffset(1, 1, 1, 1);
            _windowStyle.padding = new RectOffset(12, 12, 9, 10);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.normal.textColor = new Color(0.56f, 0.88f, 1f, 1f);
            _nameStyle = new GUIStyle(GUI.skin.label);
            _nameStyle.normal.textColor = Color.white;
            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.normal.textColor = new Color(0.56f, 0.78f, 0.88f, 1f);

            _buttonStyle = CreateButtonStyle(_buttonTexture, _buttonHoverTexture, Color.white);
            _stopButtonStyle = CreateButtonStyle(_stopTexture, _buttonHoverTexture, new Color(1f, 0.94f, 0.74f, 1f));
            _closeButtonStyle = CreateButtonStyle(_closeTexture, _buttonHoverTexture, new Color(0.84f, 0.94f, 1f, 1f));
        }

        private static GUIStyle CreateButtonStyle(Texture2D normal, Texture2D hover, Color text)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = hover;
            style.normal.textColor = text;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.margin = new RectOffset(2, 2, 2, 2);
            style.border = new RectOffset(1, 1, 1, 1);
            return style;
        }

        private static Texture2D FramedTexture(Color center, Color edge)
        {
            Texture2D texture = new Texture2D(3, 3, TextureFormat.RGBA32, false);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    texture.SetPixel(x, y, x == 0 || x == 2 || y == 0 || y == 2 ? edge : center);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Apply(false, true);
            return texture;
        }

        private static bool TryPickSim(out SimPlayer sim)
        {
            sim = null;
            Camera camera = Camera.main;
            if (camera == null) return false;
            RaycastHit hit;
            if (!Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out hit, 500f) || hit.collider == null) return false;
            SimPlayer candidate = hit.collider.GetComponentInParent<SimPlayer>();
            if (!FollowController.IsUsableSim(candidate) || CoopCompatibility.IsRemoteHuman(candidate)) return false;
            sim = candidate;
            return true;
        }

        private static bool TryGetCurrentTargetSim(out SimPlayer sim)
        {
            sim = null;
            Character target = null;
            try { target = GameData.PlayerControl == null ? null : GameData.PlayerControl.CurrentTarget; } catch { }
            if (target == null) return false;
            foreach (SimPlayer candidate in UnityEngine.Object.FindObjectsOfType<SimPlayer>())
            {
                if (candidate == null || candidate.MyStats == null || candidate.MyStats.Myself != target) continue;
                if (!FollowController.IsUsableSim(candidate) || CoopCompatibility.IsRemoteHuman(candidate)) continue;
                sim = candidate;
                return true;
            }
            return false;
        }

        private static void TryOpen(SimPlayer candidate, string source)
        {
            if (candidate == null)
            {
                Debug("selected Sim resolved: none (" + source + ")");
                return;
            }
            string name = FollowController.ReadName(candidate);
            Debug("selected Sim resolved: " + name + " (" + source + ")");
            if (!FollowController.IsUsableSim(candidate) || CoopCompatibility.IsRemoteHuman(candidate) ||
                !LeaderController.IsPlayerPartySim(candidate))
            {
                Debug("menu not opened: selected Sim is remote, unusable, or not in the player's party");
                return;
            }
            _selected = candidate;
            _selectedName = name;
            bool expeditionActive = ExpeditionCoordinator.IsActive;
            _selectedZones = expeditionActive ? new List<string>() : LeaderController.GetMenuDestinations();
            _zoneScroll = Vector2.zero;
            _canReturn = ExpeditionCoordinator.CanReturn();
            _returnZone = _canReturn ? ExpeditionCoordinator.ReturnZoneName() : null;
            float width = 242f;
            int visibleZoneRows = Math.Min(_selectedZones.Count, MaxVisibleZoneRows);
            float height = 272f + visibleZoneRows * ZoneRowHeight + (_canReturn ? 31f : 0f);
            if (expeditionActive) height += 100f;
            height = Mathf.Min(height, Mathf.Max(220f, Screen.height - 16f));
            float x = Mathf.Clamp(Input.mousePosition.x, 8f, Mathf.Max(8f, Screen.width - width - 8f));
            float y = Mathf.Clamp(Screen.height - Input.mousePosition.y, 8f, Mathf.Max(8f, Screen.height - height - 8f));
            _window = new Rect(x, y, width, height);
            _open = true;
            Debug("menu opened for " + _selectedName + " via " + source + "; verified exits=" + _selectedZones.Count);
        }

        internal static bool BeginNativeLeftClick()
        {
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            if (_suppressLeftClickUntilRelease)
            {
                Debug("click seen: suppressed close/action click");
                return false;
            }
            if (_open)
            {
                Vector3 mouse = Input.mousePosition;
                Vector2 guiMouse = new Vector2(mouse.x, Screen.height - mouse.y);
                if (_window.Contains(guiMouse))
                {
                    Debug("click seen: consumed inside action menu");
                    return false;
                }
                // Close the menu but let Erenshor receive the outside world click. This preserves normal
                // targeting/movement semantics instead of turning dismissal into a swallowed click.
                Debug("click seen: outside action menu; closing and passing through");
                Close("outside native click", false);
                return true;
            }
            if (PointerIsOverUi())
            {
                Debug("click seen: native UI click ignored by action menu");
                return true;
            }
            _nativeLeftClickActive = true;
            Debug("click seen: native PlayerControl.LeftClick");
            return true;
        }

        internal static void ObserveNativeTarget(Character character)
        {
            if (!_nativeLeftClickActive || character == null) return;
            SimPlayer sim = null;
            try { sim = character.GetComponent<SimPlayer>(); } catch { }
            _nativeLeftClickTarget = sim;
            Debug(sim == null ? "selected Sim resolved: native target is not a Sim" : "selected Sim resolved from Character.TargetMe: " + FollowController.ReadName(sim));
        }

        internal static void CompleteNativeLeftClick()
        {
            if (!_nativeLeftClickActive) return;
            SimPlayer selected = _nativeLeftClickTarget;
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            TryOpen(selected, "native left click");
        }

        private static bool PointerIsOverUi()
        {
            if (TravelStatusOverlay.PointerIsOverOverlay()) return true;
            try { return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }
            catch { return false; }
        }

        private static bool TryStartDuel()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type controller = assembly.GetType("ErenshorDuel.DuelController", false);
                    if (controller == null) continue;
                    MethodInfo start = controller.GetMethod("Start", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (start == null) return false;
                    start.Invoke(null, new object[] { _selected });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Say("[Practice Duel] Could not start duel: " + ex.Message, "yellow");
            }
            return false;
        }

        private static void Close(string reason, bool suppressUntilRelease)
        {
            string previous = _selectedName;
            _open = false;
            _selected = null;
            _selectedName = null;
            _selectedZones = new List<string>();
            _zoneScroll = Vector2.zero;
            _canReturn = false;
            _returnZone = null;
            _window = new Rect();
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            _suppressLeftClickUntilRelease = suppressUntilRelease && Input.GetMouseButton(0);
            Debug("menu closed" + (string.IsNullOrWhiteSpace(previous) ? string.Empty : " for " + previous) + "; reason=" + reason);
        }

        // High-frequency click/open/close diagnostics are noisy in normal play; gate them behind the
        // Diagnostics.Verbose config toggle instead of always writing to the BepInEx log.
        private static void Debug(string message)
        {
            if (!ErenshorFollowPlugin.VerboseDiagnostics) return;
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogDebug("SimActionMenu: " + message); } catch { }
        }

        private static void Say(string message, string color)
        {
            try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.Chat(message, color); } catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "LeftClick")]
    internal static class SimActionMenuLeftClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try { return SimActionMenu.BeginNativeLeftClick(); }
            catch { return true; }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try { SimActionMenu.CompleteNativeLeftClick(); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Character), "TargetMe")]
    internal static class SimActionMenuTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            try { SimActionMenu.ObserveNativeTarget(__instance); }
            catch { }
        }
    }
}
