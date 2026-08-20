using System;
using System.Collections.Generic;

namespace ErenshorFollow
{
    // ZoneAtlas supplies only a game-authored candidate itinerary. Every current hop still has to resolve
    // to a live, active, non-party-removing Zoneline before movement begins; this class never moves or zones.
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
            return ZoneRouteGraphPolicy.TryBuild(ReadGraph(), origin, requested, allowedFirstHops,
                out route, out ambiguous, out failure);
        }

        // Setup-window surface: only destinations whose atlas route starts through one of the currently
        // verified live Zoneline names are advertised. Future hops remain candidates and are re-proven live
        // after each real native zone transition.
        internal static List<ExpeditionRouteChoice> ListReachableRoutes(string origin, IList<string> liveFirstHops)
        {
            return ZoneRouteGraphPolicy.ListReachable(ReadGraph(), origin, liveFirstHops);
        }

        internal static List<string> ListZoneNames()
        {
            List<string> names = new List<string>(ReadGraph().Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        internal static string DescribeDiscovery(string origin, IList<string> liveFirstHops)
        {
            Dictionary<string, List<string>> graph = ReadGraph();
            int authoredLinks = 0;
            foreach (List<string> links in graph.Values) if (links != null) authoredLinks += links.Count;
            Dictionary<string, List<string>> traversal = ZoneRouteGraphPolicy.BuildTraversalGraph(graph);
            Dictionary<string, List<string>> runtime = ZoneRouteGraphPolicy.BuildRuntimeGraph(graph, origin, liveFirstHops);
            int normalizedLinks = 0;
            foreach (List<string> links in traversal.Values) if (links != null) normalizedLinks += links.Count;
            List<ExpeditionRouteChoice> reachable = ZoneRouteGraphPolicy.ListReachable(graph, origin, liveFirstHops);
            List<string> names = new List<string>();
            for (int i = 0; i < reachable.Count && i < 8; i++)
                names.Add(reachable[i].DestinationName + (reachable[i].Nearby ? "(near)" : "(" + reachable[i].TransitionCount + " hops)"));
            string text = "[Expedition route reconcile] scene=" + Safe(origin) +
                " nodes=" + graph.Count +
                " authoredLinks=" + authoredLinks +
                " normalizedEdges=" + (normalizedLinks / 2) +
                " liveFirstHops=" + Join(liveFirstHops) +
                " runtimeOutgoing=" + Join(Outgoing(runtime, origin)) +
                " reachable=" + reachable.Count +
                (names.Count == 0 ? string.Empty : " | " + string.Join(", ", names.ToArray()));
            if (liveFirstHops != null)
            {
                for (int i = 0; i < liveFirstHops.Count; i++)
                {
                    string hop = liveFirstHops[i];
                    if (string.IsNullOrWhiteSpace(hop)) continue;
                    bool authored = ContainsName(Outgoing(graph, origin), hop);
                    bool normalized = ContainsName(Outgoing(traversal, origin), hop);
                    bool runtimeEdge = ContainsName(Outgoing(runtime, origin), hop);
                    List<string> directRoute;
                    bool ambiguous;
                    string failure;
                    bool reachableDirect = TryBuild(origin, hop, liveFirstHops, out directRoute, out ambiguous, out failure) && directRoute.Count == 2;
                    text += " | liveHop=" + Safe(hop) + " canonical=" + Safe(Canonical(runtime, hop)) +
                        " liveCrossings=" + ExpeditionDestinationResolver.GetCrossings(hop, false).Count +
                        " authoredEdge=" + authored + " normalizedEdge=" + normalized +
                        " runtimeEdge=" + runtimeEdge + " reachable=" + reachableDirect +
                        " direct=" + reachableDirect + " selected=unavailable egressPOIs=optional eligibility=" +
                        (runtimeEdge ? "eligible" : "live_crossing_ineligible");
                }
            }
            return text;
        }

        private static List<string> Outgoing(Dictionary<string, List<string>> graph, string origin)
        {
            List<string> empty = new List<string>();
            string canonical = Canonical(graph, origin);
            if (canonical == null || graph == null) return empty;
            List<string> edges;
            return graph.TryGetValue(canonical, out edges) && edges != null ? edges : empty;
        }

        private static string Join(IList<string> values)
        {
            if (values == null || values.Count == 0) return "none";
            List<string> clean = new List<string>();
            for (int i = 0; i < values.Count; i++)
                if (!string.IsNullOrWhiteSpace(values[i])) clean.Add(values[i].Trim());
            return clean.Count == 0 ? "none" : string.Join(",", clean.ToArray());
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace("\n", " ").Replace("\r", " ").Trim();
        }

        private static Dictionary<string, List<string>> ReadGraph()
        {
            Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ZoneAtlasEntry[] atlas = ZoneAtlas.Atlas;
                if (atlas == null) return graph;

                // First collect canonical names so stale/unknown neighbor strings cannot create invented nodes.
                for (int i = 0; i < atlas.Length; i++)
                {
                    ZoneAtlasEntry entry = atlas[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ZoneName)) continue;
                    string name = entry.ZoneName.Trim();
                    if (!graph.ContainsKey(name)) graph.Add(name, new List<string>());
                }

                for (int i = 0; i < atlas.Length; i++)
                {
                    ZoneAtlasEntry entry = atlas[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ZoneName) || entry.NeighboringZones == null) continue;
                    string name = Canonical(graph, entry.ZoneName);
                    if (name == null) continue;
                    List<string> neighbors = graph[name];
                    for (int n = 0; n < entry.NeighboringZones.Count; n++)
                    {
                        string neighbor = Canonical(graph, entry.NeighboringZones[n]);
                        if (neighbor == null || ContainsName(neighbors, neighbor)) continue;
                        neighbors.Add(neighbor);
                    }
                    neighbors.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return graph;
        }

        private static string Canonical(Dictionary<string, List<string>> graph, string value)
        {
            if (graph == null || string.IsNullOrWhiteSpace(value)) return null;
            string query = value.Trim();
            foreach (string key in graph.Keys)
                if (key.Equals(query, StringComparison.OrdinalIgnoreCase)) return key;
            return null;
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
