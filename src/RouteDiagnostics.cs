using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    internal static class RouteDiagnostics
    {
        internal static void Report(string requested)
        {
            string query = requested == null ? string.Empty : requested.Trim();
            Say("[Erenshor Route Diag] Scene: " + Safe(SceneManager.GetActiveScene().name));
            Say("[Erenshor Route Diag] Requested: " + (query.Length == 0 ? "<missing>" : query));
            if (query.Length == 0)
            {
                Say("[Erenshor Route Diag] Usage: /elead diag <destination zone>");
                return;
            }

            bool ambiguous;
            bool removingOnly;
            string canonical = ExpeditionDestinationResolver.ResolveCanonical(query, out ambiguous, out removingOnly);
            if (ambiguous)
            {
                Say("[Erenshor Route Diag] Canonical destination: <ambiguous>");
                Say("[Erenshor Route Diag] Different live destination names match this request; type a longer name.");
                return;
            }
            if (canonical == null && removingOnly)
                canonical = UniqueMatchingName(query);
            Say("[Erenshor Route Diag] Canonical destination: " + (canonical == null ? "<none>" : canonical));
            if (canonical == null)
            {
                // /elead itself supports world-atlas itineraries when the requested destination is not
                // adjacent. Make the diagnostic follow the same decision tree instead of incorrectly
                // stopping at "no live named Zoneline" for a valid multi-hop request such as Azure.
                List<string> atlasRoute;
                bool atlasAmbiguous;
                string atlasFailure;
                if (ZoneAtlasRoutePlanner.TryBuild(SceneManager.GetActiveScene().name, query,
                    ExpeditionDestinationResolver.ListCanonicalNames(), out atlasRoute, out atlasAmbiguous, out atlasFailure) &&
                    atlasRoute != null && atlasRoute.Count >= 2)
                {
                    Say("[Erenshor Route Diag] Atlas itinerary: " + string.Join(" -> ", atlasRoute.ToArray()));
                    canonical = atlasRoute[1];
                    Say("[Erenshor Route Diag] Current first hop: " + canonical);
                }
                else
                {
                    if (atlasAmbiguous)
                        Say("[Erenshor Route Diag] Atlas destination is ambiguous; type a longer world-zone name.");
                    else
                        Say("[Erenshor Route Diag] No direct Zoneline or atlas itinerary matches that request" +
                            (string.IsNullOrWhiteSpace(atlasFailure) ? "." : ": " + atlasFailure));
                    return;
                }
            }

            List<Zoneline> crossings = ExpeditionDestinationResolver.GetCrossings(canonical, true);
            Say("[Erenshor Route Diag] Matching Zonelines: " + crossings.Count);
            if (GameData.PlayerControl == null)
            {
                Say("[Erenshor Route Diag] PlayerControl is unavailable; route preflight was not run.");
                return;
            }
            Vector3 start = GameData.PlayerControl.transform.position;
            LocalZoneRoutePlanner.Plan plan = LocalZoneRoutePlanner.Build(start, crossings, true);
            Say("[Erenshor Route Diag] Path start: player " + LocalZoneRoutePlanner.FormatVector(start) +
                " | NavMesh sample=" + (plan.StartSampled ? "success " + LocalZoneRoutePlanner.FormatVector(plan.StartSamplePosition) : "failed"));

            for (int i = 0; i < plan.Crossings.Count; i++)
            {
                LocalZoneRoutePlanner.CrossingInspection inspection = plan.Crossings[i];
                Say("[Erenshor Route Diag] Candidate " + (i + 1) + ": active=" + inspection.Active +
                    " RemoveParty=" + inspection.RemoveParty + " transform=" + LocalZoneRoutePlanner.FormatVector(inspection.TransformPosition));
                for (int c = 0; c < inspection.ColliderInfo.Count; c++)
                    Say("[Erenshor Route Diag]   collider: " + inspection.ColliderInfo[c]);
                Say("[Erenshor Route Diag]   NavMesh sample=" + (inspection.SampledApproachCount > 0 ? "success" : "failed") +
                    " | sampled approaches=" + inspection.SampledApproachCount + " | " + DescribeBest(inspection));
            }

            if (plan.Options.Count == 0)
            {
                Say("[Erenshor Route Diag] Selected candidate: <none>");
                Say("[Erenshor Route Diag] None selected: " + RejectionSummary(plan));
                return;
            }

            LocalZoneRoutePlanner.RouteOption selected = plan.Options[0];
            RouteCandidatePolicy.Candidate candidate = selected.Evaluation.Candidate;
            Say("[Erenshor Route Diag] Selected candidate: " + selected.StableKey +
                " approach=" + LocalZoneRoutePlanner.FormatVector(selected.Approach) +
                " | " + selected.Evaluation.Acceptance +
                (selected.NeedsNativeProof ? " (requires bounded native progress proof)" : string.Empty) +
                " | reason=" + selected.Evaluation.Reason +
                " | corners=" + candidate.CornerCount +
                " | endpoint->crossing=" + candidate.EndpointDistanceToCrossing.ToString("F2") + "m");
        }

        private static string DescribeBest(LocalZoneRoutePlanner.CrossingInspection inspection)
        {
            if (inspection == null || inspection.Evaluations.Count == 0) return "no sampled approach";
            if (inspection.BestAccepted != null)
            {
                RouteCandidatePolicy.Evaluation evaluation = inspection.BestAccepted.Evaluation;
                RouteCandidatePolicy.Candidate candidate = evaluation.Candidate;
                return "best=" + candidate.Path + " corners=" + candidate.CornerCount +
                       " endpoint->crossing=" + candidate.EndpointDistanceToCrossing.ToString("F2") + "m" +
                       " route=" + candidate.RouteLength.ToString("F2") + "m" +
                       " accepted=" + evaluation.Acceptance + " reason=" + evaluation.Reason;
            }

            RouteCandidatePolicy.Evaluation first = inspection.Evaluations[0];
            for (int i = 1; i < inspection.Evaluations.Count; i++)
            {
                RouteCandidatePolicy.Evaluation current = inspection.Evaluations[i];
                if (current.Candidate != null && first.Candidate != null &&
                    current.Candidate.EndpointDistanceToCrossing < first.Candidate.EndpointDistanceToCrossing)
                    first = current;
            }
            RouteCandidatePolicy.Candidate rejected = first.Candidate;
            return "best=" + (rejected == null ? "Invalid" : rejected.Path.ToString()) +
                   " corners=" + (rejected == null ? 0 : rejected.CornerCount) +
                   " endpoint->crossing=" + (rejected == null ? "n/a" : rejected.EndpointDistanceToCrossing.ToString("F2") + "m") +
                   " rejected=" + first.Reason;
        }

        private static string RejectionSummary(LocalZoneRoutePlanner.Plan plan)
        {
            List<string> reasons = new List<string>();
            for (int i = 0; i < plan.Crossings.Count; i++)
            {
                LocalZoneRoutePlanner.CrossingInspection inspection = plan.Crossings[i];
                for (int e = 0; e < inspection.Evaluations.Count; e++)
                {
                    string reason = inspection.Evaluations[e].Reason;
                    if (!reasons.Contains(reason)) reasons.Add(reason);
                    if (reasons.Count >= 4) break;
                }
                if (reasons.Count >= 4) break;
            }
            return reasons.Count == 0 ? "no NavMesh approaches were sampled" : string.Join("; ", reasons.ToArray());
        }

        private static string UniqueMatchingName(string requested)
        {
            List<string> names = new List<string>();
            List<Zoneline> lines = ExpeditionDestinationResolver.GetLiveNamedLines();
            for (int i = 0; i < lines.Count; i++)
            {
                string name = lines[i].DestinationZone.Trim();
                if (name.IndexOf(requested, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!names.Exists(delegate(string x) { return x.Equals(name, StringComparison.OrdinalIgnoreCase); })) names.Add(name);
            }
            return names.Count == 1 ? names[0] : null;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<unknown>" : value.Trim();
        }

        private static void Say(string message)
        {
            try
            {
                if (ErenshorFollowPlugin.Instance != null)
                {
                    ErenshorFollowPlugin.Instance.Chat(message, "lightblue");
                    ErenshorFollowPlugin.Instance.LogDebug(message);
                }
            }
            catch { }
        }
    }

    // Kept separate from the existing command owner so the routing pass does not disturb newer
    // deterministic chat-routing logic. Clearing the input means the existing prefix sees an empty line.
    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class RouteDiagnosticCommandPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                if (__instance == null || __instance.typed == null || string.IsNullOrWhiteSpace(__instance.typed.text)) return true;
                string raw = __instance.typed.text.Trim();
                const string prefix = "/elead diag";
                if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    (raw.Length > prefix.Length && !char.IsWhiteSpace(raw[prefix.Length]))) return true;
                string requested = raw.Length == prefix.Length ? string.Empty : raw.Substring(prefix.Length).Trim();
                __instance.typed.text = string.Empty;
                RouteDiagnostics.Report(requested);
                return false;
            }
            catch (Exception ex)
            {
                try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogError("Route diagnostic failed: " + ex); } catch { }
                return true;
            }
        }
    }
}
