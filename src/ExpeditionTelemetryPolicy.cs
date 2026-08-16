using System;

namespace ErenshorFollow
{
    // Release-default logging keeps durable lifecycle outcomes while routing writer/agent/route
    // traces behind the explicit Verbose diagnostics setting.
    internal static class ExpeditionTelemetryPolicy
    {
        internal static bool EmitMovement(bool verbose) { return verbose; }

        internal static bool EmitPhase(bool verbose, string phase)
        {
            if (verbose) return true;
            string value = (phase ?? string.Empty).Trim();
            return value.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("arrived", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("leader_reacquired", StringComparison.OrdinalIgnoreCase);
        }
    }
}
