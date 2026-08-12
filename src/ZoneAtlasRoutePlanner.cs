using System;
using System.Collections.Generic;

namespace ErenshorFollow
{
    // ZoneAtlas supplies only a game-authored itinerary. Every hop still has to resolve to a live,
    // non-party-removing Zoneline before movement begins; this class never moves or zones anything.
    internal static class ZoneAtlasRoutePlanner
    {
        internal static bool TryBuild(string origin, string requested, out List<string> route,
            out bool ambiguous, out string failure)
        {
            return TryBuild(origin, requested, null, out route, out ambiguous, out failure);
        }

        internal static bool TryBuild(string origin, string requested, IList<string> allowedFirstHops,
            out List<string> route, out bool ambiguous, out string failure)
        {
            route = new List<string>();
            ambiguous = false;
            failure = null;
            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(requested))
            {
                failure = "An origin and destination are required.";
                return false;
            }

            Dictionary<string, ZoneAtlasEntry> entries = ReadEntries();
            string start = ResolveName(entries, origin, out ambiguous);
            if (start == null)
            {
                failure = "The current zone is not present in the game's zone atlas.";
                return false;
            }
            string goal = ResolveName(entries, requested, out ambiguous);
            if (goal == null)
            {
                failure = ambiguous ? "More than one world zone matches that destination." :
                    "That destination is not present in the game's zone atlas.";
                return false;
            }
            if (start.Equals(goal, StringComparison.OrdinalIgnoreCase))
            {
                route.Add(start);
                return true;
            }

            Queue<string> open = new Queue<string>();
            Dictionary<string, string> previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            open.Enqueue(start);
            previous[start] = null;
            while (open.Count > 0)
            {
                string current = open.Dequeue();
                ZoneAtlasEntry entry;
                if (!entries.TryGetValue(current, out entry) || entry == null || entry.NeighboringZones == null) continue;
                for (int i = 0; i < entry.NeighboringZones.Count; i++)
                {
                    string neighbor = Canonical(entries, entry.NeighboringZones[i]);
                    if (current.Equals(start, StringComparison.OrdinalIgnoreCase) &&
                        allowedFirstHops != null &&
                        !ContainsName(allowedFirstHops, neighbor)) continue;
                    if (neighbor == null || previous.ContainsKey(neighbor)) continue;
                    previous[neighbor] = current;
                    if (neighbor.Equals(goal, StringComparison.OrdinalIgnoreCase))
                    {
                        BuildPath(previous, goal, route);
                        return true;
                    }
                    open.Enqueue(neighbor);
                }
            }

            failure = "The game's zone atlas has no route from " + start + " to " + goal + ".";
            return false;
        }

        internal static List<string> ListZoneNames()
        {
            List<string> names = new List<string>(ReadEntries().Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static Dictionary<string, ZoneAtlasEntry> ReadEntries()
        {
            Dictionary<string, ZoneAtlasEntry> entries = new Dictionary<string, ZoneAtlasEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ZoneAtlasEntry[] atlas = ZoneAtlas.Atlas;
                if (atlas == null) return entries;
                for (int i = 0; i < atlas.Length; i++)
                {
                    ZoneAtlasEntry entry = atlas[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ZoneName)) continue;
                    string name = entry.ZoneName.Trim();
                    if (!entries.ContainsKey(name)) entries.Add(name, entry);
                }
            }
            catch { }
            return entries;
        }

        private static string ResolveName(Dictionary<string, ZoneAtlasEntry> entries, string requested, out bool ambiguous)
        {
            ambiguous = false;
            if (entries == null || string.IsNullOrWhiteSpace(requested)) return null;
            string query = requested.Trim();
            ZoneAtlasEntry exact;
            if (entries.TryGetValue(query, out exact)) return exact.ZoneName.Trim();
            string match = null;
            foreach (string name in entries.Keys)
            {
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (match != null && !match.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    ambiguous = true;
                    return null;
                }
                match = name;
            }
            return match;
        }

        private static string Canonical(Dictionary<string, ZoneAtlasEntry> entries, string value)
        {
            if (entries == null || string.IsNullOrWhiteSpace(value)) return null;
            ZoneAtlasEntry entry;
            return entries.TryGetValue(value.Trim(), out entry) && entry != null && !string.IsNullOrWhiteSpace(entry.ZoneName)
                ? entry.ZoneName.Trim() : null;
        }

        private static void BuildPath(Dictionary<string, string> previous, string goal, List<string> route)
        {
            string cursor = goal;
            while (cursor != null)
            {
                route.Add(cursor);
                string prior;
                if (!previous.TryGetValue(cursor, out prior)) break;
                cursor = prior;
            }
            route.Reverse();
        }

        private static bool ContainsName(IList<string> names, string value)
        {
            if (names == null || string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < names.Count; i++)
                if (!string.IsNullOrWhiteSpace(names[i]) && names[i].Trim().Equals(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
