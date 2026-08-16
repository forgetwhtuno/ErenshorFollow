using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorFollow
{
    // Event-boundary local planner for verified Zonelines. It never invents a destination and never runs
    // from Update: callers build a bounded plan at leg start/resume or from the explicit diagnostic command.
    internal static class LocalZoneRoutePlanner
    {
        internal sealed class RouteOption
        {
            internal readonly Zoneline Crossing;
            internal readonly Vector3 Approach;
            internal readonly RouteCandidatePolicy.Evaluation Evaluation;
            internal readonly string StableKey;
            internal readonly Vector3[] PathCorners;

            internal RouteOption(Zoneline crossing, Vector3 approach, RouteCandidatePolicy.Evaluation evaluation,
                string stableKey, Vector3[] pathCorners)
            {
                Crossing = crossing;
                Approach = approach;
                Evaluation = evaluation;
                StableKey = stableKey;
                PathCorners = pathCorners == null ? new Vector3[0] : pathCorners;
            }

            internal bool NeedsNativeProof { get { return Evaluation != null && Evaluation.NeedsNativeProof; } }
        }


        internal sealed class CrossingTraversalOption
        {
            internal readonly Vector3 Target;
            internal readonly string StableKey;
            internal readonly string TriggerType;
            internal readonly NavMeshPathStatus PathStatus;
            internal readonly float RouteLength;
            internal readonly float StartDistanceToTrigger;

            internal CrossingTraversalOption(Vector3 target, string stableKey, string triggerType,
                NavMeshPathStatus pathStatus, float routeLength, float startDistanceToTrigger)
            {
                Target = target;
                StableKey = stableKey;
                TriggerType = triggerType;
                PathStatus = pathStatus;
                RouteLength = routeLength;
                StartDistanceToTrigger = startDistanceToTrigger;
            }
        }

        internal sealed class CrossingInspection
        {
            internal Zoneline Crossing;
            internal string StableKey;
            internal bool Active;
            internal bool RemoveParty;
            internal Vector3 TransformPosition;
            internal readonly List<string> ColliderInfo = new List<string>();
            internal int SampledApproachCount;
            internal readonly List<RouteCandidatePolicy.Evaluation> Evaluations = new List<RouteCandidatePolicy.Evaluation>();
            internal readonly List<RouteOption> AcceptedOptions = new List<RouteOption>();
            internal RouteOption BestAccepted;
        }

        internal sealed class Plan
        {
            internal readonly List<RouteOption> Options = new List<RouteOption>();
            internal readonly List<CrossingInspection> Crossings = new List<CrossingInspection>();
            internal bool StartSampled;
            internal Vector3 StartSamplePosition;
        }

        private struct Seed
        {
            internal Vector3 Position;
            internal float Radius;
            internal Seed(Vector3 position, float radius) { Position = position; Radius = radius; }
        }

        private sealed class OptionComparer : IComparer<RouteOption>
        {
            internal static readonly OptionComparer Instance = new OptionComparer();
            public int Compare(RouteOption x, RouteOption y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return 1;
                if (y == null) return -1;
                int policy = RouteCandidatePolicy.CompareEvaluations(x.Evaluation, y.Evaluation);
                return policy != 0 ? policy : string.Compare(x.StableKey, y.StableKey, StringComparison.Ordinal);
            }
        }

        private static readonly NavMeshPath ProbePath = new NavMeshPath();
        private const int MaxCollidersPerCrossing = 4;
        private const int MaxSeedsPerCrossing = 14;
        private const int MaxApproachesPerCrossing = 6;
        private const float ApproachDedupDistance = 0.75f;

        internal static Plan Build(Vector3 start, IList<Zoneline> crossings)
        {
            Plan plan = new Plan();
            NavMeshHit startHit;
            plan.StartSampled = NavMesh.SamplePosition(start, out startHit, 5f, NavMesh.AllAreas);
            plan.StartSamplePosition = plan.StartSampled ? startHit.position : start;

            List<Zoneline> ordered = new List<Zoneline>();
            if (crossings != null)
            {
                for (int i = 0; i < crossings.Count; i++)
                    if (crossings[i] != null) ordered.Add(crossings[i]);
            }
            ordered.Sort(delegate(Zoneline a, Zoneline b)
            {
                return string.Compare(CrossingKey(a), CrossingKey(b), StringComparison.Ordinal);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                CrossingInspection inspection = InspectCrossing(start, plan.StartSampled, plan.StartSamplePosition, ordered[i], i);
                plan.Crossings.Add(inspection);
                if (inspection.BestAccepted == null) continue;

                // Keep every accepted approach, not merely each crossing's best, so a runtime stall can
                // advance to another bounded approach before giving up on a real exit.
                plan.Options.AddRange(inspection.AcceptedOptions);
            }

            plan.Options.Sort(OptionComparer.Instance);
            return plan;
        }

        // Bounded diagnostic summary for a route-readiness boundary.  This consumes the same live
        // observations used to plan; it never carries a crossing or a NavMesh point across a scene.
        internal static string DescribeReadiness(Plan plan)
        {
            if (plan == null) return "plan=missing";
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("startSampled=").Append(plan.StartSampled)
                .Append(" accepted=").Append(plan.Options.Count)
                .Append(" crossings=").Append(plan.Crossings.Count);
            for (int i = 0; i < plan.Crossings.Count; i++)
            {
                CrossingInspection crossing = plan.Crossings[i];
                if (crossing == null) continue;
                text.Append(" | ").Append(crossing.StableKey)
                    .Append(" active=").Append(crossing.Active)
                    .Append(" removeParty=").Append(crossing.RemoveParty)
                    .Append(" colliders=").Append(crossing.ColliderInfo.Count)
                    .Append(" samples=").Append(crossing.SampledApproachCount)
                    .Append(" accepted=").Append(crossing.AcceptedOptions.Count);
                for (int j = 0; j < crossing.Evaluations.Count; j++)
                {
                    RouteCandidatePolicy.Evaluation evaluation = crossing.Evaluations[j];
                    if (evaluation == null || evaluation.Candidate == null) continue;
                    text.Append(" [").Append(evaluation.Candidate.StableKey)
                        .Append(" path=").Append(evaluation.Candidate.Path)
                        .Append(" corners=").Append(evaluation.Candidate.CornerCount)
                        .Append(" result=").Append(evaluation.Acceptance)
                        .Append(" reason=").Append(evaluation.Reason).Append("]");
                }
            }
            return text.ToString();
        }

        private static CrossingInspection InspectCrossing(Vector3 start, bool startSampled, Vector3 sampledStart, Zoneline crossing, int crossingIndex)
        {
            CrossingInspection inspection = new CrossingInspection();
            inspection.Crossing = crossing;
            inspection.StableKey = CrossingKey(crossing);
            inspection.Active = IsActive(crossing);
            inspection.RemoveParty = crossing != null && crossing.RemoveParty;
            inspection.TransformPosition = crossing == null ? Vector3.zero : crossing.transform.position;
            DescribeColliders(crossing, inspection.ColliderInfo);

            List<Vector3> approaches = SampleApproaches(crossing, inspection.TransformPosition, start);
            inspection.SampledApproachCount = approaches.Count;
            for (int i = 0; i < approaches.Count; i++)
            {
                string key = inspection.StableKey + "/a" + i.ToString(CultureInfo.InvariantCulture);
                Vector3[] pathCorners;
                RouteCandidatePolicy.Candidate candidate = MeasureCandidate(start, startSampled, sampledStart,
                    crossing, approaches[i], key, out pathCorners);
                RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
                inspection.Evaluations.Add(evaluation);
                if (!evaluation.Accepted) continue;
                RouteOption option = new RouteOption(crossing, approaches[i], evaluation, key, pathCorners);
                inspection.AcceptedOptions.Add(option);
                if (inspection.BestAccepted == null || OptionComparer.Instance.Compare(option, inspection.BestAccepted) < 0)
                    inspection.BestAccepted = option;
            }
            return inspection;
        }

        private static RouteCandidatePolicy.Candidate MeasureCandidate(Vector3 rawStart, bool startSampled, Vector3 sampledStart,
            Zoneline crossing, Vector3 approach, string stableKey, out Vector3[] pathCorners)
        {
            pathCorners = new Vector3[0];
            RouteCandidatePolicy.Candidate candidate = new RouteCandidatePolicy.Candidate();
            candidate.StableKey = stableKey;
            candidate.Active = IsActive(crossing);
            candidate.RemoveParty = crossing != null && crossing.RemoveParty;
            candidate.Sampled = true;
            candidate.ApproachDistanceToCrossing = DistanceToCrossing(approach, crossing);
            candidate.StartDistanceToCrossing = DistanceToCrossing(startSampled ? sampledStart : rawStart, crossing);
            candidate.EndpointDistanceToCrossing = candidate.StartDistanceToCrossing;
            candidate.RouteLength = float.MaxValue;
            candidate.Path = RouteCandidatePolicy.PathKind.Invalid;
            candidate.CornerCount = 0;

            if (!startSampled) return candidate;
            try
            {
                if (!NavMesh.CalculatePath(sampledStart, approach, NavMesh.AllAreas, ProbePath) ||
                    ProbePath.status == NavMeshPathStatus.PathInvalid || ProbePath.corners == null || ProbePath.corners.Length < 2)
                    return candidate;

                candidate.CornerCount = ProbePath.corners.Length;
                candidate.Path = ProbePath.status == NavMeshPathStatus.PathComplete
                    ? RouteCandidatePolicy.PathKind.Complete
                    : RouteCandidatePolicy.PathKind.Partial;
                Vector3 endpoint = ProbePath.corners[ProbePath.corners.Length - 1];
                candidate.EndpointDistanceToCrossing = DistanceToCrossing(endpoint, crossing);
                candidate.RouteLength = RouteLength(ProbePath.corners);
                pathCorners = (Vector3[])ProbePath.corners.Clone();
                return candidate;
            }
            catch
            {
                return candidate;
            }
        }

        private static List<Vector3> SampleApproaches(Zoneline crossing, Vector3 crossingPosition, Vector3 start)
        {
            List<Seed> seeds = new List<Seed>(MaxSeedsPerCrossing);
            AddSeed(seeds, crossingPosition, 8f);

            Vector3 towardStart = start - crossingPosition;
            towardStart.y = 0f;
            if (towardStart.sqrMagnitude > 0.01f)
            {
                towardStart.Normalize();
                AddSeed(seeds, crossingPosition + towardStart * 2.5f, 3.5f);
                AddSeed(seeds, crossingPosition + towardStart * 5f, 3.5f);
            }

            Collider[] colliders = GetColliders(crossing);
            int colliderLimit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < colliderLimit && seeds.Count < MaxSeedsPerCrossing; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                Bounds bounds = collider.bounds;
                AddSeed(seeds, bounds.center, 4f);
                AddSeed(seeds, ClosestPoint(collider, start), 3f);
            }

            Vector3[] around =
            {
                new Vector3(4f, 0f, 0f), new Vector3(-4f, 0f, 0f),
                new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, -4f)
            };
            for (int i = 0; i < around.Length && seeds.Count < MaxSeedsPerCrossing; i++)
                AddSeed(seeds, crossingPosition + around[i], 3f);

            List<Vector3> sampled = new List<Vector3>(MaxApproachesPerCrossing);
            for (int i = 0; i < seeds.Count && sampled.Count < MaxApproachesPerCrossing; i++)
            {
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(seeds[i].Position, out hit, seeds[i].Radius, NavMesh.AllAreas)) continue;
                bool duplicate = false;
                for (int j = 0; j < sampled.Count; j++)
                {
                    if (HorizontalDistance(sampled[j], hit.position) <= ApproachDedupDistance)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) sampled.Add(hit.position);
            }
            return sampled;
        }

        private static void AddSeed(List<Seed> seeds, Vector3 position, float radius)
        {
            if (seeds.Count >= MaxSeedsPerCrossing) return;
            seeds.Add(new Seed(position, radius));
        }

        private static Collider[] GetColliders(Zoneline crossing)
        {
            if (crossing == null || crossing.gameObject == null) return new Collider[0];
            try
            {
                Collider[] all = crossing.GetComponentsInChildren<Collider>(true) ?? new Collider[0];
                List<Collider> triggers = new List<Collider>();
                for (int i = 0; i < all.Length; i++)
                {
                    Collider collider = all[i];
                    // Zoneline activation is trigger-driven. Solid child colliders may belong to nearby
                    // rocks, walls, or decorative geometry and can have bounds extending back toward the
                    // party. Treating those bounds as the crossing made a point beside the Faerie's Brake
                    // rock look like a valid Azure approach even though the real trigger was ~45m away.
                    if (collider == null || !collider.enabled || !collider.isTrigger ||
                        collider.gameObject == null || !collider.gameObject.activeInHierarchy) continue;
                    triggers.Add(collider);
                }
                return triggers.ToArray();
            }
            catch { return new Collider[0]; }
        }

        internal static float DistanceToCrossing(Vector3 point, Zoneline crossing)
        {
            if (crossing == null || crossing.gameObject == null) return float.MaxValue;
            float best = HorizontalDistance(point, crossing.transform.position);
            Collider[] colliders = GetColliders(crossing);
            int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                Vector3 nearest = ClosestPoint(collider, point);
                float distance = HorizontalDistance(point, nearest);
                if (distance < best) best = distance;
            }
            return best;
        }

        internal static List<CrossingTraversalOption> BuildCrossingTraversalTargets(Vector3 start, Zoneline crossing)
        {
            List<CrossingTraversalOption> options = new List<CrossingTraversalOption>();
            if (crossing == null || crossing.gameObject == null) return options;

            NavMeshHit startHit;
            if (!NavMesh.SamplePosition(start, out startHit, 4f, NavMesh.AllAreas)) return options;

            Collider[] colliders = GetColliders(crossing);
            List<Collider> ordered = new List<Collider>(colliders);
            ordered.Sort(delegate(Collider a, Collider b)
            {
                return DistanceToCollider(start, a).CompareTo(DistanceToCollider(start, b));
            });

            for (int i = 0; i < ordered.Count && options.Count < 6; i++)
            {
                Collider collider = ordered[i];
                if (collider == null) continue;
                Bounds bounds = collider.bounds;
                Vector3 direction = bounds.center - startHit.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = crossing.transform.position - startHit.position;
                    direction.y = 0f;
                }
                if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                direction.Normalize();

                float projectedExtent = Math.Abs(direction.x) * bounds.extents.x + Math.Abs(direction.z) * bounds.extents.z;
                Vector3 closest = ClosestPoint(collider, startHit.position);
                Vector3[] rawTargets =
                {
                    bounds.center + direction * (projectedExtent + 1.5f),
                    bounds.center,
                    closest + direction * Math.Max(1.0f, Math.Min(2.0f, projectedExtent * 0.5f + 0.75f))
                };

                for (int t = 0; t < rawTargets.Length && options.Count < 6; t++)
                    TryAddCrossingTraversalTarget(options, startHit.position, collider, rawTargets[t],
                        CrossingKey(crossing) + "/trigger" + i + "/t" + t);
            }

            options.Sort(delegate(CrossingTraversalOption a, CrossingTraversalOption b)
            {
                int status = CrossingPathRank(a.PathStatus).CompareTo(CrossingPathRank(b.PathStatus));
                if (status != 0) return status;
                int route = a.RouteLength.CompareTo(b.RouteLength);
                return route != 0 ? route : string.Compare(a.StableKey, b.StableKey, StringComparison.Ordinal);
            });
            return options;
        }

        private static void TryAddCrossingTraversalTarget(List<CrossingTraversalOption> options, Vector3 start,
            Collider collider, Vector3 rawTarget, string stableKey)
        {
            NavMeshHit targetHit;
            if (!NavMesh.SamplePosition(rawTarget, out targetHit, 2.5f, NavMesh.AllAreas)) return;
            for (int i = 0; i < options.Count; i++)
                if (HorizontalDistance(options[i].Target, targetHit.position) <= ApproachDedupDistance) return;

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, targetHit.position, NavMesh.AllAreas, path) ||
                path.status == NavMeshPathStatus.PathInvalid || path.corners == null || path.corners.Length < 2) return;
            if (!PathIntersectsTrigger(path.corners, collider)) return;

            options.Add(new CrossingTraversalOption(targetHit.position, stableKey, collider.GetType().Name,
                path.status, RouteLength(path.corners), DistanceToCollider(start, collider)));
        }

        private static bool PathIntersectsTrigger(Vector3[] corners, Collider collider)
        {
            if (collider == null || corners == null || corners.Length == 0) return false;
            if (PointInsideCollider(collider, corners[0])) return true;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 start = corners[i - 1];
                Vector3 delta = corners[i] - start;
                float distance = delta.magnitude;
                if (distance <= 0.001f) continue;
                RaycastHit hit;
                try
                {
                    if (collider.Raycast(new Ray(start, delta / distance), out hit, distance + 0.05f)) return true;
                }
                catch { }
                if (PointInsideCollider(collider, corners[i])) return true;
            }
            return false;
        }

        private static bool PointInsideCollider(Collider collider, Vector3 point)
        {
            if (collider == null) return false;
            Vector3 closest = ClosestPoint(collider, point);
            return Vector3.Distance(closest, point) <= 0.05f;
        }

        private static Vector3 ClosestPoint(Collider collider, Vector3 point)
        {
            if (collider == null) return point;
            try { return collider.ClosestPoint(point); }
            catch { return collider.bounds.ClosestPoint(point); }
        }

        private static float DistanceToCollider(Vector3 point, Collider collider)
        {
            if (collider == null) return float.MaxValue;
            return HorizontalDistance(point, ClosestPoint(collider, point));
        }

        private static int CrossingPathRank(NavMeshPathStatus status)
        {
            return status == NavMeshPathStatus.PathComplete ? 0 : (status == NavMeshPathStatus.PathPartial ? 1 : 2);
        }

        internal static string CrossingKey(Zoneline crossing)
        {
            if (crossing == null) return "<null>";
            Vector3 p = crossing.transform.position;
            string name = crossing.gameObject == null ? string.Empty : crossing.gameObject.name;
            string destination = string.IsNullOrWhiteSpace(crossing.DestinationZone) ? string.Empty : crossing.DestinationZone.Trim();
            return destination.ToLowerInvariant() + "|" + name.ToLowerInvariant() + "|" +
                   p.x.ToString("F3", CultureInfo.InvariantCulture) + "," +
                   p.y.ToString("F3", CultureInfo.InvariantCulture) + "," +
                   p.z.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static void DescribeColliders(Zoneline crossing, List<string> into)
        {
            Collider[] colliders = GetColliders(crossing);
            if (colliders.Length == 0)
            {
                into.Add("none");
                return;
            }
            int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                Bounds b = collider.bounds;
                into.Add(collider.GetType().Name + " enabled=" + collider.enabled + " trigger=" + collider.isTrigger +
                    " center=" + FormatVector(b.center) + " size=" + FormatVector(b.size));
            }
            if (colliders.Length > limit) into.Add("+" + (colliders.Length - limit) + " more collider(s)");
        }

        internal static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                   value.y.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                   value.z.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }

        private static bool IsActive(Zoneline crossing)
        {
            return crossing != null && crossing.gameObject != null && crossing.gameObject.activeInHierarchy;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float RouteLength(Vector3[] corners)
        {
            if (corners == null || corners.Length < 2) return 0f;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += HorizontalDistance(corners[i - 1], corners[i]);
            return length;
        }
    }
}
