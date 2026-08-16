using System;
using System.Reflection;
using HarmonyLib;

namespace ErenshorFollow
{
    // Exact ownership boundary for the verified vanilla conflict:
    // SimPlayer.Update -> GroupedWithPlayerLogic -> DoGuard.
    // Only the exact active Expedition leader is suppressed, and only while Follow explicitly owns
    // ordinary pre-crossing travel. Every other Sim/state continues through vanilla unchanged.
    [HarmonyPatch(typeof(SimPlayer), "DoGuard")]
    internal static class ExpeditionDoGuardPatch
    {
        internal static bool ShapeVerified { get; private set; }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            MethodInfo method = AccessTools.Method(typeof(SimPlayer), "DoGuard");
            ShapeVerified = method != null && method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
            return ShapeVerified;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SimPlayer __instance, ref bool __state)
        {
            __state = false;
            try
            {
                if (!LeaderController.IsExactExpeditionLeader(__instance)) return true;
                LeaderController.NoteMovementBoundary("Native.DoGuard.before");
                if (!LeaderController.ShouldSuppressNativeDoGuard(__instance)) return true;
                __state = true;
                LeaderController.NoteNativeDoGuardSuppressed();
                return false;
            }
            catch
            {
                // Failure-open is deliberate: an uncertain patch condition must never disable native AI.
                return true;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(SimPlayer __instance, bool __state)
        {
            try
            {
                if (__state || !LeaderController.IsExactExpeditionLeader(__instance)) return;
                LeaderController.NoteMovementWriter("Native.DoGuard");
                LeaderController.NoteMovementBoundary("Native.DoGuard.after");
            }
            catch { }
        }
    }
}
