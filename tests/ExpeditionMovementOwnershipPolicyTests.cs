using System;

namespace ErenshorFollow
{
    internal static class ExpeditionMovementOwnershipPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static ExpeditionMovementOwnershipInputs Travel()
        {
            ExpeditionMovementOwnershipInputs value = new ExpeditionMovementOwnershipInputs();
            value.ExpeditionActive = true;
            value.ExactLeader = true;
            value.TravelMovementOwned = true;
            return value;
        }

        private static void DoGuardBoundary()
        {
            ExpeditionMovementOwnershipInputs x = Travel();
            Assert(ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x),
                "exact active expedition travel leader suppresses vanilla DoGuard");

            x.ExactLeader = false;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "non-leader DoGuard remains native");
            x = Travel(); x.Combat = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "combat DoGuard remains native");
            x = Travel(); x.ExplicitHold = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "explicit hold remains native");
            x = Travel(); x.Regrouping = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "regroup DoGuard remains native");
            x = Travel(); x.Paused = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "paused expedition leaves DoGuard native");
            x = Travel(); x.CrossingHandoff = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "crossing handoff leaves DoGuard native");
            x = Travel(); x.NativeZoning = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "native zoning leaves DoGuard native");
            x = Travel(); x.TerminalCleanup = true;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "terminal cleanup leaves DoGuard native");
            x = Travel(); x.TravelMovementOwned = false;
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard(x), "released movement ownership leaves DoGuard native");
        }

        private static void SpeedPolicy()
        {
            Assert(Math.Abs(ExpeditionMovementOwnershipPolicy.SelectTravelSpeed(5.4f, 2f, 3f) - 5.4f) < 0.001f,
                "verified native run speed wins");
            Assert(Math.Abs(ExpeditionMovementOwnershipPolicy.SelectTravelSpeed(0f, 2.5f, 3f) - 2.5f) < 0.001f,
                "current usable agent speed is safe fallback");
            Assert(Math.Abs(ExpeditionMovementOwnershipPolicy.SelectTravelSpeed(float.NaN, 0f, 3.1f) - 3.1f) < 0.001f,
                "captured usable speed is final fallback");
            Assert(ExpeditionMovementOwnershipPolicy.SelectTravelSpeed(0f, 0f, 0f) == 0f,
                "speed policy does not invent an arbitrary run speed");
            Assert(ExpeditionMovementOwnershipPolicy.ShouldRestoreOwnedFloat(5.0f, 5.0f, 3.0f),
                "owned unchanged speed may restore captured native value");
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldRestoreOwnedFloat(4.0f, 5.0f, 3.0f),
                "external speed change is never overwritten by stale snapshot");
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldRestoreOwnedFloat(5.0f, 5.0f, 0f),
                "zero captured speed is not restored into later native control");
        }

        private static void LocomotionPolicy()
        {
            Assert(ExpeditionMovementOwnershipPolicy.ShouldShowWalking(0.2f, 0f, 0f, 0.1f, 5f, false),
                "actual velocity shows walking");
            Assert(ExpeditionMovementOwnershipPolicy.ShouldShowWalking(0f, 0.3f, 0f, 0.1f, 5f, false),
                "desired velocity shows walking during acquisition");
            Assert(ExpeditionMovementOwnershipPolicy.ShouldShowWalking(0f, 0f, 0.2f, 1f, 5f, false),
                "position delta can prove locomotion without reported velocity");
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldShowWalking(0.4f, 0.4f, 0.2f, 1f, 5f, true),
                "stopped agent never forces Walking true");
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldShowWalking(0.4f, 0.4f, 0.2f, 1f, 0.1f, false),
                "arrival-distance leader is not forced into walking animation");
            Assert(!ExpeditionMovementOwnershipPolicy.ShouldShowWalking(0f, 0f, 0f, 1f, 5f, false),
                "stationary leader remains idle");
        }

        private static void GenerationPolicy()
        {
            Assert(ExpeditionMovementOwnershipPolicy.IsCurrentGeneration(3, 3, 7, 7),
                "current leg/order generation is accepted");
            Assert(!ExpeditionMovementOwnershipPolicy.IsCurrentGeneration(2, 3, 7, 7),
                "stale leg generation rejected after zone/reacquisition");
            Assert(!ExpeditionMovementOwnershipPolicy.IsCurrentGeneration(3, 3, 6, 7),
                "stale reissue generation rejected");
            Assert(ExpeditionMovementOwnershipPolicy.NextGeneration(0) == 1 &&
                   ExpeditionMovementOwnershipPolicy.NextGeneration(4) == 5 &&
                   ExpeditionMovementOwnershipPolicy.NextGeneration(int.MaxValue) == 1,
                "movement generations remain positive and bounded");
        }

        public static int Main()
        {
            DoGuardBoundary();
            SpeedPolicy();
            LocomotionPolicy();
            GenerationPolicy();
            Console.WriteLine("Expedition movement ownership policy tests passed: " + _passed);
            return 0;
        }
    }
}
