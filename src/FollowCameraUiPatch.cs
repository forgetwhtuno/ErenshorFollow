using System.Reflection;
using HarmonyLib;

namespace ErenshorFollow
{
    // Current installed camera evidence shows ModernControls gates mouse-look through UsingUI(), while
    // GameData.DraggingUIElement is not consulted by that branch. This postfix is monotonic: Follow can
    // only change false -> true while it owns a retained-ui left-button gesture; it never turns native UI
    // detection off and has no SuiteHub dependency. A runtime IL/member proof verifies the entire native
    // control relationship before Harmony is allowed to install the postfix.
    [HarmonyPatch(typeof(CameraController), "UsingUI")]
    internal static class FollowCameraUsingUiPatch
    {
        internal static bool ShapeVerified { get; private set; }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            MethodInfo method;
            ShapeVerified = FollowCameraCompatibility.VerifyUsingUiBoundary(out method);
            return ShapeVerified;
        }

        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            try
            {
                if (!__result && FollowUiDragGuard.OwnsPointerGesture) __result = true;
            }
            catch { }
        }
    }
}
