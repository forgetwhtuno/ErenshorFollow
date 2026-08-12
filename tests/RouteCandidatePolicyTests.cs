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
