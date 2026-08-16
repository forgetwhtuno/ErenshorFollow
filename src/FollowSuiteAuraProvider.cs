using System;
using Lunaris;
using Lunaris.IPC;

namespace ErenshorFollow
{
    // Optional Aura transport over Follow's authoritative control surface. This owns no movement
    // or routing logic and has no compile-time Suite Hub dependency.
    internal sealed class FollowSuiteAuraProvider
    {
        private const string Prefix = "forgetwhtuno.erenshor.suite.follow.v1.";
        private const int MaxFieldLength = 200;
        private readonly IAuraProvider<string> _describe;
        private readonly IAuraProvider<string> _developerSettings;
        private readonly IAuraProvider<string> _uiState;
        private readonly IAuraProvider<string, string, string> _setSetting;
        private readonly IAuraProvider<string, string, string> _action;
        internal bool Registered { get; private set; }

        internal FollowSuiteAuraProvider(LunarisPlugin owner)
        {
            _describe = owner.IPCAuraProvider<string>(Prefix + "describe");
            _developerSettings = owner.IPCAuraProvider<string>(Prefix + "settings.developer");
            _uiState = owner.IPCAuraProvider<string>(Prefix + "ui.state");
            _setSetting = owner.IPCAuraProvider<string, string, string>(Prefix + "setting.set");
            _action = owner.IPCAuraProvider<string, string, string>(Prefix + "action");
        }

        internal void Register()
        {
            try
            {
                _describe.RegisterFunc(Describe);
                _developerSettings.RegisterFunc(DeveloperSettings);
                _uiState.RegisterFunc(UiState);
                _setSetting.RegisterFunc(SetSetting);
                _action.RegisterFunc(InvokeAction);
                Registered = true;
            }
            catch { Unregister(); throw; }
        }

        internal void Unregister()
        {
            Registered = false;
            try { if (_setSetting != null) _setSetting.UnregisterFunc(); } catch { }
            try { if (_action != null) _action.UnregisterFunc(); } catch { }
            try { if (_uiState != null) _uiState.UnregisterFunc(); } catch { }
            try { if (_developerSettings != null) _developerSettings.UnregisterFunc(); } catch { }
            try { if (_describe != null) _describe.UnregisterFunc(); } catch { }
        }

        private static string Describe()
        {
            return FollowSuiteDescriptorPolicy.BuildDescribe(ErenshorFollowPlugin.PluginVersion, FollowControlApi.GetStatus());
        }

        private static string UiState()
        {
            FollowUiSurfaceCandidate top = FollowUiSurfaceRouter.Topmost();
            return SuiteUiStatePolicy.Build("follow", top.Open, top.SortOrder, top.Activated);
        }

        private static string DeveloperSettings()
        {
            return FollowSuiteDescriptorPolicy.BuildDeveloperSettings(FollowControlApi.VerboseDiagnostics);
        }

        private static string SetSetting(string settingId, string value)
        {
            string failure;
            bool ok = FollowControlApi.TrySetSetting(settingId, value, out failure);
            return ok ? "ok" : ("error: " + Bound(failure ?? "rejected", MaxFieldLength));
        }

        private static string InvokeAction(string actionId, string argument)
        {
            switch (actionId)
            {
                case "closePanel": return FollowUiSurfaceRouter.CloseAllVisuals() ? "ok" : "rejected";
                case "stop": return FollowControlApi.TryStop() ? "ok" : "rejected";
                case "pauseExpedition": return FollowControlApi.TryPauseExpedition() ? "ok" : "rejected";
                case "resumeExpedition": return FollowControlApi.TryResumeExpedition() ? "ok" : "rejected";
                case "cancelExpedition": return FollowControlApi.TryCancelExpedition() ? "ok" : "rejected";
                case "return": return FollowControlApi.TryReturn() ? "ok" : "rejected";
                default: return "unknown action";
            }
        }

        private static string Bound(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
            return value.Substring(0, max);
        }
    }
}
