using System;
using System.Text;

namespace ErenshorFollow
{
    // Unity-free Suite Hub wire policy. Follow intentionally exposes only settings with a clear,
    // safe live mutation path; persisted overlay coordinates stay owned by the existing UI.
    internal static class FollowSuiteDescriptorPolicy
    {
        internal const int MaxHubText = 200;

        internal static string BuildDescribe(string version, string status)
        {
            return "protocol=1"
                + "&module=follow"
                + "&display=" + Escape("Erenshor Follow")
                + "&version=" + Escape(Bound(version, 32))
                + "&summary=" + Escape("Local movement-assist and Sim-led travel")
                + "&status=" + Escape(Bound(status, MaxHubText))
                + "&actions=closePanel,stop,pauseExpedition,resumeExpedition,cancelExpedition,return";
        }

        internal static string BuildDeveloperSettings(bool verboseDiagnostics)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("id=verboseDiagnostics")
              .Append("&label=").Append(Escape("Verbose route diagnostics"))
              .Append("&tier=developer&type=bool&value=")
              .Append(verboseDiagnostics ? "true" : "false")
              .Append("&mutable=true");
            return sb.ToString();
        }

        internal static bool TryNormalizeSettingValue(string settingId, string value, out string normalized)
        {
            if (!string.Equals((settingId ?? string.Empty).Trim(), "verboseDiagnostics", StringComparison.OrdinalIgnoreCase))
            {
                normalized = null;
                return false;
            }
            bool parsed;
            if (!TryParseWireBool(value, out parsed))
            {
                normalized = null;
                return false;
            }
            normalized = parsed ? "true" : "false";
            return true;
        }

        internal static bool TryParseWireBool(string value, out bool parsed)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { parsed = true; return true; }
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { parsed = false; return true; }
            parsed = false;
            return false;
        }

        internal static bool ContainsSensitiveFieldName(string payload)
        {
            string lower = Uri.UnescapeDataString(payload ?? string.Empty).ToLowerInvariant();
            string[] forbidden = { "apikey", "api key", "endpoint", "filepath", "filesystem", "memory", "conversation", "prompt", "windows username" };
            for (int i = 0; i < forbidden.Length; i++)
                if (lower.IndexOf(forbidden[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string Bound(string value, int max)
        {
            string safe = value ?? string.Empty;
            return safe.Length <= max ? safe : safe.Substring(0, max);
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }
    }
}
