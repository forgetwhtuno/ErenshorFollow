using System;
using System.Collections.Generic;
using ErenshorFollow;

internal static class CrossingSeedGeometryPolicyTests
{
    private static int _passed;

    private static int Main()
    {
        Run("floor seeds sit at the bounds' minimum Y, not its center", FloorSeedsUseMinimumY);
        Run("floor seeds span the horizontal footprint of the bounds", FloorSeedsSpanFootprint);
        Run("a short/flat collider does not need floor seeding", ShortColliderSkipsFloorSeeding);
        Run("a tall collider needs floor seeding", TallColliderNeedsFloorSeeding);
        // 0.6.6 live repro fixture: a Duskenlight-shaped crossing whose transform/collider-center seed
        // sits far above real ground, while the collider's own floor face is right at ground level.
        // Proves the concrete failure mode (transform-only seeding finds nothing; the floor seed does)
        // without hardcoding the real Hidden coordinate anywhere in source.
        Run("transform-only seeding misses ground a tall trigger's floor seed reaches", TransformSeedMissesFloorSeedReachesGround);
        // 0.6.10 Hidden -> Duskenlight: 14 seeds, only 2 sampled, both ~40m from the verified
        // crossing and correctly rejected. These cover the oriented/vertical seeding added for it.
        Run("oriented face offsets cover the four horizontal faces and the floor", OrientedFacesCoverEntrances);
        Run("vertical probe spans the trigger's own height range and is bounded", VerticalProbeSpansAndIsBounded);
        Run("a 40m-away seed is not worth sampling and cannot consume budget", FarSeedIsFilteredOut);
        Run("a seed that could still produce an accepted approach is always kept", NearSeedIsAlwaysKept);
        Run("rotated/scaled colliders keep oriented offsets in normalized local space", OrientedOffsetsAreNormalized);
        // 0.6.12 Duskenlight -> Hidden large-trigger regression: BoxCollider
        // center=(223.37, 61.40, 117.58) size=(67.50, 47.11, 59.35) produced generatedSeeds=16,
        // samples=0 because the 0.6.10 proximity filter measured distance to the crossing's RAW
        // CENTRE. Half of the trigger's own surface lies >12m from that centre.
        Run("a seed inside a large trigger is retained even far from its centre", InsideLargeTriggerIsRetained);
        Run("a seed near a large trigger's surface is measured to the volume, not the centre", SurfaceSeedMeasuredToVolume);
        Run("a genuinely distant seed outside the volume is still filtered", DistantSeedStillFiltered);
        Run("a large tall trigger keeps its floor and vertical candidates", LargeTallTriggerKeepsFloorAndVerticalSeeds);
        Run("small-collider filtering is unchanged by volume measurement", SmallColliderBehaviorUnchanged);
        Run("rotated/scaled trigger volume distance honours its own basis", RotatedScaledVolumeDistanceHonoursBasis);
        Run("the live 0.6.11 Hidden seed set is restored by volume measurement", LiveHiddenRegressionIsRepaired);
        // 0.6.13 live 0.6.12 proof: all retained Hidden seeds still sampled false. The formerly
        // successful approach sits in the vertical blind band between the centre and floor layers.
        Run("a tall trigger gains a bounded intermediate vertical layer", TallTriggerAddsIntermediateLayer);
        Run("a short trigger does not gain redundant intermediate layers", ShortTriggerSkipsIntermediateLayer);
        Run("intermediate interior offsets stay bounded inside the trigger", IntermediateOffsetsStayBounded);
        Run("an axis-aligned Hidden diagnostic fixture places a lower-mid seed near the old approach", AxisAlignedHiddenIntermediateSeedReachesKnownApproach);
        Run("approach-face lower-mid offsets choose the route-facing normalized box axis", ApproachFaceUsesNormalizedRouteDirection);
        Run("approach-face lower-mid offsets stay bounded to three surface samples", ApproachFaceOffsetsStayBounded);
        // 0.6.14 live 0.6.13 proof: authoritative Hidden OBB is localSize=(1,1,1),
        // lossyScale=(80,47.11,10), rotationY~=40.27. The world-scale intermediate repair must
        // generate the route-facing lower-mid band before any fallback policy is considered.
        Run("large zero-sample trigger gets a bounded world-metre inner ring", LargeTriggerGetsBoundedFallbackRing);
        Run("small trigger skips the redundant fallback ring", SmallTriggerSkipsFallbackRing);
        Run("live rotated Hidden OBB intermediate band reaches the formerly working approach", LiveRotatedHiddenIntermediateBandReachesKnownApproach);
        // 0.6.18 live 0.6.17 proof: vitheo|zoneline (1) produced generatedSeeds=22, samples=1,
        // accepted=1, selectedSeed=face1, qualityRef=routeFaceZ+, qualityDist=40.31. Ranking had no
        // alternative because the second-stage entrance band could only place three centre-height
        // points on one depth. These pin the repaired bounded route-facing probe set.
        Run("route-facing probes and the quality reference name the same face", RouteFacingProbesCoverTheQualityReferenceFace);
        Run("a bounded route-facing probe outranks the lone extreme face on the live shape class", LiveShapeClassRouteFacingProbeOutranksExtremeFace);
        Run("a lone extreme face stays legal when every route-facing probe fails", ExtremeFaceRemainsLegalWhenEveryProbeFails);
        Run("small triggers never enter second-stage route-facing probing", SmallTriggerSkipsRouteFacingProbing);
        Run("tall narrow triggers keep three tangent steps and both levels", TallNarrowTriggerKeepsThreeTangentSteps);
        Run("short triggers keep the centre level only", ShortTriggerKeepsCentreLevelOnly);
        Run("rotated triggers keep every probe strictly inside the oriented box", RotatedTriggerProbesStayInsideNormalizedBox);
        Run("production primary budget leaves all eight fallback slots", PrimaryBudgetReservesFallbackSlots);
        Run("zero-sample fallback can add every midRing label", ZeroSampleFallbackRetainsAllEightLabels);
        Console.WriteLine("PASS: " + _passed + " crossing seed geometry policy tests.");
        return 0;
    }

    private static void OrientedFacesCoverEntrances()
    {
        CrossingSeedGeometryPolicy.Point3[] faces = CrossingSeedGeometryPolicy.OrientedFaceOffsets();
        Require(faces.Length == 5, "expected four horizontal faces plus the floor");
        bool posX = false, negX = false, posZ = false, negZ = false, floor = false;
        foreach (CrossingSeedGeometryPolicy.Point3 f in faces)
        {
            if (f.X > 0.5f) posX = true;
            if (f.X < -0.5f) negX = true;
            if (f.Z > 0.5f) posZ = true;
            if (f.Z < -0.5f) negZ = true;
            if (f.Y < -0.5f) floor = true;
        }
        Require(posX && negX && posZ && negZ,
            "a one-face-accessible trigger must be reachable from any of the four horizontal faces");
        Require(floor, "the floor face must remain seeded for tall triggers");
    }

    private static void PrimaryBudgetReservesFallbackSlots()
    {
        Require(CrossingSeedBudgetPolicy.PrimarySeedBudget == 30, "historical primary budget must remain 30");
        Require(CrossingSeedBudgetPolicy.ZeroSampleFallbackBudget == 8, "fallback reservation must remain eight");
        Require(CrossingSeedBudgetPolicy.PrimarySeedBudget + CrossingSeedBudgetPolicy.ZeroSampleFallbackBudget
            == CrossingSeedBudgetPolicy.MaxSeedsPerCrossing, "primary plus fallback must exactly fill the production budget");
        Require(CrossingSeedBudgetPolicy.PrimarySeedBudget <= 30, "primary discovery must not consume fallback capacity");
    }

    private static void ZeroSampleFallbackRetainsAllEightLabels()
    {
        List<string> simulatedPrimary = new List<string>();
        for (int i = 0; i < CrossingSeedBudgetPolicy.PrimarySeedBudget; i++) simulatedPrimary.Add("primary" + i);
        string[] labels = { "midRing0", "midRing1", "midRing2", "midRing3",
            "midRing4", "midRing5", "midRing6", "midRing7" };
        for (int i = 0; i < labels.Length; i++)
        {
            Require(CrossingSeedBudgetPolicy.CanAddFallback(simulatedPrimary.Count),
                "fallback slot " + labels[i] + " must remain available after primary zero-sample failure");
            simulatedPrimary.Add(labels[i]);
        }
        Require(simulatedPrimary.Count == CrossingSeedBudgetPolicy.MaxSeedsPerCrossing,
            "all eight fallback probes must fit after the full primary pass");
        for (int i = 0; i < labels.Length; i++)
            Require(simulatedPrimary.Contains(labels[i]), labels[i] + " must be available on fallback");
    }

    private static void VerticalProbeSpansAndIsBounded()
    {
        float[] probe = CrossingSeedGeometryPolicy.VerticalProbeOffsets(3);
        Require(probe.Length == 3, "requested step count is honoured");
        Require(Math.Abs(probe[0] + 1f) < 0.0001f, "probe starts at the bottom of the trigger's extent");
        Require(Math.Abs(probe[probe.Length - 1] - 1f) < 0.0001f, "probe ends at the top of the trigger's extent");
        Require(Math.Abs(probe[1]) < 0.0001f, "probe passes through the trigger's own centre height");
        // Bounded: this must stay a handful of samples, never an area scan.
        Require(CrossingSeedGeometryPolicy.VerticalProbeOffsets(50).Length <= 5, "probe count is capped");
        Require(CrossingSeedGeometryPolicy.VerticalProbeOffsets(0).Length >= 2, "probe count has a sane floor");
    }

    private static void FarSeedIsFilteredOut()
    {
        // The exact live number: an endpoint 40m from the verified crossing was rejected, so a seed
        // 40m out can only ever produce another rejected endpoint. It must not consume seed budget.
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSampling(40f, 8f, 4f),
            "a 40m-away seed cannot yield an accepted approach and must be filtered");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSampling(12.01f, 8f, 4f),
            "a seed beyond acceptance + sample radius is filtered");
    }

    private static void NearSeedIsAlwaysKept()
    {
        // Filtering must never remove a seed that could still produce an ACCEPTED approach: a sample
        // always lands within its own radius of its seed, so acceptance + radius is the exact bound.
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSampling(0f, 8f, 4f), "the crossing point itself is always kept");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSampling(8f, 8f, 4f), "a seed exactly at acceptance distance is kept");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSampling(11.99f, 8f, 4f),
            "a seed whose sample could still land inside acceptance is kept");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSampling(500f, 0f, 4f),
            "an unset acceptance distance disables filtering rather than rejecting everything");
    }

    private static void OrientedOffsetsAreNormalized()
    {
        // Offsets are unit local-space multipliers applied to the collider's own half-extents by the
        // caller, so rotation and lossyScale are handled by the real transform rather than by
        // axis-aligned bounds arithmetic that would place corners outside a rotated trigger.
        foreach (CrossingSeedGeometryPolicy.Point3 f in CrossingSeedGeometryPolicy.OrientedFaceOffsets())
        {
            Require(Math.Abs(f.X) <= 1f && Math.Abs(f.Y) <= 1f && Math.Abs(f.Z) <= 1f,
                "oriented offsets stay within the unit box so they map onto the collider's own faces");
            Require(Math.Abs(Math.Abs(f.X) + Math.Abs(f.Y) + Math.Abs(f.Z) - 1f) < 0.0001f,
                "each oriented offset addresses exactly one face");
        }
        foreach (float y in CrossingSeedGeometryPolicy.VerticalProbeOffsets(5))
            Require(y >= -1f && y <= 1f, "vertical probe stays inside the trigger's own extent");
    }

    private static void FloorSeedsUseMinimumY()
    {
        CrossingSeedGeometryPolicy.Point3 center = new CrossingSeedGeometryPolicy.Point3(10f, 31f, 5f);
        CrossingSeedGeometryPolicy.Point3 extents = new CrossingSeedGeometryPolicy.Point3(1f, 30f, 1f);
        CrossingSeedGeometryPolicy.Point3[] seeds = CrossingSeedGeometryPolicy.FloorSeeds(center, extents);
        Require(seeds.Length == 5, "expected the bottom-center point plus the four bottom corners");
        foreach (CrossingSeedGeometryPolicy.Point3 seed in seeds)
            Require(Math.Abs(seed.Y - 1f) < 0.001f, "every floor seed must sit at boundsCenter.Y - boundsExtents.Y (=1), got " + seed.Y);
    }

    private static void FloorSeedsSpanFootprint()
    {
        CrossingSeedGeometryPolicy.Point3 center = new CrossingSeedGeometryPolicy.Point3(0f, 10f, 0f);
        CrossingSeedGeometryPolicy.Point3 extents = new CrossingSeedGeometryPolicy.Point3(2f, 8f, 3f);
        CrossingSeedGeometryPolicy.Point3[] seeds = CrossingSeedGeometryPolicy.FloorSeeds(center, extents);
        bool sawMinCorner = false, sawMaxCorner = false;
        foreach (CrossingSeedGeometryPolicy.Point3 seed in seeds)
        {
            if (Math.Abs(seed.X - (-2f)) < 0.001f && Math.Abs(seed.Z - (-3f)) < 0.001f) sawMinCorner = true;
            if (Math.Abs(seed.X - 2f) < 0.001f && Math.Abs(seed.Z - 3f) < 0.001f) sawMaxCorner = true;
        }
        Require(sawMinCorner && sawMaxCorner, "floor seeds must reach every horizontal corner of the bounds, not only the center");
    }

    private static void ShortColliderSkipsFloorSeeding()
    {
        CrossingSeedGeometryPolicy.Point3 extents = new CrossingSeedGeometryPolicy.Point3(1f, 1.2f, 1f);
        Require(!CrossingSeedGeometryPolicy.FloorSeedsMeaningfullyDifferFromCenter(extents, 4f),
            "a 2.4m-tall collider is already fully covered by a 4m-radius center seed; floor seeding would be a redundant duplicate");
    }

    private static void TallColliderNeedsFloorSeeding()
    {
        CrossingSeedGeometryPolicy.Point3 extents = new CrossingSeedGeometryPolicy.Point3(1f, 30f, 1f);
        Require(CrossingSeedGeometryPolicy.FloorSeedsMeaningfullyDifferFromCenter(extents, 4f),
            "a 60m-tall collider's center seed cannot reach its floor with only a 4m radius");
    }

    // Reproduces the live Duskenlight Cove "Hidden" failure shape: a crossing whose transform position
    // (and therefore its collider-center seed) sits ~59m above real ground, with a tall trigger volume
    // whose floor face is right at ground level. NavMesh.SamplePosition itself cannot run in this
    // Unity-free suite, so "would a sample succeed" is modeled as a bounded vertical-reach check, which
    // is exactly what a locally flat NavMesh floor reduces a 3D sphere sample to.
    private static void TransformSeedMissesFloorSeedReachesGround()
    {
        const float groundY = 2f;
        const float transformY = 61.4f; // matches the live rawPos=(223.37, 61.40, 117.58) order of magnitude
        const float transformSeedRadius = 8f;

        Require(!CrossingSeedGeometryPolicy.WithinVerticalReachOfGround(transformY, groundY, transformSeedRadius),
            "an 8m-radius seed at the crossing's elevated transform position must not already reach the ground 59.4m below");

        CrossingSeedGeometryPolicy.Point3 boundsCenter = new CrossingSeedGeometryPolicy.Point3(223.37f, 31.7f, 117.58f);
        CrossingSeedGeometryPolicy.Point3 boundsExtents = new CrossingSeedGeometryPolicy.Point3(1.5f, 29.7f, 1.5f);
        Require(CrossingSeedGeometryPolicy.FloorSeedsMeaningfullyDifferFromCenter(boundsExtents, 4f),
            "this collider's ~59m height must be recognized as tall enough to need floor seeding");

        CrossingSeedGeometryPolicy.Point3[] floorSeeds = CrossingSeedGeometryPolicy.FloorSeeds(boundsCenter, boundsExtents);
        bool anyReachesGround = false;
        foreach (CrossingSeedGeometryPolicy.Point3 seed in floorSeeds)
            if (CrossingSeedGeometryPolicy.WithinVerticalReachOfGround(seed.Y, groundY, 4f)) anyReachesGround = true;
        Require(anyReachesGround, "at least one floor seed must land within reach of the real ground the transform-only seed missed");
    }


    // ---- 0.6.13 tall-trigger intermediate vertical coverage -----------------------------------

    private static void TallTriggerAddsIntermediateLayer()
    {
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        Require(CrossingSeedGeometryPolicy.IntermediateVerticalLayersMeaningfullyDifferFromCenter(half, FloorSeedRadius),
            "the live 47.11m-tall Hidden trigger leaves a real centre/floor blind band at 4m sample radius");
    }

    private static void ShortTriggerSkipsIntermediateLayer()
    {
        CrossingSeedGeometryPolicy.Point3 half = new CrossingSeedGeometryPolicy.Point3(2f, 6f, 2f);
        Require(!CrossingSeedGeometryPolicy.IntermediateVerticalLayersMeaningfullyDifferFromCenter(half, FloorSeedRadius),
            "a 12m-tall doorway does not justify another vertical layer at 4m sample radius");
    }

    private static void IntermediateOffsetsStayBounded()
    {
        CrossingSeedGeometryPolicy.Point3[] offsets = CrossingSeedGeometryPolicy.LowerIntermediateInteriorOffsets();
        Require(offsets.Length == 5, "the lower-mid layer is a bounded five-point cross, not a 3D grid");
        bool sawQuarterX = false;
        foreach (CrossingSeedGeometryPolicy.Point3 point in offsets)
        {
            Require(Math.Abs(point.Y + 0.5f) < 0.0001f, "every lower-mid point uses the same halfway-down layer");
            Require(Math.Abs(point.X) <= 0.25f && Math.Abs(point.Z) <= 0.25f, "lower-mid points stay inside the quarter-width cross");
            if (Math.Abs(point.X - 0.25f) < 0.0001f && Math.Abs(point.Z) < 0.0001f) sawQuarterX = true;
        }
        Require(sawQuarterX, "the interior cross includes the +X quarter-width point that matches the live Hidden geometry");
    }

    private static void AxisAlignedHiddenIntermediateSeedReachesKnownApproach()
    {
        // Pure AXIS-ALIGNED fixture using the live world-bounds diagnostic only. This is deliberately
        // not claimed to reconstruct the real BoxCollider local geometry: the 0.6.12 face diagnostics
        // prove the live trigger is transformed. Production uses authoritative BoxCollider local
        // center/size plus TransformPoint; this fixture only proves the intermediate-height arithmetic.
        CrossingSeedGeometryPolicy.Point3 center = new CrossingSeedGeometryPolicy.Point3(223.37f, 61.40f, 117.58f);
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        CrossingSeedGeometryPolicy.Point3 known = new CrossingSeedGeometryPolicy.Point3(232.14f, 50.06f, 116.71f);
        CrossingSeedGeometryPolicy.Point3 offset = CrossingSeedGeometryPolicy.LowerIntermediateInteriorOffsets()[1];
        CrossingSeedGeometryPolicy.Point3 seed = new CrossingSeedGeometryPolicy.Point3(
            center.X + offset.X * half.X,
            center.Y + offset.Y * half.Y,
            center.Z + offset.Z * half.Z);
        float distance = CrossingSeedGeometryPolicy.Distance(seed, known);
        Require(distance < FloorSeedRadius,
            "axis-aligned diagnostic fixture should put the old approach inside the unchanged 4m sample sphere; distance=" + distance);
    }

    private static void ApproachFaceUsesNormalizedRouteDirection()
    {
        CrossingSeedGeometryPolicy.Point3 half = new CrossingSeedGeometryPolicy.Point3(40f, 20f, 5f);
        // Although raw X displacement is larger, normalized Z displacement is farther outside this
        // long/thin box, so the approach-facing face must be the negative Z face.
        CrossingSeedGeometryPolicy.Point3 start = new CrossingSeedGeometryPolicy.Point3(50f, 0f, -20f);
        CrossingSeedGeometryPolicy.Point3[] offsets = CrossingSeedGeometryPolicy.LowerIntermediateApproachFaceOffsets(start, half);
        Require(offsets.Length == 3, "approach-facing band is exactly three seeds");
        foreach (CrossingSeedGeometryPolicy.Point3 point in offsets)
            Require(Math.Abs(point.Z + 1f) < 0.0001f, "normalized route direction chooses the -Z face on a long/thin box");
    }

    private static void ApproachFaceOffsetsStayBounded()
    {
        CrossingSeedGeometryPolicy.Point3 half = new CrossingSeedGeometryPolicy.Point3(8f, 12f, 8f);
        CrossingSeedGeometryPolicy.Point3[] offsets = CrossingSeedGeometryPolicy.LowerIntermediateApproachFaceOffsets(
            new CrossingSeedGeometryPolicy.Point3(20f, 0f, 2f), half);
        Require(offsets.Length == 3, "route-facing lower-mid band stays bounded to three samples");
        bool tangentPlus = false, tangentMinus = false;
        foreach (CrossingSeedGeometryPolicy.Point3 point in offsets)
        {
            Require(Math.Abs(point.X - 1f) < 0.0001f, "all samples stay on the selected +X face");
            Require(Math.Abs(point.Y + 0.5f) < 0.0001f, "all samples stay on the lower-intermediate height");
            Require(Math.Abs(point.Z) <= 0.25f, "tangent offsets never exceed quarter-width");
            if (point.Z > 0.2f) tangentPlus = true;
            if (point.Z < -0.2f) tangentMinus = true;
        }
        Require(tangentPlus && tangentMinus, "surface band samples both tangent directions");
    }

    // ---- 0.6.12 large-trigger seed retention -------------------------------------------------

    // The live Hidden trigger, as half-extents. center=(223.37, 61.40, 117.58), size=(67.50, 47.11, 59.35).
    private static CrossingSeedGeometryPolicy.Point3 LiveHiddenHalfExtents()
    {
        return new CrossingSeedGeometryPolicy.Point3(33.75f, 23.555f, 29.675f);
    }

    private const float Acceptance = 8f;   // RouteCandidatePolicy.NativeProbeApproachNearCrossing
    private const float FloorSeedRadius = 4f;

    // 1. A seed INSIDE the verified trigger volume must never be discarded for being far from centre.
    private static void InsideLargeTriggerIsRetained()
    {
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        // The oriented +X face centre: 33.75m from the centre, but ON the trigger volume.
        CrossingSeedGeometryPolicy.Point3 local = new CrossingSeedGeometryPolicy.Point3(33.75f, 0f, 0f);
        float centreDistance = CrossingSeedGeometryPolicy.Distance(
            local, new CrossingSeedGeometryPolicy.Point3(0f, 0f, 0f));
        Require(centreDistance > 12f,
            "fixture must exceed acceptance+radius when measured from the centre, or it proves nothing");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSampling(centreDistance, Acceptance, FloorSeedRadius),
            "the old centre-based filter must be shown discarding this on-volume seed");

        Require(CrossingSeedGeometryPolicy.IsInsideLocalBox(local, half),
            "the +X face centre lies on/inside the trigger volume");
        float volumeDistance = CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(local, half);
        Require(volumeDistance <= 0.001f, "a point on the volume has zero distance to it");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(volumeDistance, true, Acceptance, FloorSeedRadius),
            "a seed inside the verified trigger volume must be retained regardless of distance from centre");
    }

    // 2. A seed just outside a large trigger is judged by distance to the SURFACE, not the centre.
    private static void SurfaceSeedMeasuredToVolume()
    {
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        // 3m beyond the +X face: 36.75m from the centre, but only 3m from the volume.
        CrossingSeedGeometryPolicy.Point3 local = new CrossingSeedGeometryPolicy.Point3(36.75f, 0f, 0f);
        Require(!CrossingSeedGeometryPolicy.IsInsideLocalBox(local, half), "this fixture is outside the volume");

        float volumeDistance = CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(local, half);
        Require(Math.Abs(volumeDistance - 3f) < 0.001f, "distance to the volume is the 3m gap, not the 36.75m to centre");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(volumeDistance, false, Acceptance, FloorSeedRadius),
            "a seed 3m off a large trigger's face is well within acceptance+radius and must be kept");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSampling(36.75f, Acceptance, FloorSeedRadius),
            "the same seed measured to the centre would have been wrongly discarded");
    }

    // 3. Distance measured to the volume must still reject a genuinely remote seed - the 0.6.10 intent.
    private static void DistantSeedStillFiltered()
    {
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        // 40m beyond the +X face of the large trigger: far from the volume itself, not just its centre.
        CrossingSeedGeometryPolicy.Point3 local = new CrossingSeedGeometryPolicy.Point3(73.75f, 0f, 0f);
        Require(!CrossingSeedGeometryPolicy.IsInsideLocalBox(local, half), "fixture is outside the volume");
        float volumeDistance = CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(local, half);
        Require(Math.Abs(volumeDistance - 40f) < 0.001f, "fixture sits 40m off the volume surface");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(volumeDistance, false, Acceptance, FloorSeedRadius),
            "a seed 40m from the verified VOLUME can only ever yield a rejected endpoint and must stay filtered");
    }

    // 4. A large AND tall trigger must still generate, and now retain, its floor/vertical candidates.
    private static void LargeTallTriggerKeepsFloorAndVerticalSeeds()
    {
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        CrossingSeedGeometryPolicy.Point3 centre = new CrossingSeedGeometryPolicy.Point3(223.37f, 61.40f, 117.58f);

        Require(CrossingSeedGeometryPolicy.FloorSeedsMeaningfullyDifferFromCenter(half, FloorSeedRadius),
            "a 47m-tall trigger must still be recognized as needing floor seeding");

        CrossingSeedGeometryPolicy.Point3[] floorSeeds = CrossingSeedGeometryPolicy.FloorSeeds(centre, half);
        int retained = 0;
        foreach (CrossingSeedGeometryPolicy.Point3 seed in floorSeeds)
        {
            CrossingSeedGeometryPolicy.Point3 local = new CrossingSeedGeometryPolicy.Point3(
                seed.X - centre.X, seed.Y - centre.Y, seed.Z - centre.Z);
            bool inside = CrossingSeedGeometryPolicy.IsInsideLocalBox(local, half);
            float volumeDistance = CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(local, half);
            if (CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(volumeDistance, inside, Acceptance, FloorSeedRadius))
                retained++;
        }
        Require(retained == floorSeeds.Length,
            "all five floor seeds of this trigger lie on its own volume and must all survive filtering; retained=" + retained);

        // The vertical probe must still span the real height range, so a floor far below the trigger
        // origin is reachable.
        float[] heights = CrossingSeedGeometryPolicy.VerticalProbeOffsets(3);
        bool reachedBottom = false;
        foreach (float h in heights)
            if (h <= -0.99f) reachedBottom = true;
        Require(reachedBottom, "the vertical probe must still reach the trigger's own floor height");
    }

    // 5. A small collider behaves exactly as before: inside is inside, and far is still far. Volume
    //    measurement collapses to centre measurement when the volume is small.
    private static void SmallColliderBehaviorUnchanged()
    {
        CrossingSeedGeometryPolicy.Point3 half = new CrossingSeedGeometryPolicy.Point3(1.5f, 2f, 1.5f);

        CrossingSeedGeometryPolicy.Point3 near = new CrossingSeedGeometryPolicy.Point3(1f, 0f, 0f);
        Require(CrossingSeedGeometryPolicy.IsInsideLocalBox(near, half), "a point inside a small trigger is inside");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(0f, true, Acceptance, FloorSeedRadius),
            "an inside point is kept for a small trigger too");

        // 40m out from a small trigger: volume distance and centre distance agree to within the
        // trigger's own 1.5m half-extent, so the old and new filters make the same call.
        CrossingSeedGeometryPolicy.Point3 far = new CrossingSeedGeometryPolicy.Point3(40f, 0f, 0f);
        float volumeDistance = CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(far, half);
        float centreDistance = CrossingSeedGeometryPolicy.Distance(
            far, new CrossingSeedGeometryPolicy.Point3(0f, 0f, 0f));
        Require(Math.Abs(centreDistance - volumeDistance) <= 1.5f + 0.001f,
            "for a small trigger the two metrics cannot diverge by more than its own half-extent");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(volumeDistance, false, Acceptance, FloorSeedRadius),
            "a 40m seed beside a small trigger stays filtered");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSampling(centreDistance, Acceptance, FloorSeedRadius),
            "the old filter agreed on this case - small-collider behavior is unchanged");
    }

    // 6. Rotation/scale are honoured because the caller expresses the seed in the collider's own local
    //    space; the same world offset is inside or outside depending on the box's own basis.
    private static void RotatedScaledVolumeDistanceHonoursBasis()
    {
        // A long, thin trigger: 30m along local X, 2m along local Z.
        CrossingSeedGeometryPolicy.Point3 half = new CrossingSeedGeometryPolicy.Point3(30f, 5f, 2f);

        // 20m along the box's LONG axis is inside; the same 20m along its SHORT axis is far outside.
        CrossingSeedGeometryPolicy.Point3 alongLong = new CrossingSeedGeometryPolicy.Point3(20f, 0f, 0f);
        CrossingSeedGeometryPolicy.Point3 alongShort = new CrossingSeedGeometryPolicy.Point3(0f, 0f, 20f);

        Require(CrossingSeedGeometryPolicy.IsInsideLocalBox(alongLong, half),
            "20m along the long axis lies inside this trigger");
        Require(!CrossingSeedGeometryPolicy.IsInsideLocalBox(alongShort, half),
            "20m along the short axis lies outside this trigger");
        Require(CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(
                    CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(alongLong, half), true, Acceptance, FloorSeedRadius),
            "the on-volume seed is kept");
        Require(!CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(
                    CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(alongShort, half), false, Acceptance, FloorSeedRadius),
            "the off-volume seed at the same 20m world offset is filtered - the basis, not the raw distance, decides");
    }

    // 7. End-to-end shape of the reported regression: with the live Hidden geometry, count how many of
    //    the trigger's own oriented-face and floor seeds each filter retains. The centre-based filter
    //    keeps only the ones sitting on the vertical column through the centre - which is exactly the
    //    "generatedSeeds=16, samples=0" collapse - while the volume-based filter keeps them all.
    private static void LiveHiddenRegressionIsRepaired()
    {
        CrossingSeedGeometryPolicy.Point3 half = LiveHiddenHalfExtents();
        CrossingSeedGeometryPolicy.Point3 origin = new CrossingSeedGeometryPolicy.Point3(0f, 0f, 0f);

        CrossingSeedGeometryPolicy.Point3[] faces = CrossingSeedGeometryPolicy.OrientedFaceOffsets();
        int keptByCentre = 0, keptByVolume = 0, lateralFaces = 0;
        foreach (CrossingSeedGeometryPolicy.Point3 face in faces)
        {
            CrossingSeedGeometryPolicy.Point3 local = new CrossingSeedGeometryPolicy.Point3(
                face.X * half.X, face.Y * half.Y, face.Z * half.Z);
            // Horizontal distance, matching how the planner measures seed proximity.
            float centreDistance = CrossingSeedGeometryPolicy.Distance(
                new CrossingSeedGeometryPolicy.Point3(local.X, 0f, local.Z), origin);
            if (centreDistance > 0.001f) lateralFaces++;
            if (CrossingSeedGeometryPolicy.SeedIsWorthSampling(centreDistance, Acceptance, FloorSeedRadius)) keptByCentre++;
            bool inside = CrossingSeedGeometryPolicy.IsInsideLocalBox(local, half);
            if (CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(
                    CrossingSeedGeometryPolicy.LocalBoxSurfaceDistance(local, half), inside, Acceptance, FloorSeedRadius))
                keptByVolume++;
        }

        Require(lateralFaces == 4, "the four horizontal faces of this trigger are all laterally displaced");
        Require(keptByCentre == 1,
            "the centre-based filter kept only the bottom face - every lateral entrance was discarded; keptByCentre=" + keptByCentre);
        Require(keptByVolume == faces.Length,
            "the volume-based filter must retain every face of the verified trigger; keptByVolume=" + keptByVolume);
    }

    private static void LargeTriggerGetsBoundedFallbackRing()
    {
        CrossingSeedGeometryPolicy.Point3[] ring = CrossingSeedGeometryPolicy.LowerIntermediateFallbackRingOffsets(
            40f, 50f, 4f, 8);
        Require(ring.Length == 8, "large portal should get exactly the bounded eight-point fallback ring");
        foreach (CrossingSeedGeometryPolicy.Point3 p in ring)
        {
            Require(Math.Abs(p.X) < 1f && Math.Abs(p.Z) < 1f, "fallback ring stays inside the trigger footprint");
            Require(Math.Abs(p.Y + 0.5f) < 0.0001f, "fallback ring stays on the lower-intermediate vertical layer");
        }
    }

    private static void SmallTriggerSkipsFallbackRing()
    {
        CrossingSeedGeometryPolicy.Point3[] ring = CrossingSeedGeometryPolicy.LowerIntermediateFallbackRingOffsets(
            6f, 5f, 4f, 8);
        Require(ring.Length == 0, "a small trigger adds no ring because the centre sample already covers it");
    }

    private static void LiveRotatedHiddenIntermediateBandReachesKnownApproach()
    {
        // Exact live Hidden geometry: localSize=(1,1,1), rotationY=40.27, lossyScale=(80,47.11,10),
        // origin=(223.37,61.40,117.58), with the historically usable point near (232.14,50.06,116.71).
        CrossingSeedGeometryPolicy.Point3 localHalf = new CrossingSeedGeometryPolicy.Point3(0.5f, 0.5f, 0.5f);
        CrossingSeedGeometryPolicy.Point3 worldHalf = new CrossingSeedGeometryPolicy.Point3(40f, 23.555f, 5f);
        Require(Math.Abs(worldHalf.Y - 23.555f) < 0.001f, "world half Y must be 23.555m");
        Require(!CrossingSeedGeometryPolicy.IntermediateVerticalLayersMeaningfullyDifferFromCenter(localHalf, 4f),
            "the old local-half predicate must fail this fixture");
        Require(CrossingSeedGeometryPolicy.IntermediateVerticalLayersMeaningfullyDifferFromCenter(worldHalf, 4f),
            "the world-scale predicate must require the intermediate layer");

        CrossingSeedGeometryPolicy.Point3 localStart = new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1.5f);
        Require(CrossingSeedGeometryPolicy.SelectRouteFacingAxis(localStart, localHalf)
            == CrossingSeedGeometryPolicy.RouteFaceAxis.ZPositive, "live player-side approach must choose +Z face");
        CrossingSeedGeometryPolicy.Point3[] offsets = CrossingSeedGeometryPolicy.LowerIntermediateApproachFaceOffsets(localStart, localHalf);
        Require(offsets.Length == 3, "live intermediate route-facing band must generate three midApproach probes");

        const float originX = 223.37f;
        const float originY = 61.40f;
        const float originZ = 117.58f;
        const float yaw = 40.27f;
        const float knownX = 232.14f;
        const float knownY = 50.06f;
        const float knownZ = 116.71f;
        double radians = yaw * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);
        float best = float.MaxValue;
        foreach (CrossingSeedGeometryPolicy.Point3 p in offsets)
        {
            float localX = p.X * localHalf.X * 80f;
            float localY = p.Y * localHalf.Y * 47.11f;
            float localZ = p.Z * localHalf.Z * 10f;
            float worldX = originX + localX * cos + localZ * sin;
            float worldY = originY + localY;
            float worldZ = originZ - localX * sin + localZ * cos;
            float dx = worldX - knownX;
            float dy = worldY - knownY;
            float dz = worldZ - knownZ;
            float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < best) best = d;
        }
        Require(best < 4f, "at least one midApproach seed must place the known walkable approach inside its 4m SamplePosition sphere; best=" + best);
    }

    // ---- 0.6.18 live Vitheo large-trigger sampling repair --------------------------------------
    //
    // Live 0.6.17, crossing vitheo|zoneline (1), rawPos=(269.946, 28.200, 54.790):
    // generatedSeeds=22, filteredSeeds=0, samples=1, accepted=1. The single sampled/accepted
    // candidate was seed=face1 - the oriented -X face centre - at approach=(229.95, 28.53, 54.79),
    // qualityDist=40.31 measured against qualityRef=routeFaceZ+. Ranking was not the failure: with
    // one accepted candidate there was nothing to rank, so the extreme face was selected by default
    // and the party walked 40m toward the far hill.
    //
    // The fixture below reproduces that SHAPE CLASS generically - a large oriented trigger, a
    // route-facing quality reference on one axis, a lone viable extreme candidate on another face,
    // and no NavMesh at the mathematically ideal face centre - using no Vitheo coordinate and no
    // hardcoded destination. It requires a bounded route-facing probe to become a real candidate and
    // to outrank the extreme face under the UNCHANGED 0.6.17 ranking policy, and it requires the
    // extreme face to remain a legal selection when every probe fails.

    private const float FixtureYawDegrees = 30f;
    private const float FixtureSampleRadius = 4f;
    // A unit BoxCollider (localSize 1,1,1 -> local half 0.5) under a large non-uniform lossyScale,
    // which is the authoritative shape the live diagnostics have repeatedly reported for these
    // zonelines. Oriented world half axes therefore are (40, 20, 50) metres.
    private static readonly float[] FixtureWorldHalf = { 40f, 20f, 50f };
    private static readonly float[] FixtureCenter = { 120f, 28f, -60f };
    private static readonly CrossingSeedGeometryPolicy.Point3 FixtureLocalHalf =
        new CrossingSeedGeometryPolicy.Point3(0.5f, 0.5f, 0.5f);

    // Normalized local offset in [-1,+1] -> world, through the box's own half axes, yaw and centre.
    private static float[] FixtureToWorld(CrossingSeedGeometryPolicy.Point3 normalized)
    {
        float x = normalized.X * FixtureWorldHalf[0];
        float y = normalized.Y * FixtureWorldHalf[1];
        float z = normalized.Z * FixtureWorldHalf[2];
        double yaw = FixtureYawDegrees * Math.PI / 180.0;
        float cos = (float)Math.Cos(yaw);
        float sin = (float)Math.Sin(yaw);
        return new[]
        {
            FixtureCenter[0] + x * cos + z * sin,
            FixtureCenter[1] + y,
            FixtureCenter[2] - x * sin + z * cos
        };
    }

    // Stand-in for NavMesh.SamplePosition: a true 3D sphere search against the fixture's walkable
    // points, at the same bounded radius production uses. Nothing is widened for the test.
    private static bool FixtureSample(float[] point, float[][] walkable, float radius, out float[] hit)
    {
        hit = null;
        float best = float.MaxValue;
        for (int i = 0; i < walkable.Length; i++)
        {
            float d = FixtureDistance(point, walkable[i]);
            if (d <= radius && d < best)
            {
                best = d;
                hit = walkable[i];
            }
        }
        return hit != null;
    }

    private static float FixtureDistance(float[] a, float[] b)
    {
        float dx = a[0] - b[0];
        float dy = a[1] - b[1];
        float dz = a[2] - b[2];
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static float FixtureHorizontalDistance(float[] a, float[] b)
    {
        float dx = a[0] - b[0];
        float dz = a[2] - b[2];
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // The exact acceptance-relevant measurements the live 0.6.17 line reported for the accepted
    // candidate, so the fixture exercises the real RouteCandidatePolicy acceptance path rather than a
    // convenient one. routeLength is supplied per candidate because the repair must prove that a
    // better-quality entrance wins even when its route is LONGER than the extreme edge's.
    private static RouteCandidatePolicy.Candidate FixtureCandidate(string seedLabel, float[] approach,
        float[] qualityReference, float routeLength)
    {
        RouteCandidatePolicy.Candidate candidate = new RouteCandidatePolicy.Candidate();
        candidate.StableKey = seedLabel;
        candidate.SeedLabel = seedLabel;
        candidate.Sampled = true;
        candidate.Path = RouteCandidatePolicy.PathKind.Partial;
        candidate.CornerCount = 10;
        candidate.StartDistanceToCrossing = 196.18f;
        candidate.EndpointDistanceToCrossing = 2.25f;
        candidate.ApproachDistanceToCrossing = 0f;
        candidate.RouteLength = routeLength;
        candidate.HasApproachQuality = true;
        candidate.ApproachQualityDistance = FixtureHorizontalDistance(approach, qualityReference);
        candidate.ApproachQualityReferenceLabel = "routeFaceZ+";
        return candidate;
    }

    private static CrossingSeedGeometryPolicy.EntranceProbe[] FixtureProbes(
        out CrossingSeedGeometryPolicy.Point3 localStart)
    {
        // Route start well beyond the +Z face in the box's own local space: normalized Z distance
        // dominates normalized X, so both the quality reference and the probes must choose Z+.
        localStart = new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1.5f);
        Require(CrossingSeedGeometryPolicy.SelectRouteFacingAxis(localStart, FixtureLocalHalf)
                == CrossingSeedGeometryPolicy.RouteFaceAxis.ZPositive,
            "the fixture must place the route-facing reference on the +Z face");
        // The face's tangent is the OTHER horizontal world half axis, exactly as the planner passes it.
        return CrossingSeedGeometryPolicy.RouteFacingEntranceProbes(localStart, FixtureLocalHalf,
            FixtureWorldHalf[0], FixtureWorldHalf[1], FixtureSampleRadius);
    }

    private static List<RouteCandidatePolicy.Candidate> FixtureAcceptedCandidates(float[][] walkable,
        float[] qualityReference)
    {
        CrossingSeedGeometryPolicy.Point3 localStart;
        CrossingSeedGeometryPolicy.EntranceProbe[] probes = FixtureProbes(out localStart);
        List<RouteCandidatePolicy.Candidate> candidates = new List<RouteCandidatePolicy.Candidate>();

        // The lone viable extreme candidate the live run produced: the oriented -X face centre.
        float[] extreme = FixtureToWorld(new CrossingSeedGeometryPolicy.Point3(-1f, 0f, 0f));
        float[] extremeHit;
        if (FixtureSample(extreme, walkable, FixtureSampleRadius, out extremeHit))
            candidates.Add(FixtureCandidate("face1", extremeHit, qualityReference, 200f));

        List<float[]> already = new List<float[]>();
        for (int i = 0; i < probes.Length; i++)
        {
            float[] point = FixtureToWorld(probes[i].Offset);
            float[] hit;
            if (!FixtureSample(point, walkable, FixtureSampleRadius, out hit)) continue;
            bool duplicate = false;
            for (int d = 0; d < already.Count; d++)
                if (FixtureHorizontalDistance(already[d], hit) <= 0.75f) duplicate = true;
            if (duplicate) continue;
            already.Add(hit);
            // Deliberately LONGER than the extreme edge's route, so a pass can only come from the
            // quality term the 0.6.17 ranking policy already applies - not from route length.
            candidates.Add(FixtureCandidate("routeEntrance" + i, hit, qualityReference, 260f));
        }
        return candidates;
    }

    private static void LiveShapeClassRouteFacingProbeOutranksExtremeFace()
    {
        float[] qualityReference = FixtureToWorld(new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1f));
        CrossingSeedGeometryPolicy.Point3 localStart;
        CrossingSeedGeometryPolicy.EntranceProbe[] probes = FixtureProbes(out localStart);
        Require(probes.Length > 3, "the repaired second stage must cover more than the 0.6.17 three points");
        Require(probes.Length <= CrossingSeedGeometryPolicy.MaxRouteFacingEntranceProbes,
            "second-stage probing must stay hard-bounded");

        // One bounded route-facing probe that is NOT the ideal face centre: a lower-intermediate
        // level, near-face depth, quarter tangent. This is the class of point 0.6.17 could not place.
        int chosen = -1;
        for (int i = 0; i < probes.Length; i++)
            if (probes[i].Level < -0.4f && probes[i].Depth > 0.9f && probes[i].Tangent > 0.2f) chosen = i;
        Require(chosen >= 0, "a lower-intermediate near-face tangent probe must exist for a large tall trigger");
        float[] probeWorld = FixtureToWorld(probes[chosen].Offset);

        float[] extreme = FixtureToWorld(new CrossingSeedGeometryPolicy.Point3(-1f, 0f, 0f));
        float[][] walkable = { extreme, probeWorld };

        float[] unusedHit;
        Require(!FixtureSample(qualityReference, walkable, FixtureSampleRadius, out unusedHit),
            "the fixture must keep the mathematically ideal face-centre sample unavailable");

        List<RouteCandidatePolicy.Candidate> candidates = FixtureAcceptedCandidates(walkable, qualityReference);
        bool hasEntrance = false;
        bool hasExtreme = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].SeedLabel.StartsWith("routeEntrance", StringComparison.Ordinal)) hasEntrance = true;
            if (candidates[i].SeedLabel == "face1") hasExtreme = true;
        }
        Require(hasExtreme, "the extreme face candidate must still be produced");
        Require(hasEntrance, "a bounded route-facing probe must become a real competing candidate");

        List<RouteCandidatePolicy.Evaluation> ranked = RouteCandidatePolicy.RankAccepted(candidates);
        Require(ranked.Count >= 2, "both the entrance probe and the extreme face must be accepted");
        Require(ranked[0].Candidate.SeedLabel.StartsWith("routeEntrance", StringComparison.Ordinal),
            "the route-facing entrance probe must outrank the extreme face; selected=" + ranked[0].Candidate.SeedLabel);
        Require(ranked[0].Candidate.ApproachQualityDistance < 40.31f,
            "the selected approach must be materially closer to the route-facing reference than the live 40.31m edge");
    }

    private static void ExtremeFaceRemainsLegalWhenEveryProbeFails()
    {
        float[] qualityReference = FixtureToWorld(new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1f));
        float[] extreme = FixtureToWorld(new CrossingSeedGeometryPolicy.Point3(-1f, 0f, 0f));
        float[][] walkable = { extreme };

        List<RouteCandidatePolicy.Candidate> candidates = FixtureAcceptedCandidates(walkable, qualityReference);
        Require(candidates.Count == 1, "only the extreme face can sample in this fixture");
        List<RouteCandidatePolicy.Evaluation> ranked = RouteCandidatePolicy.RankAccepted(candidates);
        Require(ranked.Count == 1 && ranked[0].Accepted && ranked[0].Candidate.SeedLabel == "face1",
            "a lone extreme face must remain a legal accepted fallback when every route-facing probe fails");
    }

    private static void SmallTriggerSkipsRouteFacingProbing()
    {
        // The second stage is gated on a large footprint, exactly as in 0.6.17. A small crossing must
        // pay no extra NavMesh work and keep its pre-existing selection.
        RouteCandidatePolicy.Candidate candidate = FixtureCandidate("face1",
            new[] { 0f, 0f, 0f }, new[] { 40f, 0f, 0f }, 200f);
        RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
        Require(evaluation.Accepted, "the fixture candidate must be accepted for the gate test to mean anything");
        Require(!RouteCandidatePolicy.ShouldProbeRouteFacingEntrance(evaluation, 10f),
            "a small trigger must never enter second-stage route-facing probing");
        Require(RouteCandidatePolicy.ShouldProbeRouteFacingEntrance(evaluation, 40f),
            "a large trigger with a quality-poor lone candidate must still enter second-stage probing");
    }

    private static void TallNarrowTriggerKeepsThreeTangentSteps()
    {
        // Tall behaviour is unchanged: the lower-intermediate level is added because the trigger is
        // tall, but a narrow face does not earn the extra half-tangent probes.
        CrossingSeedGeometryPolicy.EntranceProbe[] probes = CrossingSeedGeometryPolicy.RouteFacingEntranceProbes(
            new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1.5f), FixtureLocalHalf, 10f, 20f, FixtureSampleRadius);
        Require(!CrossingSeedGeometryPolicy.FaceNeedsWideTangentCoverage(10f, FixtureSampleRadius),
            "a 10m half-width face is not wide enough to earn extra tangent probes");
        bool sawLowerLevel = false;
        for (int i = 0; i < probes.Length; i++)
        {
            Require(Math.Abs(probes[i].Tangent) <= 0.25f + 0.0001f,
                "a narrow face must keep three tangent steps");
            if (probes[i].Level < -0.4f) sawLowerLevel = true;
        }
        Require(sawLowerLevel, "a tall trigger must still probe the lower-intermediate vertical level");
        Require(probes.Length == 12, "tall + narrow is two levels x two depths x three tangents; got " + probes.Length);
    }

    private static void ShortTriggerKeepsCentreLevelOnly()
    {
        CrossingSeedGeometryPolicy.EntranceProbe[] probes = CrossingSeedGeometryPolicy.RouteFacingEntranceProbes(
            new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1.5f), FixtureLocalHalf, 10f, 3f, FixtureSampleRadius);
        for (int i = 0; i < probes.Length; i++)
            Require(Math.Abs(probes[i].Level) < 0.0001f,
                "a short trigger must not gain a redundant lower-intermediate level");
        Require(probes.Length == 6, "short + narrow is one level x two depths x three tangents; got " + probes.Length);
    }

    private static void RotatedTriggerProbesStayInsideNormalizedBox()
    {
        // Probes are normalized LOCAL offsets, so rotation and non-uniform scale are handled entirely
        // by the caller's BoxCollider transform. Staying strictly inside [-1,+1] is what makes that
        // safe for a rotated trigger: no probe can ever land outside the real volume.
        CrossingSeedGeometryPolicy.EntranceProbe[] probes = CrossingSeedGeometryPolicy.RouteFacingEntranceProbes(
            new CrossingSeedGeometryPolicy.Point3(-2f, 0f, 0.1f), FixtureLocalHalf, 40f, 20f, FixtureSampleRadius);
        Require(probes.Length > 0 && probes.Length <= CrossingSeedGeometryPolicy.MaxRouteFacingEntranceProbes,
            "probe count must stay bounded");
        for (int i = 0; i < probes.Length; i++)
        {
            CrossingSeedGeometryPolicy.Point3 o = probes[i].Offset;
            Require(Math.Abs(o.X) < 1f && Math.Abs(o.Y) < 1f && Math.Abs(o.Z) < 1f,
                "every probe must stay strictly inside the oriented box");
            Require(o.X < 0f, "a route start beyond the -X face must be probed on the -X face");
        }
    }

    private static void RouteFacingProbesCoverTheQualityReferenceFace()
    {
        // The 0.6.17 mismatch in one assertion: quality was measured against routeFaceZ+ while the only
        // sampled candidate sat on the -X face. The probes and the quality reference must agree on the
        // face, or second-stage probing cannot improve the quality metric by construction.
        CrossingSeedGeometryPolicy.Point3 localStart = new CrossingSeedGeometryPolicy.Point3(0f, 0f, 1.5f);
        CrossingSeedGeometryPolicy.RouteFaceAxis axis =
            CrossingSeedGeometryPolicy.SelectRouteFacingAxis(localStart, FixtureLocalHalf);
        Require(CrossingSeedGeometryPolicy.RouteFaceLabel(axis) == "routeFaceZ+",
            "the shared helper must label the fixture's route-facing face routeFaceZ+");
        CrossingSeedGeometryPolicy.EntranceProbe[] probes = CrossingSeedGeometryPolicy.RouteFacingEntranceProbes(
            localStart, FixtureLocalHalf, FixtureWorldHalf[0], FixtureWorldHalf[1], FixtureSampleRadius);
        for (int i = 0; i < probes.Length; i++)
            Require(probes[i].Offset.Z > 0f, "every probe must sit on the same face the quality reference uses");
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
