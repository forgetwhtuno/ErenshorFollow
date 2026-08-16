using System;

namespace ErenshorFollow.Tests
{
    internal static class ExpeditionTelemetryPolicyTests
    {
        private static int Main()
        {
            int assertions = 0;
            Assert(!ExpeditionTelemetryPolicy.EmitMovement(false), "default movement tracing is quiet", ref assertions);
            Assert(ExpeditionTelemetryPolicy.EmitMovement(true), "verbose movement tracing is enabled", ref assertions);
            Assert(!ExpeditionTelemetryPolicy.EmitPhase(false, "route_candidate"), "default route-corner phase is quiet", ref assertions);
            Assert(!ExpeditionTelemetryPolicy.EmitPhase(false, "command_received"), "default command trace is quiet", ref assertions);
            Assert(ExpeditionTelemetryPolicy.EmitPhase(false, "failed"), "failure remains visible", ref assertions);
            Assert(ExpeditionTelemetryPolicy.EmitPhase(false, "cancelled"), "cancellation remains visible", ref assertions);
            Assert(ExpeditionTelemetryPolicy.EmitPhase(false, "arrived"), "arrival remains visible", ref assertions);
            Assert(ExpeditionTelemetryPolicy.EmitPhase(false, "leader_reacquired"), "crossing reacquisition remains visible", ref assertions);
            Assert(ExpeditionTelemetryPolicy.EmitPhase(true, "route_candidate"), "verbose emits detailed phases", ref assertions);
            Console.WriteLine("ExpeditionTelemetryPolicyTests: PASS (" + assertions + " assertions)");
            return 0;
        }

        private static void Assert(bool condition, string label, ref int assertions)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(label);
        }
    }
}
