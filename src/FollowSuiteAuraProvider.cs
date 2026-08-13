using System;
using Lunaris;
using Lunaris.IPC;

namespace ErenshorFollow
{
    // Optional Aura transport over the public FollowControlApi. No Hub assembly reference and no
    // gameplay logic: actions/settings still revalidate in the authoritative Follow mod.
    internal sealed class FollowSuiteAuraProvider
    {
        private const string Prefix = "forgetwhtuno.erenshor.suite.follow.v1.";
        private const int MaxFieldLength = 200;

        private readonly IAuraProvider<string> _describe;
        private readonly IAuraProvider<string> _developerSettings;
        private readonly IAuraProvider<string, string, string> _setSetting;
        private readonly IAuraProvider<string, string, string> _action;

        internal FollowSuiteAuraProvider(LunarisPlugin owner)
        {
            _describe = owner.IPCAuraProvider<string>(Prefix + "describe");
            _developerSettings = owner.IPCAuraProvider<string>(Prefix + "settings.developer");
            _setSetting = owner.IPCAuraProvider<string, string, string>(Prefix + "setting.set");
            _action = owner.IPCAuraProvider<string, string, string>(Prefix + "action");
        }

        internal void Register()
        {
            try
            {
                _describe.RegisterFunc(Describe);
                _developerSettings.RegisterFunc(DeveloperSettings);
                _setSetting.RegisterFunc(SetSetting);
                _action.RegisterFunc(InvokeAction);
            }
            catch
            {
                Unregister();
                throw;
            }
        }

        internal void Unregister()
        {
            // Always attempt every endpoint so partial registration cannot leak across hot reload.
            try { if (_setSetting != null) _setSetting.UnregisterFunc(); } catch { }
            try { if (_action != null) _action.UnregisterFunc(); } catch { }
            try { if (_developerSettings != null) _developerSettings.UnregisterFunc(); } catch { }
            try { if (_describe != null) _describe.UnregisterFunc(); } catch { }
        }

        private static string Describe()
        {
            return FollowSuiteDescriptorPolicy.BuildDescribe(ErenshorFollowPlugin.PluginVersion, FollowControlApi.GetStatus());
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
