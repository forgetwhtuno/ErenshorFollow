using System;
using System.Reflection;

namespace ErenshorFollow
{
    internal static class FollowUiCampHandoffDeterministicTests
    {
        private static int _failures;

        private static int Main()
        {
            Check("on-screen position stays unchanged", ClampKeepsValidPosition());
            Check("off-screen saved position clamps to screen", ClampRecoversOffscreenPosition());
            Check("invalid saved position recovers", ClampRecoversInvalidPosition());
            Check("tiny resolution keeps panel origin reachable", ClampHandlesTinyScreen());
            Check("arrival actions hidden without verified arrival", ArrivalActionsRequireVerifiedArrival());
            Check("Camp Here requires capability", CampActionRequiresCapability());
            Check("active/pending camp suppresses duplicate Camp Here", CampActionSuppressesDuplicates());
            Check("Return follows coordinator admission", ReturnVisibilityIsIndependent());
            Check("Campmaster absence binds safely", MissingCapabilityIsSafe());
            Check("Follow has no Campmaster assembly dependency", NoCampmasterAssemblyDependency());
            Check("compatible Campmaster capability binds", CompatibleCapabilityBinds());
            Check("incompatible reflection shape fails closed", BadReflectionShapeFailsClosed());
            Check("unknown Campmaster schema fails closed", UnknownSchemaFailsClosed());
            Check("accepted request is surfaced", AcceptedRequestWorks());
            Check("rejected request preserves reason", RejectedRequestWorks());

            Console.WriteLine(_failures == 0
                ? "Erenshor Follow UI/Camp handoff deterministic tests: ALL PASS"
                : "Erenshor Follow UI/Camp handoff deterministic tests: " + _failures + " FAIL");
            return _failures == 0 ? 0 : 1;
        }

        private static void Check(string name, bool passed)
        {
            Console.WriteLine((passed ? "PASS  " : "FAIL  ") + name);
            if (!passed) _failures++;
        }

        private static bool ClampKeepsValidPosition()
        {
            TravelOverlayPoint p = TravelOverlayLogic.ClampPosition(50f, 80f, 258f, 76f, 1920f, 1080f, 8f);
            return Near(p.X, 50f) && Near(p.Y, 80f);
        }

        private static bool ClampRecoversOffscreenPosition()
        {
            TravelOverlayPoint p = TravelOverlayLogic.ClampPosition(5000f, -40f, 258f, 112f, 1280f, 720f, 8f);
            return Near(p.X, 1014f) && Near(p.Y, 8f);
        }

        private static bool ClampRecoversInvalidPosition()
        {
            TravelOverlayPoint p = TravelOverlayLogic.ClampPosition(float.NaN, float.PositiveInfinity, 258f, 76f, 800f, 600f, 8f);
            return Near(p.X, 8f) && Near(p.Y, 8f);
        }

        private static bool ClampHandlesTinyScreen()
        {
            TravelOverlayPoint p = TravelOverlayLogic.ClampPosition(100f, 100f, 258f, 112f, 200f, 100f, 8f);
            return Near(p.X, 0f) && Near(p.Y, 0f);
        }

        private static bool ArrivalActionsRequireVerifiedArrival()
        {
            ArrivalActionVisibility v = TravelOverlayLogic.ResolveArrivalActions(false, true, false, false, true);
            return !v.ShowCampHere && !v.ShowReturn;
        }

        private static bool CampActionRequiresCapability()
        {
            ArrivalActionVisibility absent = TravelOverlayLogic.ResolveArrivalActions(true, false, false, false, false);
            ArrivalActionVisibility present = TravelOverlayLogic.ResolveArrivalActions(true, true, false, false, false);
            return !absent.ShowCampHere && present.ShowCampHere;
        }

        private static bool CampActionSuppressesDuplicates()
        {
            ArrivalActionVisibility active = TravelOverlayLogic.ResolveArrivalActions(true, true, true, false, false);
            ArrivalActionVisibility pending = TravelOverlayLogic.ResolveArrivalActions(true, true, false, true, false);
            return !active.ShowCampHere && !pending.ShowCampHere;
        }

        private static bool ReturnVisibilityIsIndependent()
        {
            ArrivalActionVisibility v = TravelOverlayLogic.ResolveArrivalActions(true, false, false, false, true);
            return !v.ShowCampHere && v.ShowReturn;
        }

        private static bool MissingCapabilityIsSafe()
        {
            return CampmasterReflectionBinder.FindBinding(new Assembly[0]) == null;
        }

        private static bool NoCampmasterAssemblyDependency()
        {
            AssemblyName[] references = typeof(CampmasterIntegrationBridge).Assembly.GetReferencedAssemblies();
            for (int i = 0; i < references.Length; i++)
            {
                string name = references[i].Name ?? string.Empty;
                if (name.IndexOf("Campmaster", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
        }

        private static bool CompatibleCapabilityBinds()
        {
            CampmasterControlBinding binding = CampmasterReflectionBinder.TryBindCandidate(
                typeof(ErenshorCampmaster.CampmasterControlApi), true);
            return binding != null && binding.IsAvailable && !binding.IsHuntCampActive;
        }

        private static bool BadReflectionShapeFailsClosed()
        {
            return CampmasterReflectionBinder.TryBindCandidate(typeof(BrokenControlApi), false) == null;
        }

        private static bool UnknownSchemaFailsClosed()
        {
            return CampmasterReflectionBinder.TryBindCandidate(typeof(FutureControlApi), false) == null;
        }

        private static bool AcceptedRequestWorks()
        {
            ErenshorCampmaster.CampmasterControlApi.AcceptNext = true;
            ErenshorCampmaster.CampmasterControlApi.NextFailure = null;
            CampmasterControlBinding binding = CampmasterReflectionBinder.TryBindCandidate(
                typeof(ErenshorCampmaster.CampmasterControlApi), true);
            string failure;
            return binding != null && binding.TryDeclareHere(out failure) && failure == null;
        }

        private static bool RejectedRequestWorks()
        {
            ErenshorCampmaster.CampmasterControlApi.AcceptNext = false;
            ErenshorCampmaster.CampmasterControlApi.NextFailure = "rejected for test";
            CampmasterControlBinding binding = CampmasterReflectionBinder.TryBindCandidate(
                typeof(ErenshorCampmaster.CampmasterControlApi), true);
            string failure;
            return binding != null && !binding.TryDeclareHere(out failure) && failure == "rejected for test";
        }

        private static bool Near(float a, float b)
        {
            return Math.Abs(a - b) < 0.001f;
        }

        public static class BrokenControlApi
        {
            public const int SchemaVersion = 1;
            public static bool IsAvailable { get { return true; } }
            public static bool IsHuntCampActive { get { return false; } }
            public static string TryDeclareHere(out string failure) { failure = null; return "wrong return type"; }
        }

        public static class FutureControlApi
        {
            public const int SchemaVersion = 2;
            public static bool IsAvailable { get { return true; } }
            public static bool IsHuntCampActive { get { return false; } }
            public static bool TryDeclareHere(out string failure) { failure = null; return true; }
        }
    }
}

namespace ErenshorCampmaster
{
    public static class CampmasterControlApi
    {
        public const int SchemaVersion = 1;
        public static bool AcceptNext = true;
        public static string NextFailure;
        public static bool IsAvailable { get { return true; } }
        public static bool IsHuntCampActive { get { return false; } }

        public static bool TryDeclareHere(out string failure)
        {
            failure = NextFailure;
            return AcceptNext;
        }
    }
}
