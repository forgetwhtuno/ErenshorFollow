using System;
using System.Collections.Generic;
using ErenshorFollow;

internal static class RouteCandidatePolicyTests
{
    private static int _passed;

    public static int Main()
    {
        Run("multiple crossings: invalid then complete selects complete", MultipleCrossingsSelectComplete);
        Run("partial near crossing is accepted", PartialNearCrossingAccepted);
        Run("partial without meaningful progress is rejected", PartialNoProgressRejected);
        Run("all invalid produces no accepted route", AllInvalidFailsCleanly);
        Run("different partial destination names are ambiguous", DifferentNamesAreAmbiguous);
        Run("same destination on two Zonelines is not ambiguous", SameNameIsNotAmbiguous);
        Run("RemoveParty route is rejected", RemovePartyRejected);
        Run("ranking is deterministic regardless enumeration order", RankingDeterministic);
        Run("complete path far from the crossing is not proof of a valid crossing", CompleteFarFromCrossingRejected);
        Run("complete path close to the crossing is accepted", CompleteNearCrossingAccepted);
        Run("route failure message is generic only when no candidate was ever verified", RouteFailureMessageNoCandidates);
        Run("route failure message names the crossing when candidates existed but none worked", RouteFailureMessageWithCandidates);
        Run("blank reason does not produce empty parentheses", RouteFailureMessageBlankReason);
        Run("blank destination falls back to a neutral noun", RouteFailureMessageBlankDestination);
        Run("rejected crossings alone are not an accepted candidate", RejectedCandidatesAreNotAccepted);
        Run("an accepted crossing is an accepted candidate", AcceptedCandidateIsDetected);
        Run("non-crossing execution failure does not claim a crossing", TravelExecutionFailureWording);
        Run("travel execution failure with blank reason has no empty parentheses", TravelExecutionBlankReason);
        Run("crossing transition failure distinguishes trigger handoff from route approach", CrossingTransitionFailureWording);
        Run("no accepted route overrides any site classification", NoAcceptedRouteOverridesSiteKind);
        Run("an unclassified site with an accepted route degrades to travel execution", UnclassifiedSiteDegrades);
        Console.WriteLine("PASS: " + _passed + " route-candidate policy tests.");
        return 0;
    }

    private static void MultipleCrossingsSelectComplete()
    {
        RouteCandidatePolicy.Candidate first = Candidate("crossing-1", false, RouteCandidatePolicy.PathKind.Invalid, 0, 20f, 20f, 12f, 999f);
        RouteCandidatePolicy.Candidate second = Candidate("crossing-2", true, RouteCandidatePolicy.PathKind.Complete, 4, 18f, 0f, 1f, 22f);
        List<RouteCandidatePolicy.Evaluation> ranked = RouteCandidatePolicy.RankAccepted(new List<RouteCandidatePolicy.Candidate> { first, second });
        Require(ranked.Count == 1, "expected only the complete crossing to be accepted");
        Require(ranked[0].Candidate.StableKey == "crossing-2", "candidate 2 should be selected");
    }

    private static void PartialNearCrossingAccepted()
    {
        RouteCandidatePolicy.Candidate partial = Candidate("partial-near", true, RouteCandidatePolicy.PathKind.Partial, 5, 20f, 3.5f, 1.5f, 18f);
        RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(partial);
        Require(evaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.PartialNearCrossing, evaluation.Reason);
    }

    private static void PartialNoProgressRejected()
    {
        RouteCandidatePolicy.Candidate partial = Candidate("partial-stall", true, RouteCandidatePolicy.PathKind.Partial, 3, 6f, 5.5f, 2f, 1f);
        RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(partial);
        Require(!evaluation.Accepted, "partial route with only 0.5m progress must be rejected");
        Require(evaluation.Reason.IndexOf("meaningful progress", StringComparison.OrdinalIgnoreCase) >= 0, evaluation.Reason);
    }

    private static void AllInvalidFailsCleanly()
    {
        List<RouteCandidatePolicy.Candidate> candidates = new List<RouteCandidatePolicy.Candidate>
        {
            Candidate("a", false, RouteCandidatePolicy.PathKind.Invalid, 0, 20f, 20f, 20f, 999f),
            Candidate("b", false, RouteCandidatePolicy.PathKind.Invalid, 0, 10f, 10f, 10f, 999f)
        };
        Require(RouteCandidatePolicy.RankAccepted(candidates).Count == 0, "all invalid candidates should yield no route");
    }

    private static void DifferentNamesAreAmbiguous()
    {
        List<RouteCandidatePolicy.DestinationNameCandidate> names = new List<RouteCandidatePolicy.DestinationNameCandidate>
        {
            new RouteCandidatePolicy.DestinationNameCandidate("Vitheo's Watch", true, false),
            new RouteCandidatePolicy.DestinationNameCandidate("Vitheo's Woods", true, false)
        };
        bool ambiguous;
        bool removingOnly;
        string resolved = RouteCandidatePolicy.ResolveCanonicalName(names, "Vitheo", out ambiguous, out removingOnly);
        Require(resolved == null && ambiguous, "two different names should be ambiguous");
    }

    private static void SameNameIsNotAmbiguous()
    {
        List<RouteCandidatePolicy.DestinationNameCandidate> names = new List<RouteCandidatePolicy.DestinationNameCandidate>
        {
            new RouteCandidatePolicy.DestinationNameCandidate("Vitheo's Watch", true, false),
            new RouteCandidatePolicy.DestinationNameCandidate("Vitheo's Watch", true, false)
        };
        bool ambiguous;
        bool removingOnly;
        string resolved = RouteCandidatePolicy.ResolveCanonicalName(names, "Watch", out ambiguous, out removingOnly);
        Require(resolved == "Vitheo's Watch", "duplicate crossings should resolve to their one canonical name");
        Require(!ambiguous, "duplicate same-destination Zonelines are not ambiguous");
    }

    private static void RemovePartyRejected()
    {
        RouteCandidatePolicy.Candidate candidate = Candidate("remove-party", true, RouteCandidatePolicy.PathKind.Complete, 4, 8f, 0f, 0f, 8f);
        candidate.RemoveParty = true;
        Require(!RouteCandidatePolicy.Evaluate(candidate).Accepted, "RemoveParty crossing must be rejected regardless of path quality");

        List<RouteCandidatePolicy.DestinationNameCandidate> names = new List<RouteCandidatePolicy.DestinationNameCandidate>
        {
            new RouteCandidatePolicy.DestinationNameCandidate("Forbidden Place", true, true)
        };
        bool ambiguous;
        bool removingOnly;
        Require(RouteCandidatePolicy.ResolveCanonicalName(names, "Forbidden", out ambiguous, out removingOnly) == null && removingOnly,
            "name resolver should report a removing-only destination");
    }

    private static void RankingDeterministic()
    {
        RouteCandidatePolicy.Candidate a = Candidate("a", true, RouteCandidatePolicy.PathKind.Complete, 4, 20f, 0f, 1f, 12f);
        RouteCandidatePolicy.Candidate b = Candidate("b", true, RouteCandidatePolicy.PathKind.Complete, 4, 20f, 0f, 1f, 16f);
        RouteCandidatePolicy.Candidate c = Candidate("c", true, RouteCandidatePolicy.PathKind.Partial, 4, 20f, 3f, 2f, 10f);
        List<RouteCandidatePolicy.Evaluation> forward = RouteCandidatePolicy.RankAccepted(new List<RouteCandidatePolicy.Candidate> { a, b, c });
        List<RouteCandidatePolicy.Evaluation> reverse = RouteCandidatePolicy.RankAccepted(new List<RouteCandidatePolicy.Candidate> { c, b, a });
        Require(forward.Count == reverse.Count, "ranked counts differ");
        for (int i = 0; i < forward.Count; i++)
            Require(forward[i].Candidate.StableKey == reverse[i].Candidate.StableKey, "rank order changed at index " + i);
        Require(forward[0].Candidate.StableKey == "a", "shorter complete route should rank first");
        Require(forward[2].Candidate.StableKey == "c", "partial route should rank behind complete routes");
    }

    // Reproduces the verified Brake -> Azure field case: NavMesh.CalculatePath reports Complete all the way
    // to a sampled approach point, but that approach is ~42m from the real crossing. A Complete path is
    // necessary but not sufficient proof of a valid crossing -- it must also land near the crossing.
    private static void CompleteFarFromCrossingRejected()
    {
        RouteCandidatePolicy.Candidate candidate = Candidate("azure-bad-approach", true, RouteCandidatePolicy.PathKind.Complete,
            6, 42f, 0f, 41.9f, 45f);
        RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
        Require(!evaluation.Accepted, "a Complete path ending 41.9m from the crossing must not be accepted");
        Require(evaluation.Reason.IndexOf("too far", StringComparison.OrdinalIgnoreCase) >= 0, evaluation.Reason);
    }

    private static void CompleteNearCrossingAccepted()
    {
        RouteCandidatePolicy.Candidate candidate = Candidate("azure-good-approach", true, RouteCandidatePolicy.PathKind.Complete,
            4, 42f, 0f, 5.3f, 40f);
        RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
        Require(evaluation.Accepted && evaluation.Acceptance == RouteCandidatePolicy.AcceptanceKind.Complete,
            "a Complete path ending 5.3m from the crossing must be accepted");
    }

    private static void RouteFailureMessageNoCandidates()
    {
        string message = RouteCandidatePolicy.DescribeRouteFailure("Azure", RouteCandidatePolicy.RouteFailureKind.NoAcceptedRoute, "no walkable route to Azure.");
        Require(message == "no walkable route to Azure.", "no verified candidate ever existed: " + message);
    }

    private static void RouteFailureMessageWithCandidates()
    {
        string message = RouteCandidatePolicy.DescribeRouteFailure("Azure", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed, "the boundary approach did not produce a real zone transition");
        Require(message.IndexOf("could not reach a valid crossing approach to Azure", StringComparison.OrdinalIgnoreCase) >= 0,
            "message should name the crossing instead of a generic no-route phrase: " + message);
        Require(message.IndexOf("no walkable route", StringComparison.OrdinalIgnoreCase) < 0,
            "message must not fall back to the generic no-route phrasing when a candidate was verified: " + message);
    }

    private static void RouteFailureMessageBlankReason()
    {
        string message = RouteCandidatePolicy.DescribeRouteFailure("Azure", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed, "   ");
        Require(message.IndexOf("()", StringComparison.Ordinal) < 0, "blank reason must not emit empty parentheses: " + message);
        Require(message == "could not reach a valid crossing approach to Azure.", message);
    }

    private static void RouteFailureMessageBlankDestination()
    {
        Require(RouteCandidatePolicy.DescribeRouteFailure(null, RouteCandidatePolicy.RouteFailureKind.NoAcceptedRoute, null) == "no walkable route to the destination.",
            "null destination should fall back to a neutral noun");
        Require(RouteCandidatePolicy.DescribeRouteFailure("  ", RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed, null) == "could not reach a valid crossing approach to the destination.",
            "blank destination should fall back to a neutral noun");
    }

    // "An accepted candidate existed" must mean acceptance policy passed -- not that a Zoneline or a raw
    // sampled point was found. RankAccepted is the same surface the planner uses to build leg options, so
    // an empty result is exactly the "no walkable route" case.
    private static void RejectedCandidatesAreNotAccepted()
    {
        RouteCandidatePolicy.Candidate unsampled = Candidate("raw-crossing", false, RouteCandidatePolicy.PathKind.Invalid, 0, 30f, 30f, 30f, 999f);
        RouteCandidatePolicy.Candidate farApproach = Candidate("far-approach", true, RouteCandidatePolicy.PathKind.Complete, 6, 42f, 0f, 41.9f, 45f);
        List<RouteCandidatePolicy.Evaluation> accepted =
            RouteCandidatePolicy.RankAccepted(new List<RouteCandidatePolicy.Candidate> { unsampled, farApproach });
        Require(accepted.Count == 0, "rejected crossings must not count as verified approaches");
        Require(RouteCandidatePolicy.DescribeRouteFailure("Azure", RouteCandidatePolicy.ResolveFailureKind(accepted.Count > 0, RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed), "route validation stopped making useful progress")
            == "no walkable route to Azure.", "no accepted candidate must keep the generic wording");
    }

    private static void AcceptedCandidateIsDetected()
    {
        RouteCandidatePolicy.Candidate good = Candidate("good-approach", true, RouteCandidatePolicy.PathKind.Complete, 4, 20f, 0f, 1f, 22f);
        List<RouteCandidatePolicy.Evaluation> accepted =
            RouteCandidatePolicy.RankAccepted(new List<RouteCandidatePolicy.Candidate> { good });
        Require(accepted.Count == 1, "an accepted approach should be counted");
        Require(RouteCandidatePolicy.DescribeRouteFailure("Azure", RouteCandidatePolicy.ResolveFailureKind(accepted.Count > 0, RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed), "the boundary approach did not produce a real zone transition")
            .IndexOf("could not reach a valid crossing approach to Azure", StringComparison.OrdinalIgnoreCase) >= 0,
            "an exhausted accepted candidate must use the crossing-approach wording");
    }

    // FailRoute is a shared funnel: regrouping, player-follow loss, and native path invalidation all arrive
    // there with an accepted route in hand. Those must not claim the crossing could not be reached.
    private static void TravelExecutionFailureWording()
    {
        string message = RouteCandidatePolicy.DescribeRouteFailure("Azure",
            RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed, "could not hold the leader for regrouping");
        Require(message == "travel to Azure failed (could not hold the leader for regrouping).", message);
        Require(message.IndexOf("crossing", StringComparison.OrdinalIgnoreCase) < 0,
            "a non-crossing failure must not mention a crossing: " + message);
        Require(message.IndexOf("no walkable route", StringComparison.OrdinalIgnoreCase) < 0,
            "an accepted route must not report as undiscoverable: " + message);
    }

    private static void TravelExecutionBlankReason()
    {
        string message = RouteCandidatePolicy.DescribeRouteFailure("Azure",
            RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed, null);
        Require(message == "travel to Azure failed.", message);
        Require(message.IndexOf("()", StringComparison.Ordinal) < 0, "blank reason must not emit empty parentheses: " + message);
    }

    private static void CrossingTransitionFailureWording()
    {
        string message = RouteCandidatePolicy.DescribeRouteFailure("Bonepits",
            RouteCandidatePolicy.RouteFailureKind.CrossingTransitionFailed,
            "leader entered the approach but the native trigger did not fire");
        Require(message.IndexOf("native crossing to Bonepits did not complete", StringComparison.OrdinalIgnoreCase) >= 0, message);
        Require(message.IndexOf("no walkable route", StringComparison.OrdinalIgnoreCase) < 0, message);
        Require(message.IndexOf("valid crossing approach", StringComparison.OrdinalIgnoreCase) < 0, message);
    }

    private static void NoAcceptedRouteOverridesSiteKind()
    {
        Require(RouteCandidatePolicy.ResolveFailureKind(false, RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed)
            == RouteCandidatePolicy.RouteFailureKind.NoAcceptedRoute,
            "without an accepted route a crossing claim must not survive");
        Require(RouteCandidatePolicy.ResolveFailureKind(false, RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed)
            == RouteCandidatePolicy.RouteFailureKind.NoAcceptedRoute,
            "without an accepted route a travel claim must not survive");
    }

    private static void UnclassifiedSiteDegrades()
    {
        Require(RouteCandidatePolicy.ResolveFailureKind(true, RouteCandidatePolicy.RouteFailureKind.NoAcceptedRoute)
            == RouteCandidatePolicy.RouteFailureKind.TravelExecutionFailed,
            "an accepted route with no specific site claim should default to travel execution");
        Require(RouteCandidatePolicy.ResolveFailureKind(true, RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed)
            == RouteCandidatePolicy.RouteFailureKind.CrossingApproachFailed,
            "an explicit crossing site claim should be preserved");
    }

    private static RouteCandidatePolicy.Candidate Candidate(string key, bool sampled, RouteCandidatePolicy.PathKind path,
        int corners, float startDistance, float endpointDistance, float approachDistance, float routeLength)
    {
        return new RouteCandidatePolicy.Candidate
        {
            StableKey = key,
            Active = true,
            RemoveParty = false,
            Sampled = sampled,
            Path = path,
            CornerCount = corners,
            StartDistanceToCrossing = startDistance,
            EndpointDistanceToCrossing = endpointDistance,
            ApproachDistanceToCrossing = approachDistance,
            RouteLength = routeLength
        };
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
