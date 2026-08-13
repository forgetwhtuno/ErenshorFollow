using System;

namespace ErenshorFollow
{
    internal static class FollowControlDescriptorTests
    {
        private static int Main()
        {
            int failed = 0;
            failed += Check("verbose true descriptor exact bool wire",
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(true), "value") == "true" &&
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(true), "type") == "bool");
            failed += Check("verbose false descriptor exact bool wire",
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(false), "value") == "false");
            failed += Check("developer tier explicit",
                Field(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(false), "tier") == "developer");

            string normalized;
            failed += Check("mutable verbose setting routes",
                FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "TRUE", out normalized) && normalized == "true" &&
                FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "false", out normalized) && normalized == "false");
            failed += Check("invalid bool rejected",
                !FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "1", out normalized) &&
                !FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("verboseDiagnostics", "on", out normalized));
            failed += Check("unadvertised setting rejected",
                !FollowSuiteDescriptorPolicy.TryNormalizeSettingValue("overlayPositionX", "50", out normalized));

            string describe = FollowSuiteDescriptorPolicy.BuildDescribe("0.5.0", new string('s', 500));
            failed += Check("status bounded", Field(describe, "status").Length == FollowSuiteDescriptorPolicy.MaxHubText);
            failed += Check("no private or secret fields exposed",
                !FollowSuiteDescriptorPolicy.ContainsSensitiveFieldName(describe) &&
                !FollowSuiteDescriptorPolicy.ContainsSensitiveFieldName(FollowSuiteDescriptorPolicy.BuildDeveloperSettings(true)));
            failed += Check("safe action allowlist advertised",
                Field(describe, "actions") == "stop,pauseExpedition,resumeExpedition,cancelExpedition,return");

            Console.WriteLine(failed == 0 ? "Follow control descriptor tests: ALL PASS" : "Follow control descriptor tests: FAILURES=" + failed);
            return failed == 0 ? 0 : 1;
        }

        private static int Check(string name, bool ok)
        {
            Console.WriteLine("[Follow ControlApi] " + name + ": " + (ok ? "PASS" : "FAIL"));
            return ok ? 0 : 1;
        }

        private static string Field(string line, string key)
        {
            string[] pairs = (line ?? string.Empty).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = Uri.UnescapeDataString(pairs[i].Substring(0, eq));
                if (k == key) return Uri.UnescapeDataString(pairs[i].Substring(eq + 1));
            }
            return string.Empty;
        }
    }
}
