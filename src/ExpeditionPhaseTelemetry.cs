using System;

namespace ErenshorFollow
{
    // Phase-boundary telemetry only. It intentionally emits at most one line for an identical phase/detail
    // signature and never logs per-frame movement data.
    internal static class ExpeditionPhaseTelemetry
    {
        private static int _sessionId;
        private static string _lastPhase = "idle";
        private static string _lastDetail = "none";
        private static string _lastSignature;

        internal static int CurrentSessionId { get { return _sessionId; } }
        internal static string LastPhase { get { return _lastPhase; } }
        internal static string LastDetail { get { return _lastDetail; } }

        internal static void Begin(int sessionId)
        {
            _sessionId = sessionId;
            _lastPhase = "forming";
            _lastDetail = "none";
            _lastSignature = null;
        }

        internal static void Record(string phase, string detail)
        {
            string safePhase = Safe(phase, "unknown", 48);
            string safeDetail = Safe(detail, "none", 280);
            string signature = _sessionId + "|" + safePhase + "|" + safeDetail;
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal)) return;
            _lastSignature = signature;
            _lastPhase = safePhase;
            _lastDetail = safeDetail;
            if (!ExpeditionTelemetryPolicy.EmitPhase(ErenshorFollowPlugin.VerboseDiagnostics, safePhase)) return;
            try
            {
                if (ErenshorFollowPlugin.Instance != null)
                    ErenshorFollowPlugin.Instance.LogInfo("[Expedition phase] session=" + _sessionId +
                        " phase=" + safePhase + " | " + safeDetail);
            }
            catch { }
        }

        internal static string Describe()
        {
            return "session=" + _sessionId + " phase=" + _lastPhase + " detail=" + _lastDetail;
        }

        internal static void Reset()
        {
            _sessionId = 0;
            _lastPhase = "idle";
            _lastDetail = "none";
            _lastSignature = null;
        }

        private static string Safe(string value, string fallback, int maxLength)
        {
            string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Replace("\r", " ").Replace("\n", " ").Trim();
            return result.Length <= maxLength ? result : result.Substring(0, maxLength);
        }
    }
}
