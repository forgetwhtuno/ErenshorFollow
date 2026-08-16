using System;
using HarmonyLib;
using UnityEngine;

namespace ErenshorFollow
{
    // Read-only observation of the native trigger boundary proven by the installed-assembly investigation.
    // The prefix deliberately does not alter __instance, the collider, GameData.Zoning, or native flow.
    [HarmonyPatch(typeof(Zoneline), "OnTriggerEnter")]
    internal static class ZonelineTriggerObserver
    {
        [HarmonyPrefix]
        private static void Prefix(Zoneline __instance, Collider other)
        {
            try { LeaderController.NoteNativeZonelineTrigger(__instance, other); }
            catch (Exception ex)
            {
                try
                {
                    if (ErenshorFollowPlugin.Instance != null)
                        ErenshorFollowPlugin.Instance.LogDebug("Zoneline trigger observation failed: " + ex.GetType().Name);
                }
                catch { }
            }
        }
    }
}
