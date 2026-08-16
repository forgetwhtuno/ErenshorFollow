using System;
using System.Collections.Generic;

namespace ErenshorFollow
{
    // Unity-free representation of a candidate atlas itinerary. The graph proves only that Erenshor's
    // ZoneAtlas contains a plausible chain. Runtime travel must separately prove each current leg using
    // a live non-party-removing Zoneline before any movement order begins.
    internal sealed class ExpeditionRouteChoice
    {
        internal readonly string DestinationName;
        internal readonly List<string> Route;

        internal ExpeditionRouteChoice(string destinationName, IList<string> route)
        {
            DestinationName = destinationName ?? string.Empty;
            Route = new List<string>();
            if (route == null) return;
            for (int i = 0; i < route.Count; i++)
                if (!string.IsNullOrWhiteSpace(route[i])) Route.Add(route[i].Trim());
        }

        internal bool Nearby { get { return Route.Count == 2; } }
        internal int TransitionCount { get { return Math.Max(0, Route.Count - 1); } }
    }

    internal static class ZoneRouteGraphPolicy
    {
        internal static bool TryBuild(Dictionary<string, List<string>> graph, string origin, string requested,
            IList<string> allowedFirstHops, out List<string> route, out bool ambiguous, out string failure)
        {
            route = new List<string>();
            ambiguous = false;
            failure = null;
            if (graph == null || graph.Count == 0)
            {
                failure = "The game's zone atlas is unavailable.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(requested))
            {
                failure = "An origin and destination are required.";
                return false;
            }

            string start = ResolveName(graph, origin, out ambiguous);
            if (start == null)
            {
                failure = ambiguous ? "More than one world zone matches the current zone." :
                    "The current zone is not present in the game's zone atlas.";
                return false;
            }
            string goal = ResolveName(graph, requested, out ambiguous);
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

            // ZoneAtlasEntry.NeighboringZones describes world adjacency, not movement authority.
            // Some atlas assets can carry the relationship on only one endpoint, so normalize those
            // authored links into an undirected traversal graph. This never authorizes a crossing: the
            // first hop is still constrained by the caller's live Zoneline set, and every later leg is
            // re-authorized from the newly loaded scene before movement starts.
            Dictionary<string, List<string>> traversal = BuildTraversalGraph(graph);

            Queue<string> open = new Queue<string>();
            Dictionary<string, string> previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            open.Enqueue(start);
            previous[start] = null;

            while (open.Count > 0)
            {
                string current = open.Dequeue();
                List<string> neighbors;
                if (!traversal.TryGetValue(current, out neighbors) || neighbors == null) continue;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    string neighbor = Canonical(graph, neighbors[i]);
                    if (neighbor == null) continue;
                    if (current.Equals(start, StringComparison.OrdinalIgnoreCase) &&
                        allowedFirstHops != null && !ContainsName(allowedFirstHops, neighbor)) continue;
                    if (previous.ContainsKey(neighbor)) continue;
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

        internal static List<ExpeditionRouteChoice> ListReachable(Dictionary<string, List<string>> graph,
            string origin, IList<string> allowedFirstHops)
        {
            List<ExpeditionRouteChoice> choices = new List<ExpeditionRouteChoice>();
            if (graph == null || graph.Count == 0 || string.IsNullOrWhiteSpace(origin)) return choices;

            bool originAmbiguous;
            string start = ResolveName(graph, origin, out originAmbiguous);
            if (start == null || originAmbiguous) return choices;

            List<string> destinations = new List<string>(graph.Keys);
            destinations.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < destinations.Count; i++)
            {
                string destination = destinations[i];
                if (destination.Equals(start, StringComparison.OrdinalIgnoreCase)) continue;
                List<string> route;
                bool ambiguous;
                string failure;
                if (!TryBuild(graph, start, destination, allowedFirstHops, out route, out ambiguous, out failure)) continue;
                if (route.Count < 2) continue;
                choices.Add(new ExpeditionRouteChoice(destination, route));
            }

            choices.Sort(delegate(ExpeditionRouteChoice a, ExpeditionRouteChoice b)
            {
                int nearby = (a.Nearby ? 0 : 1).CompareTo(b.Nearby ? 0 : 1);
                if (nearby != 0) return nearby;
                int hops = a.TransitionCount.CompareTo(b.TransitionCount);
                if (hops != 0) return hops;
                return string.Compare(a.DestinationName, b.DestinationName, StringComparison.OrdinalIgnoreCase);
            });
            return choices;
        }

        internal static Dictionary<string, List<string>> BuildTraversalGraph(Dictionary<string, List<string>> graph)
        {
            Dictionary<string, List<string>> traversal = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (graph == null) return traversal;

            foreach (string key in graph.Keys)
            {
                string canonical = Canonical(graph, key);
                if (canonical != null && !traversal.ContainsKey(canonical)) traversal[canonical] = new List<string>();
            }

            foreach (KeyValuePair<string, List<string>> pair in graph)
            {
                string from = Canonical(graph, pair.Key);
                if (from == null || pair.Value == null) continue;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    string to = Canonical(graph, pair.Value[i]);
                    if (to == null || from.Equals(to, StringComparison.OrdinalIgnoreCase)) continue;
                    AddUnique(traversal[from], to);
                    AddUnique(traversal[to], from);
                }
            }

            foreach (List<string> neighbors in traversal.Values)
                neighbors.Sort(StringComparer.OrdinalIgnoreCase);
            return traversal;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value)) return;
            for (int i = 0; i < values.Count; i++)
                if (values[i].Equals(value, StringComparison.OrdinalIgnoreCase)) return;
            values.Add(value);
        }

        private static string ResolveName(Dictionary<string, List<string>> graph, string requested, out bool ambiguous)
        {
            ambiguous = false;
            if (graph == null || string.IsNullOrWhiteSpace(requested)) return null;
            string query = requested.Trim();
            List<string> exact;
            if (graph.TryGetValue(query, out exact)) return Canonical(graph, query);

            string match = null;
            foreach (string name in graph.Keys)
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

        private static string Canonical(Dictionary<string, List<string>> graph, string value)
        {
            if (graph == null || string.IsNullOrWhiteSpace(value)) return null;
            string query = value.Trim();
            foreach (string key in graph.Keys)
                if (key.Equals(query, StringComparison.OrdinalIgnoreCase)) return key;
            return null;
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
