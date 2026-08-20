using System;
using System.Collections.Generic;

namespace ErenshorFollow
{
    internal static class ExpeditionRouteGraphTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static Dictionary<string, List<string>> Graph()
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Hidden Hills", new List<string> { "Stowaway's Step", "The Blight" } },
                { "Stowaway's Step", new List<string> { "Hidden Hills", "Port Azure" } },
                { "The Blight", new List<string> { "Hidden Hills", "Deep Marsh" } },
                { "Deep Marsh", new List<string> { "The Blight", "Port Azure" } },
                { "Port Azure", new List<string> { "Stowaway's Step", "Deep Marsh", "Far Harbor" } },
                { "Far Harbor", new List<string> { "Port Azure" } },
                { "Isolated", new List<string>() }
            };
        }

        private static void AdjacentRoute()
        {
            List<string> route; bool ambiguous; string failure;
            bool ok = ZoneRouteGraphPolicy.TryBuild(Graph(), "Hidden Hills", "Stowaway's Step",
                new [] { "Stowaway's Step", "The Blight" }, out route, out ambiguous, out failure);
            Assert(ok && !ambiguous && route.Count == 2, "adjacent atlas route accepted");
            Assert(route[0] == "Hidden Hills" && route[1] == "Stowaway's Step", "adjacent preview preserves canonical names");
        }

        private static void MultiHopRoute()
        {
            List<string> route; bool ambiguous; string failure;
            bool ok = ZoneRouteGraphPolicy.TryBuild(Graph(), "Hidden Hills", "Port Azure",
                new [] { "Stowaway's Step" }, out route, out ambiguous, out failure);
            Assert(ok && route.Count == 3, "multi-hop route found");
            Assert(route[0] == "Hidden Hills" && route[1] == "Stowaway's Step" && route[2] == "Port Azure",
                "multi-hop route preview is immediate and ordered");
        }

        private static void ReachableListAndUnreachableOmission()
        {
            List<ExpeditionRouteChoice> choices = ZoneRouteGraphPolicy.ListReachable(Graph(), "Hidden Hills",
                new [] { "Stowaway's Step", "The Blight" });
            bool adjacent = false, distant = false, isolated = false;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].DestinationName == "Stowaway's Step") adjacent = choices[i].Nearby;
                if (choices[i].DestinationName == "Far Harbor") distant = choices[i].TransitionCount >= 3;
                if (choices[i].DestinationName == "Isolated") isolated = true;
            }
            Assert(adjacent, "reachable destination list marks live adjacent destination Nearby");
            Assert(distant, "reachable destination list includes multi-zone destination");
            Assert(!isolated, "unreachable destination omitted");
        }

        private static void LiveFirstHopAuthorityConstrainsCandidate()
        {
            List<string> route; bool ambiguous; string failure;
            bool ok = ZoneRouteGraphPolicy.TryBuild(Graph(), "Hidden Hills", "Deep Marsh",
                new [] { "Stowaway's Step" }, out route, out ambiguous, out failure);
            Assert(ok, "route planner can seek alternate route when direct atlas edge is not live");
            Assert(route.Count >= 4 && route[1] == "Stowaway's Step",
                "candidate itinerary begins only through an allowed live first hop");
        }

        private static void NoLiveFirstHopMeansNoStartableRoute()
        {
            List<string> route; bool ambiguous; string failure;
            bool ok = ZoneRouteGraphPolicy.TryBuild(Graph(), "Hidden Hills", "Port Azure",
                new string[0], out route, out ambiguous, out failure);
            Assert(!ok && route.Count == 0, "no verified live first hop produces no startable route");
        }

        private static void RecalculationCanChooseNewShorterRoute()
        {
            Dictionary<string, List<string>> graph = Graph();
            List<string> initial; bool ambiguous; string failure;
            Assert(ZoneRouteGraphPolicy.TryBuild(graph, "Hidden Hills", "Far Harbor",
                new [] { "Stowaway's Step" }, out initial, out ambiguous, out failure), "initial route exists");
            Assert(initial.Count == 4, "initial route has three transitions");

            // On the far side the coordinator replans from the actual current scene rather than indexing a stale route.
            List<string> replanned;
            Assert(ZoneRouteGraphPolicy.TryBuild(graph, "Port Azure", "Far Harbor",
                new [] { "Far Harbor" }, out replanned, out ambiguous, out failure), "post-zone route recalculates");
            Assert(replanned.Count == 2 && replanned[1] == "Far Harbor", "recalculation adopts shorter valid route");
        }


        private static void AsymmetricAtlasLinksRemainReachable()
        {
            Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Hidden Hills", new List<string> { "Bonepits" } },
                { "Bonepits", new List<string>() },
                // The far-side atlas entry is the only asset that records this adjacency.
                { "Stowaway's Step", new List<string> { "Bonepits", "Port Azure" } },
                { "Port Azure", new List<string>() }
            };

            List<string> route; bool ambiguous; string failure;
            bool ok = ZoneRouteGraphPolicy.TryBuild(graph, "Hidden Hills", "Port Azure",
                new [] { "Bonepits" }, out route, out ambiguous, out failure);
            Assert(ok && !ambiguous, "one-sided atlas adjacency remains traversable");
            Assert(route.Count == 4 && route[0] == "Hidden Hills" && route[1] == "Bonepits" &&
                route[2] == "Stowaway's Step" && route[3] == "Port Azure",
                "asymmetric route still begins through the verified live first hop");

            List<ExpeditionRouteChoice> choices = ZoneRouteGraphPolicy.ListReachable(graph, "Hidden Hills", new [] { "Bonepits" });
            bool found = false;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i].DestinationName == "Port Azure") found = choices[i].TransitionCount == 3 && !choices[i].Nearby;
            Assert(found, "multi-hop picker includes destinations whose atlas link is authored on the reverse endpoint");
        }

        private static void DeterministicOrganization()
        {
            List<ExpeditionRouteChoice> choices = ZoneRouteGraphPolicy.ListReachable(Graph(), "Hidden Hills",
                new [] { "Stowaway's Step", "The Blight" });
            Assert(choices.Count > 3, "destination list populated");
            Assert(choices[0].Nearby && choices[1].Nearby, "Nearby destinations sort before other reachable zones");
            for (int i = 1; i < choices.Count; i++)
            {
                if (!choices[i - 1].Nearby && choices[i].Nearby)
                    throw new Exception("FAILED: Nearby ordering regressed");
            }
            _passed++;
            Console.WriteLine("PASS: destination organization remains stable");
        }

        private static void DirectLiveEdgeAbsentFromAtlas()
        {
            Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            { { "Duskenlight", new List<string> { "Hidden" } }, { "Hidden", new List<string>() }, { "Jaws", new List<string>() } };
            List<string> route; bool ambiguous; string failure;
            Assert(ZoneRouteGraphPolicy.TryBuild(graph, "Duskenlight", "Jaws", new [] { "Hidden", "Jaws", "Windwashed" }, out route, out ambiguous, out failure),
                "direct live edge absent from authored graph is route-valid");
            Assert(route.Count == 2 && route[1] == "Jaws", "direct live Jaws-style edge becomes selected first hop");
        }

        private static void LiveDuplicateAndOptionalPoiDoNotGate()
        {
            Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            { { "Duskenlight", new List<string> { "Hidden" } }, { "Hidden", new List<string>() }, { "Jaws", new List<string>() }, { "Windwashed", new List<string>() } };
            Dictionary<string, List<string>> runtime = ZoneRouteGraphPolicy.BuildRuntimeGraph(graph, "Duskenlight",
                new [] { "hidden", "Jaws", "Windwashed" });
            Assert(runtime["Duskenlight"].Count == 3, "authored and live duplicate produce one canonical runtime edge");
            List<ExpeditionRouteChoice> choices = ZoneRouteGraphPolicy.ListReachable(graph, "Duskenlight", new [] { "Hidden", "Jaws", "Windwashed" });
            bool hidden = false, jaws = false, windwashed = false;
            for (int i = 0; i < choices.Count; i++) { if (choices[i].DestinationName == "Hidden") hidden = true; if (choices[i].DestinationName == "Jaws") jaws = true; if (choices[i].DestinationName == "Windwashed") windwashed = true; }
            Assert(hidden && jaws && windwashed, "multiple live exits remain valid without optional egress metadata");
        }

        private static void LiveEdgeEnablesMultiHopAndRejectsSelf()
        {
            Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            { { "A", new List<string>() }, { "B", new List<string> { "C" } }, { "C", new List<string> { "D" } }, { "D", new List<string>() } };
            List<string> route; bool ambiguous; string failure;
            Assert(ZoneRouteGraphPolicy.TryBuild(graph, "A", "D", new [] { "A", "B" }, out route, out ambiguous, out failure),
                "current live edge enables authored multi-hop route");
            Assert(route.Count == 4 && route[1] == "B", "multi-hop route starts through executable live first hop");
            Dictionary<string, List<string>> runtime = ZoneRouteGraphPolicy.BuildRuntimeGraph(graph, "A", new [] { "A" });
            Assert(runtime["A"].Count == 0, "self live edge does not become route progress");
        }

        private static void IneligibleOrMissingLiveHopFailsGracefully()
        {
            Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            { { "A", new List<string> { "X" } }, { "X", new List<string>() }, { "B", new List<string>() } };
            List<string> route; bool ambiguous; string failure;
            Assert(!ZoneRouteGraphPolicy.TryBuild(graph, "A", "B", new string[0], out route, out ambiguous, out failure) && route.Count == 0,
                "no authored or eligible live edge remains a graceful no-route result");
        }

        public static int Main()
        {
            AdjacentRoute();
            MultiHopRoute();
            ReachableListAndUnreachableOmission();
            LiveFirstHopAuthorityConstrainsCandidate();
            NoLiveFirstHopMeansNoStartableRoute();
            RecalculationCanChooseNewShorterRoute();
            AsymmetricAtlasLinksRemainReachable();
            DeterministicOrganization();
            DirectLiveEdgeAbsentFromAtlas();
            LiveDuplicateAndOptionalPoiDoNotGate();
            LiveEdgeEnablesMultiHopAndRejectsSelf();
            IneligibleOrMissingLiveHopFailsGracefully();
            Console.WriteLine("Expedition route graph tests passed: " + _passed);
            return 0;
        }
    }
}
