using System;
using Lunaris;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    // Deep Sims compatibility is reflection/owner-ID based (see DisableEmbeddedDeepSimsFollow
    // and CoopCompatibility/ExpeditionIntegrationBridge) rather than a declared loader
    // dependency, so it continues to work whether or not Deep Sims is present or has loaded yet.
    [LunarisPlugin(PluginGuid, PluginVersion, "forgetwhtuno",
        "Player movement assistance, Sim-led travel, and expedition coordination around existing Erenshor zone transitions.")]
    [LunarisPermission(LunarisPermission.Reflection | LunarisPermission.Harmony)]
    public sealed class ErenshorFollowPlugin : LunarisPlugin
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.follow";
        internal const string PluginName = "Erenshor Follow";
        internal const string PluginVersion = "0.5.0";
        internal static ErenshorFollowPlugin Instance;
        internal static bool VerboseDiagnostics { get; private set; }

        private Harmony _harmony;
        internal FollowSettings Settings;

        private void Awake()
        {
            Instance = this;
            Settings = new FollowSettings();
            Config.Register(ref Settings);

            TravelStatusOverlay.OffsetX = Settings.OverlayOffsetX;
            TravelStatusOverlay.OffsetY = Settings.OverlayOffsetY;
            VerboseDiagnostics = Settings.DiagnosticsVerbose;

            CoopCompatibility.Initialize();
            ExpeditionIntegrationBridge.Initialize();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            DisableEmbeddedDeepSimsFollow();
            SceneManager.sceneLoaded += OnSceneLoaded;
            Logging.LogInfo("Erenshor Follow loaded. Use /efollow <SimName> or /efollow off. /dsfollow is also accepted for compatibility.");
            Logging.LogInfo("Sim-Led Expeditions available: /expedition status|pause|resume|cancel|return.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try { ExpeditionCoordinator.HandleSceneLoaded(scene); }
            catch (Exception ex) { Logging.LogError("Expedition scene handling failed: " + ex); }
        }

        private void Update()
        {
            try { FollowController.Tick(); }
            catch (Exception ex)
            {
                Logging.LogError("Follow update failed: " + ex);
                FollowController.Stop();
            }
            try { LeaderController.Tick(); }
            catch (Exception ex)
            {
                Logging.LogError("Leader travel update failed: " + ex);
                LeaderController.Stop("Leader travel stopped after an internal error.");
            }
            // Runs after the leg so this frame's leg outcomes are consumed by the coordinator immediately.
            try { ExpeditionCoordinator.Tick(); }
            catch (Exception ex)
            {
                Logging.LogError("Expedition update failed: " + ex);
                ExpeditionCoordinator.Cancel("an internal error stopped the expedition.");
            }
            try { SimActionMenu.Tick(); }
            catch (Exception ex) { Logging.LogError("Sim action menu update failed: " + ex); }
        }

        private void OnGUI()
        {
            try
            {
                TravelStatusOverlay.Draw();
                SimActionMenu.Draw();
            }
            catch (Exception ex) { Logging.LogError("Follow UI draw failed: " + ex); }
        }

        private void OnDestroy()
        {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            ExpeditionCoordinator.Shutdown();
            LeaderController.Stop(null);
            FollowController.Stop();
            if (_harmony != null) _harmony.UnpatchSelf();
            CoopCompatibility.Reset();
            ExpeditionIntegrationBridge.Reset();
            if (Instance == this) Instance = null;
        }

        private void DisableEmbeddedDeepSimsFollow()
        {
            try
            {
                System.Reflection.MethodInfo movement = AccessTools.Method(typeof(PlayerControl), "LandMovement");
                if (movement != null)
                {
                    _harmony.Unpatch(movement, HarmonyPatchType.Prefix, "forgetwhtuno.erenshor.deepsims");
                    Logging.LogInfo("Standalone Follow owns player-follow movement; disabled the embedded Deep Sims movement prefix.");
                }
            }
            catch (Exception ex)
            {
                Logging.LogWarning("Could not disable the embedded Deep Sims follow prefix: " + ex.Message);
            }
        }

        internal void LogError(string message)
        {
            Logging.LogError(message);
        }

        internal void LogDebug(string message)
        {
            Logging.LogDebug(message);
        }

        // Native Lunaris config does not auto-persist a .Value write to disk the way BepInEx's
        // ConfigEntry did. Called after TravelStatusOverlay's position config changes.
        internal void SavePersistedSettings()
        {
            try { Config.Save(); } catch { }
        }

        internal void Chat(string message, string color = "lightblue")
        {
            try { UpdateSocialLog.LogAdd(message, color); }
            catch { try { UpdateSocialLog.LogAdd(message); } catch { } }
        }

        internal bool TryHandle(TypeText typeText, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string command = raw.Trim();
            string spokenMessage;
            string naturalLeader;
            string naturalDestination;
            if (TryParseNaturalLead(command, out spokenMessage, out naturalLeader, out naturalDestination))
            {
                bool naturalAmbiguous;
                SimPlayer naturalSim = FindSim(naturalLeader, out naturalAmbiguous);
                if (naturalSim == null) return false;
                ClearInput(typeText);
                Chat("You tell the group: " + spokenMessage, "lightblue");
                LeaderController.StartSmart(naturalSim, naturalDestination);
                return true;
            }
            string controlPhrase;
            if (TryParseNaturalExpeditionControl(command, out spokenMessage, out controlPhrase))
            {
                ClearInput(typeText);
                Chat("You tell the group: " + spokenMessage, "lightblue");
                HandleExpeditionControlPhrase(controlPhrase);
                return true;
            }
            if (IsCommand(command, "/expedition"))
            {
                string expeditionArgument = command.Length == 11 ? string.Empty : command.Substring(11).Trim();
                ClearInput(typeText);
                HandleExpeditionCommand(expeditionArgument);
                return true;
            }
            if (IsCommand(command, "/elead"))
            {
                string leadArgument = command.Length == 6 ? string.Empty : command.Substring(6).Trim();
                ClearInput(typeText);
                HandleLeadCommand(leadArgument);
                return true;
            }
            int prefixLength;
            if (command.StartsWith("/efollow", StringComparison.OrdinalIgnoreCase) &&
                (command.Length == 8 || char.IsWhiteSpace(command[8]))) prefixLength = 8;
            else if (command.StartsWith("/dsfollow", StringComparison.OrdinalIgnoreCase) &&
                (command.Length == 9 || char.IsWhiteSpace(command[9]))) prefixLength = 9;
            else return false;

            string argument = command.Length == prefixLength ? string.Empty : command.Substring(prefixLength).Trim();
            ClearInput(typeText);
            if (argument.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                StopAllTravel("[Erenshor Follow] Following stopped.", "yellow");
                return true;
            }
            if (argument.Length == 0)
            {
                Chat("[Erenshor Follow] Usage: /efollow <SimName> or /efollow off", "yellow");
                return true;
            }

            bool ambiguous;
            SimPlayer target = FindSim(argument, out ambiguous);
            if (target == null)
            {
                Chat(ambiguous
                    ? "[Erenshor Follow] More than one Sim matches that name. Type a longer name."
                    : "[Erenshor Follow] Could not find that Sim nearby.", "yellow");
                return true;
            }
            LeaderController.Stop(null);
            FollowController.Start(target, FollowController.ReadName(target));
            Chat("[Erenshor Follow] Following " + FollowController.TargetName + ". Press WASD, Space, or click to stop.", "lightblue");
            return true;
        }

        private void HandleLeadCommand(string argument)
        {
            string value = argument == null ? string.Empty : argument.Trim();
            if (value.Length == 0 || value.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Chat(LeaderController.Status(), "lightblue");
                return;
            }
            if (value.Equals("zones", StringComparison.OrdinalIgnoreCase))
            {
                Chat(LeaderController.DescribeDestinations(), "lightblue");
                return;
            }
            if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                StopAllTravel("[Erenshor Lead] Leader travel stopped.", "lightblue");
                return;
            }
            if (value.Equals("resume", StringComparison.OrdinalIgnoreCase))
            {
                LeaderController.Resume();
                return;
            }

            int split = value.IndexOfAny(new char[] { ' ', '\t' });
            if (split <= 0 || split >= value.Length - 1)
            {
                Chat("[Erenshor Lead] Usage: /elead <SimName> <adjacent zone>, /elead zones, /elead resume, or /elead off", "yellow");
                return;
            }
            string simName = value.Substring(0, split).Trim();
            string destination = value.Substring(split + 1).Trim();
            bool ambiguous;
            SimPlayer sim = FindSim(simName, out ambiguous);
            if (sim == null)
            {
                Chat(ambiguous ? "[Erenshor Lead] More than one Sim matches that name." : "[Erenshor Lead] Could not find that living local Sim.", "yellow");
                return;
            }
            LeaderController.StartSmart(sim, destination);
        }

        private void HandleExpeditionCommand(string argument)
        {
            string value = argument == null ? string.Empty : argument.Trim();
            if (value.Length == 0 || value.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Chat(ExpeditionCoordinator.Status(), "lightblue");
                if (!ExpeditionCoordinator.IsActive)
                {
                    System.Collections.Generic.List<string> zones = ExpeditionDestinationResolver.ListCanonicalNames();
                    Chat(zones.Count == 0
                        ? "[Erenshor Expedition] No verified adjacent zone exits are available here."
                        : "[Erenshor Expedition] Verified destinations: " + string.Join(", ", zones.ToArray()) +
                          ". Start one with /elead <SimName> <zone> or the Sim action menu.", "lightblue");
                }
                return;
            }
            if (value.Equals("pause", StringComparison.OrdinalIgnoreCase) || value.Equals("hold", StringComparison.OrdinalIgnoreCase))
            {
                ExpeditionCoordinator.Pause(ExpeditionPauseReason.PlayerRequest);
                return;
            }
            if (value.Equals("resume", StringComparison.OrdinalIgnoreCase))
            {
                ExpeditionCoordinator.Resume();
                return;
            }
            if (value.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                if (!ExpeditionCoordinator.IsActive)
                {
                    Chat("[Erenshor Expedition] No expedition is active.", "yellow");
                    return;
                }
                ExpeditionCoordinator.Cancel("you called it off.");
                return;
            }
            if (value.Equals("return", StringComparison.OrdinalIgnoreCase) || value.Equals("back", StringComparison.OrdinalIgnoreCase))
            {
                ExpeditionCoordinator.TryReturn();
                return;
            }
            Chat("[Erenshor Expedition] Usage: /expedition status, pause, resume, cancel, or return.", "yellow");
        }

        private void HandleExpeditionControlPhrase(string phrase)
        {
            switch (phrase)
            {
                case "hold here":
                    ExpeditionCoordinator.Pause(ExpeditionPauseReason.PlayerRequest);
                    break;
                case "keep going":
                    ExpeditionCoordinator.Resume();
                    break;
                case "let's head back":
                    ExpeditionCoordinator.TryReturn();
                    break;
                case "cancel the expedition":
                    if (!ExpeditionCoordinator.IsActive) Chat("[Erenshor Expedition] No expedition is active.", "yellow");
                    else ExpeditionCoordinator.Cancel("you called it off.");
                    break;
            }
        }

        // Deliberately exact-match only. These phrases resolve straight to coordinator operations, so a
        // loose match would let ordinary party chat move the group.
        private static bool TryParseNaturalExpeditionControl(string raw, out string spokenMessage, out string phrase)
        {
            spokenMessage = null;
            phrase = null;
            string message = ExtractPartyMessage(raw);
            if (string.IsNullOrWhiteSpace(message)) return false;
            string normalized = message.Trim().TrimEnd('.', '!', '?').Trim().ToLowerInvariant();
            if (normalized == "lets head back") normalized = "let's head back";
            if (normalized != "hold here" && normalized != "keep going" &&
                normalized != "let's head back" && normalized != "cancel the expedition") return false;
            // "let's head back" may legitimately start a fresh return trip, so it stays available after
            // arrival; the rest only make sense against a live session.
            if (normalized != "let's head back" && !ExpeditionCoordinator.IsActive) return false;
            spokenMessage = message;
            phrase = normalized;
            return true;
        }

        private static string ExtractPartyMessage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string[] prefixes = { "/p", "/party", "/group" };
            foreach (string prefix in prefixes)
            {
                if (raw.Length > prefix.Length && raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(raw[prefix.Length]))
                    return raw.Substring(prefix.Length).Trim();
            }
            return null;
        }

        private static bool TryParseNaturalLead(string raw, out string spokenMessage, out string leader, out string destination)
        {
            spokenMessage = null;
            leader = null;
            destination = null;
            string message = ExtractPartyMessage(raw);
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (!TravelCommandGrammar.TryParseLeadRequest(message, out leader, out destination)) return false;
            spokenMessage = message;
            return true;
        }

        private static bool IsCommand(string text, string prefix)
        {
            return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   (text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length]));
        }

        internal static SimPlayer FindSim(string requested, out bool ambiguous)
        {
            ambiguous = false;
            SimPlayer[] sims = UnityEngine.Object.FindObjectsOfType<SimPlayer>();
            SimPlayer partial = null;
            foreach (SimPlayer sim in sims)
            {
                if (!FollowController.IsUsableSim(sim) || CoopCompatibility.IsRemoteHuman(sim)) continue;
                string name = FollowController.ReadName(sim);
                if (name.Equals(requested, StringComparison.OrdinalIgnoreCase)) return sim;
                if (name.IndexOf(requested, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (partial != null)
                {
                    ambiguous = true;
                    partial = null;
                    continue;
                }
                if (!ambiguous) partial = sim;
            }
            return ambiguous ? null : partial;
        }

        private static void ClearInput(TypeText typeText)
        {
            try { if (typeText != null && typeText.typed != null) typeText.typed.text = string.Empty; } catch { }
        }

        internal static void StopAllTravel(string message, string color)
        {
            // Cancel first so the coordinator emits its own terminal event instead of only learning about
            // the teardown second-hand from LeaderController.
            ExpeditionCoordinator.Cancel("travel was stopped.");
            LeaderController.Stop(null);
            FollowController.Stop();
            try
            {
                if (Instance != null && !string.IsNullOrWhiteSpace(message)) Instance.Chat(message, color);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class TypeTextPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                if (ErenshorFollowPlugin.Instance == null || __instance == null || __instance.typed == null) return true;
                return !ErenshorFollowPlugin.Instance.TryHandle(__instance, __instance.typed.text);
            }
            catch (Exception ex)
            {
                // Instance can be null here for the same reason the try block checks it, so the
                // original log call turned a handled error into an NRE escaping the patch.
                if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogError("Follow command failed: " + ex);
                return true;
            }
        }
    }
}
