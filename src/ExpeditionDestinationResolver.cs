using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorFollow
{
    // The only source of expedition destinations: Zoneline objects that are live in the loaded scene.
    // No wiki data, no LLM output, no coordinates, and deliberately not ZoneAtlas -- that scriptable-object
    // graph names neighbours but does not prove a walkable exit is currently loaded.
    // Canonical-name resolver only. Runtime routing separately evaluates every live crossing belonging to
    // the canonical destination instead of treating Unity enumeration order as route authority.
    internal static class ExpeditionDestinationResolver
    {
        internal static List<string> ListCanonicalNames()
        {
            List<string> names = new List<string>();
            List<Zoneline> lines = GetLiveNamedLines();
            for (int i = 0; i < lines.Count; i++)
            {
                Zoneline line = lines[i];
                if (!IsUsable(line)) continue;
                string name = line.DestinationZone.Trim();
                if (!names.Exists(delegate(string x) { return string.Equals(x, name, StringComparison.OrdinalIgnoreCase); }))
                    names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        internal static ExpeditionDestination Resolve(string requested, out bool ambiguous)
        {
            bool removingOnly;
            string canonical = ResolveCanonical(requested, out ambiguous, out removingOnly);
            if (canonical == null) return null;

            List<Zoneline> crossings = GetCrossings(canonical, false);
            return crossings.Count == 0
                ? null
                : new ExpeditionDestination(ExpeditionDestinationKind.AdjacentZone, canonical, crossings);
        }

        internal static bool IsCurrentlyReachable(string canonicalName)
        {
            return GetCrossings(canonicalName, false).Count > 0;
        }

        // True when the requested name only matches Zonelines that would dismiss the party, so the caller
        // can explain the refusal instead of reporting "unknown destination".
        internal static bool MatchesOnlyPartyRemovingExit(string requested)
        {
            bool ambiguous;
            bool removingOnly;
            ResolveCanonical(requested, out ambiguous, out removingOnly);
            return !ambiguous && removingOnly;
        }

        internal static string ResolveCanonical(string requested, out bool ambiguous, out bool removingOnly)
        {
            List<Zoneline> lines = GetLiveNamedLines();
            List<RouteCandidatePolicy.DestinationNameCandidate> names = new List<RouteCandidatePolicy.DestinationNameCandidate>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                Zoneline line = lines[i];
                names.Add(new RouteCandidatePolicy.DestinationNameCandidate(
                    line.DestinationZone, line.gameObject != null && line.gameObject.activeInHierarchy, line.RemoveParty));
            }
            return RouteCandidatePolicy.ResolveCanonicalName(names, requested, out ambiguous, out removingOnly);
        }

        internal static List<Zoneline> GetCrossings(string canonicalName, bool includePartyRemoving)
        {
            List<Zoneline> matches = new List<Zoneline>();
            if (string.IsNullOrWhiteSpace(canonicalName)) return matches;
            string canonical = canonicalName.Trim();
            List<Zoneline> lines = GetLiveNamedLines();
            for (int i = 0; i < lines.Count; i++)
            {
                Zoneline line = lines[i];
                if (!line.DestinationZone.Trim().Equals(canonical, StringComparison.OrdinalIgnoreCase)) continue;
                if (!includePartyRemoving && line.RemoveParty) continue;
                matches.Add(line);
            }
            matches.Sort(delegate(Zoneline a, Zoneline b)
            {
                return string.Compare(LocalZoneRoutePlanner.CrossingKey(a), LocalZoneRoutePlanner.CrossingKey(b), StringComparison.Ordinal);
            });
            return matches;
        }

        internal static List<Zoneline> GetLiveNamedLines()
        {
            List<Zoneline> lines = new List<Zoneline>();
            foreach (Zoneline line in UnityEngine.Object.FindObjectsOfType<Zoneline>())
            {
                if (line == null || line.gameObject == null || !line.gameObject.activeInHierarchy ||
                    string.IsNullOrWhiteSpace(line.DestinationZone)) continue;
                lines.Add(line);
            }
            return lines;
        }

        // Zoneline.CallZoning() force-dismisses every GameData.GroupMembers slot when RemoveParty is set,
        // so the leader cannot possibly still be in the party on the far side. Such an exit can never
        // satisfy an expedition's contract; reject it up front rather than failing after the fade.
        private static bool IsUsable(Zoneline line)
        {
            return line != null && line.gameObject != null && line.gameObject.activeInHierarchy &&
                   !string.IsNullOrWhiteSpace(line.DestinationZone) && !line.RemoveParty;
        }
    }
}
