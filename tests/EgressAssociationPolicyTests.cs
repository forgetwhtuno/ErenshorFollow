using System;
using ErenshorFollow;

internal static class EgressAssociationPolicyTests
{
    private static int _passed;

    private static int Main()
    {
        // Native acceptance mirrors GameData.GetSafeNavMeshPoint: 2m sample, 0.25m vertical
        // tolerance, and NO fallback search. An off-mesh authored POI degrades native Sim travel
        // too, so the diagnostic must report that rather than quietly widening the radius.
        Run("a failed native sample is never accepted", FailedSampleRejected);
        Run("a sample inside the native vertical tolerance is accepted", InToleranceSampleAccepted);
        Run("a sample outside the native vertical tolerance is rejected", OutOfToleranceSampleRejected);
        Run("a non-finite vertical difference is rejected", NonFiniteVerticalRejected);

        // Association must be decided on distance to the real collider VOLUME. Raw transform
        // distance is diagnostic only: on a large oriented trigger the two differ by tens of metres.
        Run("candidate ordering prefers containment over proximity", ContainmentBeatsProximity);
        Run("candidate ordering uses volume distance, never raw centre distance", OrderingIgnoresRawDistance);
        Run("candidate ordering is deterministic for equal geometry", OrderingIsDeterministic);

        Run("no candidate crossings is reported as NONE", NoCandidatesIsNone);
        Run("a single candidate crossing is UNIQUE", SingleCandidateIsUnique);
        Run("a POI far from every crossing volume is NONE, not a distant match", FarPoiIsNone);
        Run("one containment among several candidates is UNIQUE", SingleContainmentIsUnique);
        Run("two containments are reported AMBIGUOUS rather than tiebroken", DoubleContainmentIsAmbiguous);
        Run("a clearly nearer crossing is UNIQUE", ClearNearestIsUnique);
        Run("two similarly near crossings are AMBIGUOUS", CloseRivalsAreAmbiguous);

        Run("association wording is stable and states the measured metrics", AssociationWordingIsStable);

        // The live comparison this pass exists for, expressed without any hardcoded coordinate.
        Run("an on-mesh authored POI on the trigger outranks a distant computed face", AuthoredBeatsDistantFace);
        Run("an off-mesh authored POI never claims to be more sensible", OffMeshAuthoredNeverWins);
        Run("an authored POI no closer than the computed approach does not win", NoImprovementDoesNotWin);

        Console.WriteLine("PASS: " + _passed + " egress association policy tests.");
        return 0;
    }

    private static void FailedSampleRejected()
    {
        Require(!EgressAssociationPolicy.NativeSampleAccepted(false, 0f),
            "a POI with no NavMesh within the native 2m sphere cannot be accepted");
    }

    private static void InToleranceSampleAccepted()
    {
        Require(EgressAssociationPolicy.NativeSampleAccepted(true, 0.2f), "0.2m vertical is inside native tolerance");
        Require(EgressAssociationPolicy.NativeSampleAccepted(true, -0.25f), "the tolerance is symmetric and inclusive");
    }

    private static void OutOfToleranceSampleRejected()
    {
        Require(!EgressAssociationPolicy.NativeSampleAccepted(true, 0.26f),
            "native returns the RAW point once the hit is beyond 0.25m vertically, so this is not a native-usable POI");
    }

    private static void NonFiniteVerticalRejected()
    {
        Require(!EgressAssociationPolicy.NativeSampleAccepted(true, float.NaN), "NaN is never acceptable");
        Require(!EgressAssociationPolicy.NativeSampleAccepted(true, float.PositiveInfinity), "infinity is never acceptable");
    }

    private static void ContainmentBeatsProximity()
    {
        // A POI standing INSIDE a trigger volume is unambiguous evidence of which exit it serves,
        // even when another trigger's surface happens to be marginally closer.
        Require(EgressAssociationPolicy.CompareCandidates(true, 0f, "a", false, 0.001f, "b") < 0,
            "containment must outrank a fractionally nearer surface");
    }

    private static void OrderingIgnoresRawDistance()
    {
        // CompareCandidates takes no raw-distance argument at all; this pins that fact by proving
        // the ordering is decided purely by the volume metric.
        Require(EgressAssociationPolicy.CompareCandidates(false, 2f, "far-centre", false, 30f, "near-centre") < 0,
            "the nearer collider volume wins regardless of where either trigger's centre sits");
    }

    private static void OrderingIsDeterministic()
    {
        Require(EgressAssociationPolicy.CompareCandidates(false, 5f, "a", false, 5f, "b") < 0,
            "equal geometry falls back to a stable key ordering");
        Require(EgressAssociationPolicy.CompareCandidates(false, 5f, "b", false, 5f, "a") > 0,
            "the stable ordering is antisymmetric");
    }

    private static void NoCandidatesIsNone()
    {
        Require(Classify(0, false, float.MaxValue, false, float.MaxValue) == EgressAssociationPolicy.AssociationKind.None,
            "a scene with no live Zoneline cannot associate a POI");
    }

    private static void SingleCandidateIsUnique()
    {
        Require(Classify(1, false, 3f, false, float.MaxValue) == EgressAssociationPolicy.AssociationKind.Unique,
            "one nearby crossing is an unambiguous association");
    }

    private static void FarPoiIsNone()
    {
        Require(Classify(3, false, 55f, false, 70f) == EgressAssociationPolicy.AssociationKind.None,
            "a POI 55m from every trigger volume is evidence of absence, not a distant match");
    }

    private static void SingleContainmentIsUnique()
    {
        Require(Classify(3, true, 0f, false, 12f) == EgressAssociationPolicy.AssociationKind.Unique,
            "exactly one containing trigger is a unique association");
    }

    private static void DoubleContainmentIsAmbiguous()
    {
        Require(Classify(2, true, 0f, true, 0f) == EgressAssociationPolicy.AssociationKind.Ambiguous,
            "two overlapping triggers containing one POI must be reported honestly, not tiebroken");
    }

    private static void ClearNearestIsUnique()
    {
        Require(Classify(2, false, 1.5f, false, 22f) == EgressAssociationPolicy.AssociationKind.Unique,
            "a crossing 20m nearer than its rival is a unique association");
    }

    private static void CloseRivalsAreAmbiguous()
    {
        Require(Classify(2, false, 6f, false, 8f) == EgressAssociationPolicy.AssociationKind.Ambiguous,
            "two crossings within the separation margin must be reported ambiguous");
    }

    private static void AssociationWordingIsStable()
    {
        string unique = EgressAssociationPolicy.DescribeAssociation(EgressAssociationPolicy.AssociationKind.Unique,
            "zoneline (1)#42->Vitheo", true, 0f, 41.30f);
        Require(unique.StartsWith("association=UNIQUE", StringComparison.Ordinal), "unique verdict is labelled");
        Require(unique.IndexOf("dVol=0.00", StringComparison.Ordinal) >= 0, "the volume metric is always reported");
        Require(unique.IndexOf("dRaw=41.30", StringComparison.Ordinal) >= 0,
            "the raw-centre metric is reported SEPARATELY so the two can never be confused");

        string none = EgressAssociationPolicy.DescribeAssociation(EgressAssociationPolicy.AssociationKind.None,
            null, false, float.MaxValue, float.MaxValue);
        Require(none == "association=NONE", "an absent association states exactly that");
    }

    private static void AuthoredBeatsDistantFace()
    {
        // Shape of the live 0.6.17 Vitheo failure, expressed only as metrics: the selected computed
        // approach was a far extreme face, while an authored POI sits on the trigger itself.
        Require(EgressAssociationPolicy.AuthoredTargetIsMoreSensible(true, 0.05f, 0f, 40.31f, 5f),
            "an on-mesh authored POI on the trigger volume is a materially more sensible target");
    }

    private static void OffMeshAuthoredNeverWins()
    {
        Require(!EgressAssociationPolicy.AuthoredTargetIsMoreSensible(false, float.NaN, 0f, 40.31f, 5f),
            "a POI with no native NavMesh sample cannot be recommended over anything");
        Require(!EgressAssociationPolicy.AuthoredTargetIsMoreSensible(true, 3f, 0f, 40.31f, 5f),
            "a POI outside the native vertical tolerance cannot be recommended over anything");
    }

    private static void NoImprovementDoesNotWin()
    {
        Require(!EgressAssociationPolicy.AuthoredTargetIsMoreSensible(true, 0f, 7f, 9f, 5f),
            "a marginal improvement is not evidence that the authored point is better");
    }

    private static EgressAssociationPolicy.AssociationKind Classify(int count, bool bestInside,
        float bestVolume, bool secondInside, float secondVolume)
    {
        return EgressAssociationPolicy.Classify(count, bestInside, bestVolume, secondInside, secondVolume,
            EgressAssociationPolicy.UniqueAssociationMargin, EgressAssociationPolicy.MaxAssociationDistance);
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
