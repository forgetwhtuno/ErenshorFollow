using System;

namespace ErenshorFollow
{
    internal struct ExpeditionMovementOwnershipInputs
    {
        internal bool ExpeditionActive;
        internal bool ExactLeader;
        internal bool TravelMovementOwned;
        internal bool Combat;
        internal bool ExplicitHold;
        internal bool Regrouping;
        internal bool Paused;
        internal bool TerminalCleanup;
        internal bool CrossingHandoff;
        internal bool NativeZoning;
    }

    internal static class ExpeditionMovementOwnershipPolicy
    {
        internal const float MinimumUsableSpeed = 0.05f;
        internal const float LocomotionSpeedThreshold = 0.08f;
        internal const float LocomotionDistanceThreshold = 0.08f;
        internal const float NearOrderDistance = 0.20f;
        internal const float OwnedFloatTolerance = 0.02f;

        internal static bool ShouldSuppressDoGuard(ExpeditionMovementOwnershipInputs input)
        {
            return input.ExpeditionActive && input.ExactLeader && input.TravelMovementOwned &&
                   !input.Combat && !input.ExplicitHold && !input.Regrouping && !input.Paused &&
                   !input.TerminalCleanup && !input.CrossingHandoff && !input.NativeZoning;
        }

        internal static float SelectTravelSpeed(float nativeRunSpeed, float currentAgentSpeed, float capturedAgentSpeed)
        {
            if (FinitePositive(nativeRunSpeed)) return nativeRunSpeed;
            if (FinitePositive(currentAgentSpeed)) return currentAgentSpeed;
            if (FinitePositive(capturedAgentSpeed)) return capturedAgentSpeed;
            return 0f;
        }

        internal static bool ShouldShowWalking(float velocityMagnitude, float desiredVelocityMagnitude,
            float positionDelta, float sampleSeconds, float distanceToOrder, bool agentStopped)
        {
            if (agentStopped || !Finite(distanceToOrder) || distanceToOrder <= NearOrderDistance) return false;
            float deltaSpeed = sampleSeconds > 0.001f && Finite(positionDelta)
                ? Math.Max(0f, positionDelta) / sampleSeconds : 0f;
            return PositiveAtLeast(velocityMagnitude, LocomotionSpeedThreshold) ||
                   PositiveAtLeast(desiredVelocityMagnitude, LocomotionSpeedThreshold) ||
                   PositiveAtLeast(deltaSpeed, LocomotionDistanceThreshold);
        }

        internal static bool ShouldRestoreOwnedFloat(float currentValue, float lastOwnedValue, float capturedValue)
        {
            return Finite(currentValue) && Finite(lastOwnedValue) && FinitePositive(capturedValue) &&
                   Math.Abs(currentValue - lastOwnedValue) <= OwnedFloatTolerance;
        }

        internal static bool IsCurrentGeneration(int observedOwnerGeneration, int currentOwnerGeneration,
            int observedOrderGeneration, int currentOrderGeneration)
        {
            return observedOwnerGeneration > 0 && observedOrderGeneration > 0 &&
                   observedOwnerGeneration == currentOwnerGeneration &&
                   observedOrderGeneration == currentOrderGeneration;
        }

        internal static int NextGeneration(int current)
        {
            return current >= int.MaxValue - 1 || current < 0 ? 1 : current + 1;
        }

        private static bool PositiveAtLeast(float value, float threshold)
        {
            return Finite(value) && value >= threshold;
        }

        private static bool FinitePositive(float value)
        {
            return Finite(value) && value >= MinimumUsableSpeed;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
