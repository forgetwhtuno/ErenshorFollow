using System;

namespace ErenshorFollow
{
    // Pure, Unity-free geometry used by LocalZoneRoutePlanner's approach-seed generation. A live
    // Duskenlight Cove crossing (destination "Hidden") produced zero NavMesh samples despite having
    // one live trigger collider: rawPos=(223.37, 61.40, 117.58). Windwashed, from the same zone/session,
    // produced a proven Complete/28-corner route, so the planner and native NavMesh both work in this
    // zone -- the defect is specific to how seeds are placed for THIS crossing's geometry.
    //
    // The existing seed set (transform position, the collider's bounds.center, and the collider point
    // closest to the party) all cluster around the collider's vertical MIDPOINT/transform origin. If a
    // zoneline's trigger is a tall vertical volume -- an archway, doorway, or cliff-face trigger that
    // spans from real ground level up to well above head height -- every one of those seeds can sit
    // many meters above the walkable floor, and Unity's NavMesh.SamplePosition performs a true 3D sphere
    // search, so a seed that far above the floor fails to find NavMesh within any reasonably bounded
    // radius. This is a plausible, generic explanation that requires no Hidden-specific coordinate: any
    // crossing with a tall trigger volume would exhibit the same zero-sample failure.
    //
    // The fix is to also seed the collider bounds' FLOOR (its minimum Y face) instead of relying only on
    // its vertical center: the floor of a tall trigger is far more likely to sit on or near real walkable
    // ground than its geometric midpoint. This changes only WHERE Follow looks for an approach point --
    // every generated seed still has to pass NavMesh.SamplePosition, then CalculatePath, then the exact
    // same RouteCandidatePolicy acceptance distances as before. It cannot weaken what counts as a valid
    // route; it can only widen where a genuinely valid one might be found.
    internal static class CrossingSeedGeometryPolicy
    {
        internal struct Point3
        {
            internal float X;
            internal float Y;
            internal float Z;
            internal Point3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        // The horizontal center of the bounds' bottom face, plus its four bottom corners. Five points,
        // all at boundsCenter.Y - boundsExtents.Y (the floor), bounding this to a fixed, small, one-time
        // cost per crossing rather than any kind of area scan.
        internal static Point3[] FloorSeeds(Point3 boundsCenter, Point3 boundsExtents)
        {
            float floorY = boundsCenter.Y - Math.Abs(boundsExtents.Y);
            float minX = boundsCenter.X - Math.Abs(boundsExtents.X);
            float maxX = boundsCenter.X + Math.Abs(boundsExtents.X);
            float minZ = boundsCenter.Z - Math.Abs(boundsExtents.Z);
            float maxZ = boundsCenter.Z + Math.Abs(boundsExtents.Z);
            return new[]
            {
                new Point3(boundsCenter.X, floorY, boundsCenter.Z),
                new Point3(minX, floorY, minZ),
                new Point3(maxX, floorY, minZ),
                new Point3(minX, floorY, maxZ),
                new Point3(maxX, floorY, maxZ)
            };
        }

        // True when the collider's own vertical extent is large enough that seeding only its center
        // could plausibly miss ground the floor seeds would reach - i.e. whether floor seeding is
        // actually a materially different search from the existing center-based seed, not merely an
        // equivalent duplicate. Kept as an explicit, testable fact rather than an unconditional add so a
        // short/flat trigger does not silently double its seed count for no geometric reason.
        internal static bool FloorSeedsMeaningfullyDifferFromCenter(Point3 boundsExtents, float seedRadius)
        {
            return Math.Abs(boundsExtents.Y) > Math.Max(0f, seedRadius);
        }


        // A tall trigger can have a walkable approach between its geometric centre and its bottom
        // face. Centre-only + floor-only probing leaves a vertical blind band whenever the half-height
        // is more than roughly two sample radii. The live Hidden trigger is the concrete case:
        // centre Y=61.40, bottom Y=37.84, while a previously proven approach was around Y=50.06.
        // This predicate is intentionally conservative so ordinary short doorway triggers do not gain
        // extra seed work for no geometric reason.
        internal static bool IntermediateVerticalLayersMeaningfullyDifferFromCenter(Point3 boundsExtents,
            float seedRadius)
        {
            return Math.Abs(boundsExtents.Y) > Math.Max(0f, seedRadius) * 2f;
        }

        // Bounded local-space offsets for the lower intermediate layer of a tall trigger. A centre
        // point plus quarter-width points along each horizontal axis covers the interior of a large
        // portal volume without turning approach discovery into a grid scan. The quarter-width choice
        // is evidence-driven by the live Hidden geometry: its previously proven approach sits about
        // 8.8m from centre while the box half-width is ~33.8m (roughly one quarter).
        internal static Point3[] LowerIntermediateInteriorOffsets()
        {
            return new[]
            {
                new Point3(0f, -0.5f, 0f),
                new Point3(0.25f, -0.5f, 0f), new Point3(-0.25f, -0.5f, 0f),
                new Point3(0f, -0.5f, 0.25f), new Point3(0f, -0.5f, -0.25f)
            };
        }

        // Second-stage fallback used only after every primary seed for a large/tall trigger has failed
        // NavMesh.SamplePosition. 0.6.13 proved the vertical layer itself was correct but its five-point
        // axis cross was too sparse inside a very large rotated box: the historically live approach is
        // only a few metres from an 8m inner ring, while it is >4m from every centre/quarter-axis sample.
        //
        // The ring radius is expressed in WORLD metres so a non-uniformly scaled BoxCollider does not
        // turn a "small" normalized offset into tens of metres on one axis. The returned offsets are
        // normalized by the caller-supplied world half-axis lengths and remain inside the box. Small
        // triggers return no points because the existing centre sample sphere already covers the same
        // area; this is deliberately a zero-sample fallback, not a routine area scan.
        internal static Point3[] LowerIntermediateFallbackRingOffsets(float halfWorldX, float halfWorldZ,
            float sampleRadius, int steps)
        {
            float hx = Math.Abs(halfWorldX);
            float hz = Math.Abs(halfWorldZ);
            float radius = Math.Max(0f, sampleRadius);
            if (hx <= 0.0001f || hz <= 0.0001f || radius <= 0f) return new Point3[0];

            float ringRadius = Math.Min(radius * 2f, Math.Min(hx, hz) * 0.25f);
            // If the ring lies inside the already-tested centre sphere, it adds no search coverage.
            if (ringRadius <= radius + 0.001f) return new Point3[0];

            if (steps < 4) steps = 4;
            if (steps > 8) steps = 8;
            Point3[] result = new Point3[steps];
            for (int i = 0; i < steps; i++)
            {
                double angle = (Math.PI * 2.0 * i) / steps;
                float worldX = (float)Math.Cos(angle) * ringRadius;
                float worldZ = (float)Math.Sin(angle) * ringRadius;
                result[i] = new Point3(worldX / hx, -0.5f, worldZ / hz);
            }
            return result;
        }

        // Three bounded samples on the horizontal box face that the current route start most strongly
        // approaches, all at the same lower-intermediate height. This covers the combination the live
        // failure was missing: lateral surface position + intermediate Y. The face is selected in the
        // collider's own local space by normalized distance, so a long/thin or rotated trigger does not
        // accidentally choose an axis merely because its raw world AABB is wider on that axis.
        // Returns normalized local offsets, to be scaled by the caller's authoritative BoxCollider
        // half-extents.
        internal static Point3[] LowerIntermediateApproachFaceOffsets(Point3 localStartRelative, Point3 halfExtents)
        {
            float hx = Math.Max(0.0001f, Math.Abs(halfExtents.X));
            float hz = Math.Max(0.0001f, Math.Abs(halfExtents.Z));
            float nx = Math.Abs(localStartRelative.X) / hx;
            float nz = Math.Abs(localStartRelative.Z) / hz;
            if (nx >= nz)
            {
                float sx = localStartRelative.X < 0f ? -1f : 1f;
                return new[]
                {
                    new Point3(sx, -0.5f, 0f),
                    new Point3(sx, -0.5f, 0.25f),
                    new Point3(sx, -0.5f, -0.25f)
                };
            }

            float sz = localStartRelative.Z < 0f ? -1f : 1f;
            return new[]
            {
                new Point3(0f, -0.5f, sz),
                new Point3(0.25f, -0.5f, sz),
                new Point3(-0.25f, -0.5f, sz)
            };
        }

        // ---- Hidden -> Duskenlight: oriented geometry and vertical probing -----------------------
        //
        // Live evidence for crossing duskenlight|zoneline (1), rawPos=(282.93, 18.27, -158.88):
        // 14 seeds generated, only 2 produced a NavMesh sample at all, and BOTH resolved ~40m from
        // the verified crossing and were correctly rejected. Two facts follow directly:
        //
        //  * 12 of 14 seeds found NO NavMesh within their 3-8m sample radius, so there is no
        //    walkable ground within a few metres of the trigger's transform origin, its axis-aligned
        //    bounds centre, its AABB floor, or the four 4m cardinal offsets. Widening the radius is
        //    not the answer - that is exactly what produced the 40m endpoints.
        //  * A sample cannot land 40m from its own seed when the largest radius in use is 8m, so the
        //    two that did sample must have come from seeds that were ALREADY far from the crossing.
        //    The only seeds that can be far away are the AABB floor CORNERS: for a large or rotated
        //    trigger the axis-aligned bounds corners lie well outside the trigger volume itself.
        //
        // So the existing seed set is looking in the wrong places for this crossing: near-trigger
        // seeds are all clustered on one axis-aligned point cloud, while the far seeds are AABB
        // artefacts rather than real trigger geometry. The two additions below are deliberately
        // ORIENTED (they use the collider's own local-to-world basis, so rotation and scale are
        // honoured) and VERTICAL (they probe the trigger's own height range, so a trigger whose
        // origin sits above or below walkable ground is still reachable).
        //
        // Nothing here relaxes acceptance: every generated point still has to pass
        // NavMesh.SamplePosition, then NavMesh.CalculatePath, then the unchanged RouteCandidatePolicy
        // proximity rules. A 40m endpoint stays rejected.

        // Face centres of the ORIENTED box, expressed as local-space offsets scaled by the box's own
        // half-extents and then transformed by the caller. Returns the four horizontal faces (the
        // plausible entrances) plus the bottom face. The oriented face centre of a rotated trigger is
        // inside/adjacent to the real volume, unlike an axis-aligned bounds corner.
        internal static Point3[] OrientedFaceOffsets()
        {
            return new[]
            {
                new Point3(1f, 0f, 0f), new Point3(-1f, 0f, 0f),
                new Point3(0f, 0f, 1f), new Point3(0f, 0f, -1f),
                new Point3(0f, -1f, 0f)
            };
        }

        // Evenly spaced heights across the trigger's own vertical extent, expressed as local-space Y
        // offsets in [-1, +1] of the half-extent. Used to probe for walkable ground when the
        // trigger's origin height is not the height the floor actually sits at. Bounded to a small
        // fixed count so this stays a handful of extra samples, not a scan.
        internal static float[] VerticalProbeOffsets(int steps)
        {
            if (steps < 2) steps = 2;
            if (steps > 5) steps = 5;
            float[] result = new float[steps];
            for (int i = 0; i < steps; i++)
                result[i] = -1f + 2f * (i / (float)(steps - 1));
            return result;
        }

        // A seed is only worth spending sample budget on if it could still yield an approach the
        // existing acceptance policy would take. Seeds farther from the verified crossing than the
        // acceptance distance can only ever produce a rejected endpoint (the live 40m case), so
        // filtering them out STRENGTHENS the search: it stops AABB corner artefacts from consuming
        // the seed/sample budget that near-trigger oriented seeds need.
        //
        // CRITICAL: `seedDistanceToCrossing` must be measured the SAME way acceptance measures it -
        // as a distance to the verified crossing VOLUME (see SeedIsWorthSamplingNearVolume). The
        // "can only ever produce a rejected endpoint" argument above is a statement about the
        // acceptance metric, and it is only sound while both use one metric. Feeding this a distance
        // to the crossing's raw transform point instead is what caused the 0.6.11 large-trigger
        // regression documented on SeedIsWorthSamplingNearVolume.
        internal static bool SeedIsWorthSampling(float seedDistanceToCrossing, float maxAcceptedApproachDistance,
            float sampleRadius)
        {
            if (maxAcceptedApproachDistance <= 0f) return true;
            return seedDistanceToCrossing <= maxAcceptedApproachDistance + Math.Max(0f, sampleRadius);
        }

        // ---- Large-trigger seed retention (0.6.12) ------------------------------------------------
        //
        // Live regression, crossing hidden|zoneline rawPos=(223.374, 61.400, 117.582):
        // generatedSeeds=16, samples=0, accepted=0 - yet this same crossing was traversed by an
        // earlier candidate via an approach around (232.14, 50.06, 116.71). The trigger is a large
        // BoxCollider: center=(223.37, 61.40, 117.58), size=(67.50, 47.11, 59.35).
        //
        // The 0.6.10 proximity filter measured "distance from the crossing" as the horizontal
        // distance to the crossing's RAW TRANSFORM POINT, then compared it against
        // acceptance (8m) + sampleRadius (4m) = 12m. For this collider that is mathematically
        // unsafe, because half of its own footprint lies beyond that threshold:
        //
        //   * AABB floor corners sit sqrt(33.75^2 + 29.68^2) ~= 44.9m from the raw point - filtered,
        //     though every one of them is ON the verified trigger volume.
        //   * Oriented +/-X face centres sit 33.75m out and +/-Z face centres 29.68m out - filtered,
        //     though all four are ON the verified trigger volume.
        //
        // What survived were only the seeds sitting on the vertical line through the collider centre
        // (the floor-face centre, the bottom oriented face, and the three vertical probes, all at
        // horizontal distance 0) plus the raw/cardinal seeds clustered within a few metres of it. So
        // all 16 generated seeds collapsed onto one narrow column inside a 67m x 59m volume, and the
        // formerly-proven approach ~8.8m away laterally was outside every surviving seed's radius.
        // The seed class was generated and then filtered - not missing, and not a genuine absence of
        // NavMesh.
        //
        // The repair is to measure candidate relevance against the verified collider VOLUME, which is
        // precisely what LocalZoneRoutePlanner.DistanceToCrossing (the acceptance metric) already
        // does via Collider.ClosestPoint. A seed INSIDE the trigger volume has an acceptance distance
        // of 0 and must never be discarded for being "far from centre"; a seed outside is judged by
        // its distance to the volume's surface, so a genuinely remote AABB artefact beside a small or
        // rotated trigger stays filtered exactly as 0.6.10 intended.
        //
        // This does not weaken acceptance. Every retained seed still has to pass
        // NavMesh.SamplePosition, then NavMesh.CalculatePath, then the unchanged RouteCandidatePolicy
        // rules - a 40m endpoint is still rejected downstream.
        internal static bool SeedIsWorthSamplingNearVolume(float seedDistanceToCrossingVolume,
            bool seedInsideCrossingVolume, float maxAcceptedApproachDistance, float sampleRadius)
        {
            // Inside the verified trigger volume the acceptance distance is 0 by definition, so such a
            // seed can always still produce an accepted approach regardless of the volume's size.
            if (seedInsideCrossingVolume) return true;
            return SeedIsWorthSampling(seedDistanceToCrossingVolume, maxAcceptedApproachDistance, sampleRadius);
        }

        // Exact distance from a point to an oriented box, with the point already expressed in the
        // box's own local space relative to its centre and the half-extents in those same local
        // units. Returns 0 inside the box. Rotation and scale are therefore honoured by construction:
        // the caller does the basis change, this stays pure arithmetic. Used as the fallback when a
        // live Collider.ClosestPoint is unavailable, and as the testable definition of "distance to
        // the volume rather than to the centre".
        internal static float LocalBoxSurfaceDistance(Point3 localPoint, Point3 halfExtents)
        {
            float dx = Math.Abs(localPoint.X) - Math.Abs(halfExtents.X);
            float dy = Math.Abs(localPoint.Y) - Math.Abs(halfExtents.Y);
            float dz = Math.Abs(localPoint.Z) - Math.Abs(halfExtents.Z);
            if (dx < 0f) dx = 0f;
            if (dy < 0f) dy = 0f;
            if (dz < 0f) dz = 0f;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // True when the point lies within the oriented box (inclusive of its surface), in the same
        // local space LocalBoxSurfaceDistance expects.
        internal static bool IsInsideLocalBox(Point3 localPoint, Point3 halfExtents)
        {
            return Math.Abs(localPoint.X) <= Math.Abs(halfExtents.X)
                && Math.Abs(localPoint.Y) <= Math.Abs(halfExtents.Y)
                && Math.Abs(localPoint.Z) <= Math.Abs(halfExtents.Z);
        }

        // Straight-line distance between two points. Kept here so the Unity-free tests can express
        // "distance to the raw centre" - the metric this pass replaces - without a UnityEngine type.
        internal static float Distance(Point3 a, Point3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Test-only stand-in for "would NavMesh.SamplePosition succeed here": Unity's real call performs
        // a true 3D sphere search against the baked mesh, which for a locally flat floor reduces to "is
        // this point within `radius` of the floor's height". This lets a deterministic test prove the
        // seeding shape (transform/center seed too high, floor seed within reach) without a live NavMesh.
        internal static bool WithinVerticalReachOfGround(float pointY, float groundY, float radius)
        {
            return Math.Abs(pointY - groundY) <= Math.Max(0f, radius);
        }
    }
}
