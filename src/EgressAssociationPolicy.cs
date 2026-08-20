using System;
using System.Globalization;

namespace ErenshorFollow
{
    // Pure, Unity-free rules for the read-only egress-POI diagnostic. This decides nothing about
    // routing: it only classifies observed scene geometry so the live report is reproducible and the
    // wording is covered by deterministic tests rather than only exercised in game.
    //
    // WHY THIS EXISTS
    // Native Erenshor never searches for a Zoneline by destination. Every long-distance Sim move is
    // NavMeshAgent.SetDestination(GameData.GetSafeNavMeshPoint(<authored transform>)), and the
    // authored transform for a zone exit is a PointOfInterest whose Use == POIType.zoneline (these
    // register themselves into GameData.EgressLocations at Awake). Follow instead computes an
    // approach point on the Zoneline's trigger collider. Before considering whether the authored POI
    // could replace that computation, we must first prove the authored POIs exist, sit on NavMesh,
    // and associate unambiguously with a specific crossing. That proof is what this pass gathers.
    //
    // CRITICAL MEASUREMENT RULE
    // A POI is associated with a crossing by distance to the crossing's real collider VOLUME, never
    // to its raw transform position. The live Vitheo trigger is exactly why: on a large or rotated
    // BoxCollider the raw centre can be tens of metres from a point sitting on the trigger itself,
    // which is the same defect that produced the 0.6.11 seed regression. Raw-transform distance is
    // retained in the report as a secondary diagnostic only.
    internal static class EgressAssociationPolicy
    {
        // GameData.GetSafeNavMeshPoint's own defaults, mirrored so the diagnostic reports what NATIVE
        // travel would accept rather than what Follow's wider planner would accept. Native samples at
        // 2m and requires the hit to be within 0.25m vertically, otherwise it returns the raw point
        // unchanged - it has no fallback search at all.
        internal const float NativeSampleRadius = 2f;
        internal const float NativeVerticalTolerance = 0.25f;

        // A second candidate must be at least this much farther from the POI before the nearest
        // crossing is called a unique association. Chosen so two genuinely distinct exits separate
        // cleanly while two overlapping triggers around one doorway are honestly reported ambiguous.
        internal const float UniqueAssociationMargin = 5f;

        // Beyond this the POI is simply not near any crossing in this scene. Egress POIs are authored
        // standing spots in front of an exit, so a best-volume distance larger than this is evidence
        // of absence, not of a distant match.
        internal const float MaxAssociationDistance = 40f;

        internal enum AssociationKind { None, Ambiguous, Unique }

        // Would NATIVE travel accept this POI as a destination? Native calls SamplePosition at 2m and
        // then requires |hit.y - raw.y| <= 0.25; on failure it uses the raw point, which means an
        // off-mesh authored POI degrades native Sim travel too rather than being silently corrected.
        internal static bool NativeSampleAccepted(bool sampled, float verticalDifference)
        {
            if (!sampled) return false;
            if (float.IsNaN(verticalDifference) || float.IsInfinity(verticalDifference)) return false;
            return Math.Abs(verticalDifference) <= NativeVerticalTolerance;
        }

        // Classification from the two best candidates only - the caller has already ordered them by
        // (inside volume, then volume distance). Containment beats proximity because a POI standing
        // inside a trigger volume is unambiguous evidence of which exit it serves; two containments
        // are genuinely ambiguous and must be reported as such rather than resolved by a tiebreak.
        internal static AssociationKind Classify(int candidateCount, bool bestInside, float bestVolumeDistance,
            bool secondInside, float secondVolumeDistance, float uniqueMargin, float maxAssociationDistance)
        {
            if (candidateCount <= 0) return AssociationKind.None;
            if (!bestInside && !Finite(bestVolumeDistance)) return AssociationKind.None;
            if (!bestInside && bestVolumeDistance > Math.Max(0f, maxAssociationDistance)) return AssociationKind.None;
            if (candidateCount == 1) return AssociationKind.Unique;
            if (bestInside) return secondInside ? AssociationKind.Ambiguous : AssociationKind.Unique;
            if (secondInside) return AssociationKind.Ambiguous;
            if (!Finite(secondVolumeDistance)) return AssociationKind.Unique;
            return secondVolumeDistance - bestVolumeDistance >= Math.Max(0f, uniqueMargin)
                ? AssociationKind.Unique
                : AssociationKind.Ambiguous;
        }

        // Ordering used before Classify. Returns <0 when A is the better association for a POI.
        // Deliberately independent of raw-transform distance: including it here would reintroduce the
        // exact centre-distance bias this diagnostic exists to avoid.
        internal static int CompareCandidates(bool aInside, float aVolumeDistance, string aKey,
            bool bInside, float bVolumeDistance, string bKey)
        {
            if (aInside != bInside) return aInside ? -1 : 1;
            int distance = Safe(aVolumeDistance).CompareTo(Safe(bVolumeDistance));
            if (distance != 0) return distance;
            return string.Compare(aKey ?? string.Empty, bKey ?? string.Empty, StringComparison.Ordinal);
        }

        // One-line verdict for the report. Kept here so its exact wording is deterministic.
        internal static string DescribeAssociation(AssociationKind kind, string bestKey, bool bestInside,
            float bestVolumeDistance, float bestRawDistance)
        {
            switch (kind)
            {
                case AssociationKind.None:
                    return "association=NONE";
                case AssociationKind.Ambiguous:
                    return "association=AMBIGUOUS nearest=" + Key(bestKey) +
                        " inside=" + bestInside +
                        " dVol=" + Metres(bestVolumeDistance) +
                        " dRaw=" + Metres(bestRawDistance);
                default:
                    return "association=UNIQUE crossing=" + Key(bestKey) +
                        " inside=" + bestInside +
                        " dVol=" + Metres(bestVolumeDistance) +
                        " dRaw=" + Metres(bestRawDistance);
            }
        }

        // The comparison the whole pass is for: is the authored POI a materially more sensible target
        // than the geometry-derived approach Follow currently selects? Answered purely from the two
        // candidates' distances to the same crossing volume, so no coordinate is ever hardcoded.
        internal static bool AuthoredTargetIsMoreSensible(bool poiSampled, float poiVerticalDifference,
            float poiVolumeDistance, float computedApproachVolumeDistance, float requiredImprovement)
        {
            if (!NativeSampleAccepted(poiSampled, poiVerticalDifference)) return false;
            if (!Finite(poiVolumeDistance) || !Finite(computedApproachVolumeDistance)) return false;
            return computedApproachVolumeDistance - poiVolumeDistance >= Math.Max(0f, requiredImprovement);
        }

        private static string Key(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
        }

        private static string Metres(float value)
        {
            return Finite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";
        }

        private static float Safe(float value)
        {
            return Finite(value) ? value : float.MaxValue;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value < float.MaxValue;
        }
    }
}
