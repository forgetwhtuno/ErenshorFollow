using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
            // How many seed points were actually constructed (transform/collider-center/closest-point/
            // floor-face/cardinal-offset) before NavMesh.SamplePosition was tried on each. Distinguishes
            // "we looked in 0 places" from "we looked in N places and NavMesh.SamplePosition failed at
            // every one" for the next crossing that produces zero samples.
            internal int GeneratedSeedCount;
            // Seeds considered but discarded before sampling, and the per-seed record behind both
            // counts. See SeedDiagnostic.
            internal int FilteredSeedCount;
            internal readonly List<SeedDiagnostic> SeedDiagnostics = new List<SeedDiagnostic>();
            internal readonly List<RouteCandidatePolicy.Evaluation> Evaluations = new List<RouteCandidatePolicy.Evaluation>();
            // Parallel to Evaluations: the exact world position each evaluated candidate was sampled at.
            // Kept separate from RouteCandidatePolicy.Candidate (which stays Unity-free) so a rejected
            // candidate's position is still available for diagnostics, not only an accepted one's.
            internal readonly List<Vector3> EvaluationApproaches = new List<Vector3>();
            internal readonly List<string> ApproachSeedLabels = new List<string>();
            internal readonly List<RouteOption> AcceptedOptions = new List<RouteOption>();
            internal RouteOption BestAccepted;
            // 0.6.18 second-stage forensics. Populated only when the bounded route-facing entrance
            // probe actually runs (a large trigger whose only accepted approach is quality-poor), so
            // an event-boundary diagnostic can prove whether that stage generated points, where, and
            // what NavMesh/CalculatePath said about each. Never per frame; bounded by the probe cap.
            internal readonly List<RouteFacingProbeDiagnostic> RouteFacingProbes = new List<RouteFacingProbeDiagnostic>();
            internal int PrimarySeedCount;
            internal int PrimarySampleCount;
            internal bool FallbackTriggered;
            internal int FallbackSeedsAdded;
            internal int FallbackSampleCount;
            internal string LocalHalf = string.Empty;
            internal string WorldHalf = string.Empty;
            internal bool IntermediateEnabled;
            internal int MidApproachGenerated;
            // Authoritative live BoxCollider record (local centre/size, lossyScale, rotation, oriented
            // world half axes, and the route start expressed in the collider's own local space) taken
            // at route-build time from the collider itself, never from any hardcoded zone knowledge.
            internal readonly List<string> RouteFacingGeometry = new List<string>();
            internal string RouteFacingQualityReference = string.Empty;
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
            internal string Label;
            internal Seed(Vector3 position, float radius, string label)
            {
                Position = position;
                Radius = radius;
                Label = label;
            }
        }

        private struct ApproachQualityReference
        {
            internal bool Valid;
            internal Vector3 Position;
            internal Vector3 Tangent;
            internal string Label;
        }

        // One bounded record per generated-or-rejected seed for a single route-build attempt. This is
        // what proves WHY a crossing produced zero samples: whether a useful seed class was never
        // generated, was generated then filtered, or survived and genuinely failed
        // NavMesh.SamplePosition. Built only during a route build (never per frame) and capped by the
        // same MaxSeedsPerCrossing budget as the seeds themselves.
        internal sealed class SeedDiagnostic
        {
            internal int Index;
            internal string Label;
            internal Vector3 Position;
            internal float Radius;
            internal float DistanceToRawCenter;
            internal float DistanceToColliderVolume;
            internal bool InsideCollider;
            internal bool Kept;
            internal string FilterReason;
            internal bool Sampled;
            internal Vector3 SampleHit;
        }

        // One bounded record per SECOND-STAGE route-facing entrance probe. The 0.6.17 live Vitheo
        // failure could not be diagnosed because this stage produced no record at all: the crossing
        // reported one sample and one accepted candidate, and nothing said whether the route-facing
        // probes had run, where they were placed, or whether NavMesh.SamplePosition refused them.
        internal sealed class RouteFacingProbeDiagnostic
        {
            internal int Index;
            internal string Label;
            // The point as CONSTRUCTED, before NavMesh.SamplePosition - this is the value that proves
            // where the generator actually looked, which a sampled hit alone cannot.
            internal Vector3 UnsampledPosition;
            internal float Radius;
            internal Vector3 LocalNormalizedOffset;
            internal float WorldY;
            internal float DistanceToColliderVolume;
            internal bool InsideCollider;
            internal string QualityReferenceLabel;
            internal bool Sampled;
            internal Vector3 SampleHit;
            internal bool Duplicate;
            internal bool Evaluated;
            internal string PathStatus = "notEvaluated";
            internal string Acceptance = "notEvaluated";
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
        // Raised from 14 to make room for the floor-face seeds below (up to 5 per collider) without
        // starving the pre-existing transform/center/closest-point seeds. Still a fixed, small, one-time
        // cost per route-build call - never a per-frame or unbounded scan.
        // Raised again in 0.6.12: repairing the centre-based proximity filter (see
        // CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume) legitimately retains the oriented
        // face and floor-corner seeds of a LARGE trigger, which previously were all discarded. Still a
        // fixed, small, one-time cost per route-build call - the budget is not removed, only sized so
        // a big trigger's own faces cannot starve the proven cardinal/raw seeds behind them.
        // 0.6.14 reserves eight additional slots for a SECOND-STAGE inner ring that is generated only
        // when all 30 primary large-trigger probes produce zero NavMesh samples. Working crossings pay
        // no extra SamplePosition cost.
        // Historical discovery shape: 30 primary seeds plus eight reserved zero-sample fallback
        // slots. Center preference belongs in ranking/probes, never by consuming a primary slot.
        internal const int MaxSeedsPerCrossing = CrossingSeedBudgetPolicy.MaxSeedsPerCrossing;
        internal const int PrimarySeedBudget = CrossingSeedBudgetPolicy.PrimarySeedBudget;
        internal const int ZeroSampleFallbackBudget = CrossingSeedBudgetPolicy.ZeroSampleFallbackBudget;
        private const int MaxApproachesPerCrossing = 6;
        private const float ApproachDedupDistance = 0.75f;
        private const float FloorSeedRadius = 4f;

        internal static Plan Build(Vector3 start, IList<Zoneline> crossings)
        {
            return Build(start, crossings, ErenshorFollowPlugin.VerboseDiagnostics);
        }

        // Explicit diagnostics (for /elead diag) can request the per-seed forensic records even when
        // normal verbose logging is off. Ordinary expedition planning does not allocate one diagnostic
        // object per seed unless the user explicitly enabled Verbose diagnostics.
        internal static Plan Build(Vector3 start, IList<Zoneline> crossings, bool includeSeedDiagnostics)
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
                CrossingInspection inspection = InspectCrossing(start, plan.StartSampled, plan.StartSamplePosition, ordered[i], i, includeSeedDiagnostics);
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
                // Raw crossing transform position: proves whether the atlas/native crossing coordinate
                // itself is where the physical zoneline actually sits, independent of NavMesh sampling.
                text.Append(" | ").Append(crossing.StableKey)
                    .Append(" rawPos=").Append(FormatVector(crossing.TransformPosition))
                    .Append(" active=").Append(crossing.Active)
                    .Append(" removeParty=").Append(crossing.RemoveParty)
                    .Append(" colliders=").Append(crossing.ColliderInfo.Count)
                    .Append(" generatedSeeds=").Append(crossing.GeneratedSeedCount)
                    .Append(" primarySeeds=").Append(crossing.PrimarySeedCount)
                    .Append(" primarySamples=").Append(crossing.PrimarySampleCount)
                    .Append(" fallbackTriggered=").Append(crossing.FallbackTriggered)
                    .Append(" fallbackSeedsAdded=").Append(crossing.FallbackSeedsAdded)
                    .Append(" fallbackSamples=").Append(crossing.FallbackSampleCount)
                    .Append(" intermediateEnabled=").Append(crossing.IntermediateEnabled)
                    .Append(" midApproachGenerated=").Append(crossing.MidApproachGenerated)
                    .Append(" localHalf=").Append(string.IsNullOrEmpty(crossing.LocalHalf) ? "n/a" : crossing.LocalHalf)
                    .Append(" worldHalf=").Append(string.IsNullOrEmpty(crossing.WorldHalf) ? "n/a" : crossing.WorldHalf)
                    .Append(" filteredSeeds=").Append(crossing.FilteredSeedCount)
                    .Append(" samples=").Append(crossing.SampledApproachCount)
                    .Append(" accepted=").Append(crossing.AcceptedOptions.Count);
                if (crossing.SampledApproachCount == 0)
                {
                    text.Append(" [no NavMesh sample succeeded near any of the ").Append(crossing.GeneratedSeedCount)
                        .Append(" generated seed(s) for this crossing]");
                    // Collider type/enabled/trigger/bounds are only worth the extra text when every seed
                    // already failed - this is exactly the detail the next zero-sample crossing needs.
                    for (int c = 0; c < crossing.ColliderInfo.Count; c++)
                        text.Append(" {collider").Append(c).Append(": ").Append(crossing.ColliderInfo[c]).Append('}');
                    // Per-seed record. Distinguishes the three possible causes of a zero-sample
                    // crossing that a bare count cannot: a useful seed class was never generated, was
                    // generated and then filtered out, or survived filtering and genuinely failed
                    // NavMesh.SamplePosition. Bounded by the seed budget and only produced on this
                    // failure path, never per frame.
                    for (int sIdx = 0; sIdx < crossing.SeedDiagnostics.Count; sIdx++)
                        text.Append(' ').Append(DescribeSeed(crossing.SeedDiagnostics[sIdx]));
                }
                for (int j = 0; j < crossing.Evaluations.Count; j++)
                {
                    RouteCandidatePolicy.Evaluation evaluation = crossing.Evaluations[j];
                    if (evaluation == null || evaluation.Candidate == null) continue;
                    Vector3 approach = j < crossing.EvaluationApproaches.Count ? crossing.EvaluationApproaches[j] : Vector3.zero;
                    text.Append(" [approach=").Append(FormatVector(approach)).Append(' ')
                        .Append(RouteCandidatePolicy.DescribeCandidate(evaluation.Candidate, evaluation)).Append("]");
                }
                // Second-stage forensics. Emitted only when the bounded route-facing entrance stage
                // actually ran for this crossing (a large trigger whose first pass left a lone
                // quality-poor candidate), which is exactly the 0.6.17 live case that could not be
                // explained from the summary counters alone. Event-boundary only, never per frame.
                if (crossing.RouteFacingProbes.Count > 0 || crossing.RouteFacingGeometry.Count > 0)
                {
                    text.Append(" routeFacingProbes=").Append(crossing.RouteFacingProbes.Count)
                        .Append(" routeFacingQualityRef=").Append(string.IsNullOrEmpty(crossing.RouteFacingQualityReference)
                            ? "n/a" : crossing.RouteFacingQualityReference);
                    for (int g = 0; g < crossing.RouteFacingGeometry.Count; g++)
                        text.Append(" {box").Append(g).Append(": ").Append(crossing.RouteFacingGeometry[g]).Append('}');
                    for (int pIdx = 0; pIdx < crossing.RouteFacingProbes.Count; pIdx++)
                        text.Append(' ').Append(DescribeRouteFacingProbe(crossing.RouteFacingProbes[pIdx]));
                }
                if (crossing.BestAccepted != null && crossing.BestAccepted.Evaluation != null)
                {
                    RouteCandidatePolicy.Candidate selected = crossing.BestAccepted.Evaluation.Candidate;
                    text.Append(" [destination=").Append(crossing.Crossing == null ? "unknown" : crossing.Crossing.DestinationZone)
                        .Append(" selectedSeed=").Append(selected.SeedLabel)
                        .Append(" face=").Append(selected.HasLateralCentering ? selected.FaceLabel : "n/a")
                        .Append(" faceCenter=").Append(selected.HasLateralCentering ? selected.FaceCenterPosition : "n/a")
                        .Append(" candidate=").Append(selected.StableKey)
                        .Append(" lateralOffset=").Append(selected.HasLateralCentering
                            ? selected.LateralOffset.ToString("F2", CultureInfo.InvariantCulture) : "n/a")
                        .Append(" path=").Append(selected.Path)
                        .Append(" rankingReason=lowest-accepted-quality-then-route")
                        .Append(" centerRank=").Append(selected.HasLateralCentering
                            ? selected.LateralOffset.ToString("F2", CultureInfo.InvariantCulture) : "n/a")
                        .Append(" selectedReason=").Append(selected.HasLateralCentering
                            ? "centered-valid-approach" : "accepted-route")
                        .Append("]");
                }
            }
            return text.ToString();
        }

        private static CrossingInspection InspectCrossing(Vector3 start, bool startSampled, Vector3 sampledStart, Zoneline crossing, int crossingIndex, bool includeSeedDiagnostics)
        {
            CrossingInspection inspection = new CrossingInspection();
            inspection.Crossing = crossing;
            inspection.StableKey = CrossingKey(crossing);
            inspection.Active = IsActive(crossing);
            inspection.RemoveParty = crossing != null && crossing.RemoveParty;
            inspection.TransformPosition = crossing == null ? Vector3.zero : crossing.transform.position;
            DescribeColliders(crossing, inspection.ColliderInfo);

            int generatedSeedCount;
            int filteredSeedCount;
            List<string> approachLabels = new List<string>();
            List<Vector3> approaches = SampleApproaches(crossing, inspection.TransformPosition, start, inspection,
                includeSeedDiagnostics ? inspection.SeedDiagnostics : null, approachLabels,
                out generatedSeedCount, out filteredSeedCount,
                out inspection.PrimarySeedCount, out inspection.PrimarySampleCount,
                out inspection.FallbackTriggered, out inspection.FallbackSeedsAdded,
                out inspection.FallbackSampleCount);
            inspection.SampledApproachCount = approaches.Count;
            inspection.FilteredSeedCount = filteredSeedCount;
            for (int i = 0; i < approaches.Count; i++)
            {
                string key = inspection.StableKey + "/a" + i.ToString(CultureInfo.InvariantCulture);
                Vector3[] pathCorners;
                string seedLabel = i < approachLabels.Count ? approachLabels[i] : "unknown";
                RouteCandidatePolicy.Candidate candidate = MeasureCandidate(start, startSampled, sampledStart,
                    crossing, approaches[i], seedLabel, key, out pathCorners);
                RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
                inspection.Evaluations.Add(evaluation);
                inspection.EvaluationApproaches.Add(approaches[i]);
                if (!evaluation.Accepted) continue;
                RouteOption option = new RouteOption(crossing, approaches[i], evaluation, key, pathCorners);
                inspection.AcceptedOptions.Add(option);
                if (inspection.BestAccepted == null || OptionComparer.Instance.Compare(option, inspection.BestAccepted) < 0)
                    inspection.BestAccepted = option;
            }
            // If the only accepted result is a poor edge on a large trigger, spend one bounded
            // second-stage probe on the route-facing interior band before final ranking. This is
            // deliberately after normal sampling: ordinary crossings and already-good entrances
            // pay no extra NavMesh work, while a lone extreme candidate cannot become final merely
            // because no alternative was generated in the first pass.
            if (inspection.BestAccepted != null && RouteCandidatePolicy.ShouldProbeRouteFacingEntrance(
                inspection.BestAccepted.Evaluation, LargestTriggerFootprint(crossing)))
            {
                AddRouteFacingEntranceSeeds(approaches, approachLabels, crossing, start, inspection,
                    ref generatedSeedCount);
                for (int i = inspection.Evaluations.Count; i < approaches.Count; i++)
                {
                    string key = inspection.StableKey + "/a" + i.ToString(CultureInfo.InvariantCulture);
                    Vector3[] pathCorners;
                    string seedLabel = i < approachLabels.Count ? approachLabels[i] : "routeEntrance";
                    RouteCandidatePolicy.Candidate candidate = MeasureCandidate(start, startSampled, sampledStart,
                        crossing, approaches[i], seedLabel, key, out pathCorners);
                    RouteCandidatePolicy.Evaluation evaluation = RouteCandidatePolicy.Evaluate(candidate);
                    inspection.Evaluations.Add(evaluation);
                    inspection.EvaluationApproaches.Add(approaches[i]);
                    RecordRouteFacingOutcome(inspection, seedLabel, candidate, evaluation);
                    if (!evaluation.Accepted) continue;
                    RouteOption option = new RouteOption(crossing, approaches[i], evaluation, key, pathCorners);
                    inspection.AcceptedOptions.Add(option);
                    // Ranking policy is unchanged from 0.6.17: the same comparer decides, it simply
                    // finally has competing candidates to decide between.
                    if (OptionComparer.Instance.Compare(option, inspection.BestAccepted) < 0)
                        inspection.BestAccepted = option;
                }
                inspection.SampledApproachCount = approaches.Count;
            }
            // Assigned after the second stage so the reported seed count includes the route-facing
            // probes. 0.6.17 assigned it before, which is why the live Vitheo line read
            // generatedSeeds=22 whether or not the entrance probes had been generated.
            inspection.GeneratedSeedCount = generatedSeedCount;
            return inspection;
        }

        private static float LargestTriggerFootprint(Zoneline crossing)
        {
            Collider[] colliders = GetColliders(crossing);
            float largest = 0f;
            for (int i = 0; i < Math.Min(MaxCollidersPerCrossing, colliders.Length); i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                BoxCollider box = collider as BoxCollider;
                if (box != null && box.transform != null)
                {
                    Vector3 half = box.size * 0.5f;
                    Vector3 scale = box.transform.lossyScale;
                    largest = Math.Max(largest, Math.Max(Math.Abs(half.x * scale.x), Math.Abs(half.z * scale.z)));
                }
                else largest = Math.Max(largest, Math.Max(collider.bounds.extents.x, collider.bounds.extents.z));
            }
            return largest;
        }

        // Bounded second-stage route-facing entrance sampling.
        //
        // 0.6.17 generated exactly three points here, all at the box CENTRE height and one inward
        // depth, and recorded nothing about them. On the live Vitheo trigger that produced no extra
        // candidate at all, so the lone quality-poor -X face edge stayed selected by default. This
        // version derives the probe set from the LIVE collider (authoritative local centre/size,
        // lossyScale, rotation) via CrossingSeedGeometryPolicy.RouteFacingEntranceProbes: the same
        // face the approach-quality reference is measured from, two inward depths, bounded tangent
        // steps, and the lower-intermediate vertical level when the trigger is tall enough for it to
        // differ from centre. It remains a handful of points on one face - no grid, no flood fill,
        // no widened radius, no hardcoded coordinates - and each one still has to pass
        // NavMesh.SamplePosition, CalculatePath, and the unchanged acceptance/ranking rules.
        private static void AddRouteFacingEntranceSeeds(List<Vector3> sampled, List<string> labels,
            Zoneline crossing, Vector3 routeStart, CrossingInspection inspection, ref int generated)
        {
            if (sampled == null || labels == null || sampled.Count >= MaxApproachesPerCrossing) return;
            Collider[] colliders = GetColliders(crossing);
            int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            int probeIndex = 0;
            for (int i = 0; i < limit && sampled.Count < MaxApproachesPerCrossing; i++)
            {
                BoxCollider box = colliders[i] as BoxCollider;
                if (box == null || box.transform == null) continue;
                Transform t = box.transform;
                Vector3 half = box.size * 0.5f;
                if (half.sqrMagnitude <= 0.0001f) continue;
                Vector3 scale = t.lossyScale;
                float halfWorldX = Math.Abs(half.x * scale.x);
                float halfWorldY = Math.Abs(half.y * scale.y);
                float halfWorldZ = Math.Abs(half.z * scale.z);
                Vector3 localStart = t.InverseTransformPoint(routeStart) - box.center;

                CrossingSeedGeometryPolicy.Point3 localStartPoint =
                    new CrossingSeedGeometryPolicy.Point3(localStart.x, localStart.y, localStart.z);
                CrossingSeedGeometryPolicy.Point3 halfPoint =
                    new CrossingSeedGeometryPolicy.Point3(half.x, half.y, half.z);
                CrossingSeedGeometryPolicy.RouteFaceAxis axis =
                    CrossingSeedGeometryPolicy.SelectRouteFacingAxis(localStartPoint, halfPoint);
                string qualityReference = CrossingSeedGeometryPolicy.RouteFaceLabel(axis);
                bool alongX = axis == CrossingSeedGeometryPolicy.RouteFaceAxis.XPositive
                    || axis == CrossingSeedGeometryPolicy.RouteFaceAxis.XNegative;
                // The tangent of the chosen face is the OTHER horizontal world half axis: how wide the
                // face actually is decides whether three tangent samples can cover it.
                float faceTangentWorldHalf = alongX ? halfWorldZ : halfWorldX;

                RecordRouteFacingGeometry(inspection, box, localStart, halfWorldX, halfWorldY, halfWorldZ,
                    qualityReference);

                CrossingSeedGeometryPolicy.EntranceProbe[] probes =
                    CrossingSeedGeometryPolicy.RouteFacingEntranceProbes(localStartPoint, halfPoint,
                        faceTangentWorldHalf, halfWorldY, FloorSeedRadius);
                for (int e = 0; e < probes.Length && sampled.Count < MaxApproachesPerCrossing; e++)
                {
                    CrossingSeedGeometryPolicy.Point3 offset = probes[e].Offset;
                    Vector3 point = t.TransformPoint(box.center + new Vector3(
                        offset.X * Math.Abs(half.x), offset.Y * Math.Abs(half.y), offset.Z * Math.Abs(half.z)));
                    generated++;
                    string label = "routeEntrance" + probeIndex.ToString(CultureInfo.InvariantCulture);
                    probeIndex++;

                    RouteFacingProbeDiagnostic record = new RouteFacingProbeDiagnostic();
                    record.Index = probeIndex - 1;
                    record.Label = label;
                    record.UnsampledPosition = point;
                    record.Radius = FloorSeedRadius;
                    record.LocalNormalizedOffset = new Vector3(offset.X, offset.Y, offset.Z);
                    record.WorldY = point.y;
                    record.DistanceToColliderVolume = DistanceToCrossingVolume(point, crossing, colliders);
                    record.InsideCollider = IsInsideAnyCrossingCollider(point, colliders);
                    record.QualityReferenceLabel = qualityReference;

                    NavMeshHit hit;
                    record.Sampled = NavMesh.SamplePosition(point, out hit, FloorSeedRadius, NavMesh.AllAreas);
                    if (record.Sampled) record.SampleHit = hit.position;
                    AddRouteFacingProbeDiagnostic(inspection, record);
                    if (!record.Sampled) continue;

                    bool duplicate = false;
                    for (int d = 0; d < sampled.Count; d++)
                        if (HorizontalDistance(sampled[d], hit.position) <= ApproachDedupDistance) duplicate = true;
                    if (duplicate)
                    {
                        record.Duplicate = true;
                        record.PathStatus = "duplicateApproach";
                        record.Acceptance = "duplicateApproach";
                        continue;
                    }
                    sampled.Add(hit.position);
                    labels.Add(label);
                }
            }
        }

        private static void AddRouteFacingProbeDiagnostic(CrossingInspection inspection, RouteFacingProbeDiagnostic record)
        {
            if (inspection == null || record == null) return;
            if (inspection.RouteFacingProbes.Count >= CrossingSeedGeometryPolicy.MaxRouteFacingEntranceProbes
                * MaxCollidersPerCrossing) return;
            inspection.RouteFacingProbes.Add(record);
        }

        // Authoritative live BoxCollider record for the crossing the second stage is probing. Taken
        // from the collider itself at route-build time: nothing here is zone-specific or hardcoded.
        private static void RecordRouteFacingGeometry(CrossingInspection inspection, BoxCollider box,
            Vector3 localStart, float halfWorldX, float halfWorldY, float halfWorldZ, string qualityReference)
        {
            if (inspection == null || box == null || box.transform == null) return;
            if (inspection.RouteFacingGeometry.Count >= MaxCollidersPerCrossing) return;
            Transform t = box.transform;
            inspection.RouteFacingQualityReference = qualityReference;
            inspection.RouteFacingGeometry.Add(
                "center=" + FormatVector(box.center) +
                " size=" + FormatVector(box.size) +
                " lossyScale=" + FormatVector(t.lossyScale) +
                " euler=" + FormatVector(t.eulerAngles) +
                " worldHalfAxes=(" + halfWorldX.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                halfWorldY.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                halfWorldZ.ToString("0.00", CultureInfo.InvariantCulture) + ")" +
                " localStart=" + FormatVector(localStart) +
                " qualityRef=" + qualityReference);
        }

        // Threads the CalculatePath/acceptance outcome back onto the probe that produced the approach,
        // so one diagnostic line covers the whole generate -> sample -> path -> accept chain.
        private static void RecordRouteFacingOutcome(CrossingInspection inspection, string seedLabel,
            RouteCandidatePolicy.Candidate candidate, RouteCandidatePolicy.Evaluation evaluation)
        {
            if (inspection == null || string.IsNullOrEmpty(seedLabel) || candidate == null) return;
            for (int i = 0; i < inspection.RouteFacingProbes.Count; i++)
            {
                RouteFacingProbeDiagnostic record = inspection.RouteFacingProbes[i];
                if (record == null || record.Label != seedLabel) continue;
                record.Evaluated = true;
                record.PathStatus = candidate.Path.ToString() + "/" + candidate.CornerCount.ToString(CultureInfo.InvariantCulture);
                record.Acceptance = evaluation == null ? "unevaluated" : evaluation.Acceptance.ToString();
                return;
            }
        }

        private static string DescribeRouteFacingProbe(RouteFacingProbeDiagnostic probe)
        {
            if (probe == null) return "{probe=null}";
            StringBuilder text = new StringBuilder();
            text.Append("{probe").Append(probe.Index.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(probe.Label)
                .Append(" localOffset=").Append(FormatVector(probe.LocalNormalizedOffset))
                .Append(" pos=").Append(FormatVector(probe.UnsampledPosition))
                .Append(" worldY=").Append(probe.WorldY.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" r=").Append(probe.Radius.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" dVol=").Append(probe.DistanceToColliderVolume.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" inside=").Append(probe.InsideCollider)
                .Append(" qualityRef=").Append(string.IsNullOrEmpty(probe.QualityReferenceLabel) ? "n/a" : probe.QualityReferenceLabel)
                .Append(" sampled=").Append(probe.Sampled);
            if (probe.Sampled) text.Append(" hit=").Append(FormatVector(probe.SampleHit));
            text.Append(" path=").Append(probe.PathStatus)
                .Append(" result=").Append(probe.Acceptance);
            return text.Append('}').ToString();
        }

        private static RouteCandidatePolicy.Candidate MeasureCandidate(Vector3 rawStart, bool startSampled, Vector3 sampledStart,
            Zoneline crossing, Vector3 approach, string seedLabel, string stableKey, out Vector3[] pathCorners)
        {
            pathCorners = new Vector3[0];
            RouteCandidatePolicy.Candidate candidate = new RouteCandidatePolicy.Candidate();
            candidate.StableKey = stableKey;
            candidate.SeedLabel = seedLabel;
            candidate.Active = IsActive(crossing);
            candidate.RemoveParty = crossing != null && crossing.RemoveParty;
            candidate.Sampled = true;
            candidate.ApproachDistanceToCrossing = DistanceToCrossing(approach, crossing);
            candidate.StartDistanceToCrossing = DistanceToCrossing(startSampled ? sampledStart : rawStart, crossing);
            candidate.EndpointDistanceToCrossing = candidate.StartDistanceToCrossing;
            candidate.RouteLength = float.MaxValue;
            candidate.Path = RouteCandidatePolicy.PathKind.Invalid;
            candidate.CornerCount = 0;
            ApproachQualityReference quality = GetApproachQualityReference(crossing, rawStart);
            if (quality.Valid)
            {
                candidate.HasApproachQuality = true;
                candidate.ApproachQualityDistance = HorizontalDistance(approach, quality.Position);
                candidate.ApproachQualityReferenceLabel = quality.Label;
                candidate.ApproachQualityReferencePosition = FormatVector(quality.Position);
                if (quality.Tangent.sqrMagnitude > 0.0001f)
                {
                    candidate.HasLateralCentering = true;
                    candidate.CenteringGroup = CrossingKey(crossing);
                    candidate.FaceLabel = quality.Label;
                    candidate.FaceCenterPosition = FormatVector(quality.Position);
                    candidate.FaceTangent = FormatVector(quality.Tangent);
                    candidate.LateralOffset = Math.Abs(Vector3.Dot(approach - quality.Position, quality.Tangent));
                }
            }

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

        // Large triggers can contain many valid NavMesh points. For those only, rank a candidate by
        // lateral displacement from the route-facing face centre before route length. This is a true
        // OBB tangent measurement, so rotation, non-uniform scale, and an off-centre BoxCollider are
        // respected. It prefers a centered entrance without rejecting an offset edge when it is the
        // only accepted route. Small crossings retain their exact pre-0.6.16 ordering.
        private static ApproachQualityReference GetApproachQualityReference(Zoneline crossing, Vector3 routeStart)
        {
            ApproachQualityReference result = new ApproachQualityReference();
            Collider[] colliders = GetColliders(crossing);
            float largestFootprint = 0f;
            Collider selected = null;
            for (int i = 0; i < Math.Min(MaxCollidersPerCrossing, colliders.Length); i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                BoxCollider box = collider as BoxCollider;
                float halfX;
                float halfZ;
                if (box != null && box.transform != null)
                {
                    Vector3 halfLocal = box.size * 0.5f;
                    Vector3 scale = box.transform.lossyScale;
                    halfX = Math.Abs(halfLocal.x * scale.x);
                    halfZ = Math.Abs(halfLocal.z * scale.z);
                }
                else
                {
                    halfX = collider.bounds.extents.x;
                    halfZ = collider.bounds.extents.z;
                }
                float footprint = Math.Max(halfX, halfZ);
                if (footprint > largestFootprint)
                {
                    largestFootprint = footprint;
                    selected = collider;
                }
            }
            if (selected == null || largestFootprint < 12f) return result;

            BoxCollider selectedBox = selected as BoxCollider;
            if (selectedBox != null && selectedBox.transform != null)
            {
                Transform t = selectedBox.transform;
                Vector3 localStart = t.InverseTransformPoint(routeStart) - selectedBox.center;
                Vector3 half = selectedBox.size * 0.5f;
                // Shared with the second-stage entrance probes. The quality reference and the probes
                // MUST name the same face: 0.6.17 measured quality against routeFaceZ+ while its only
                // sampled candidate sat on the -X face, and no probe was ever placed on the Z+ face at
                // a height NavMesh would accept, so quality could not improve by construction.
                CrossingSeedGeometryPolicy.RouteFaceAxis axis = CrossingSeedGeometryPolicy.SelectRouteFacingAxis(
                    new CrossingSeedGeometryPolicy.Point3(localStart.x, localStart.y, localStart.z),
                    new CrossingSeedGeometryPolicy.Point3(half.x, half.y, half.z));
                bool axisIsX = axis == CrossingSeedGeometryPolicy.RouteFaceAxis.XPositive
                    || axis == CrossingSeedGeometryPolicy.RouteFaceAxis.XNegative;
                float faceSign = (axis == CrossingSeedGeometryPolicy.RouteFaceAxis.XNegative
                    || axis == CrossingSeedGeometryPolicy.RouteFaceAxis.ZNegative) ? -1f : 1f;
                if (axisIsX && Math.Abs(half.x) > 0.001f)
                    result.Position = t.TransformPoint(selectedBox.center + new Vector3(faceSign * Math.Abs(half.x), 0f, 0f));
                else if (!axisIsX && Math.Abs(half.z) > 0.001f)
                    result.Position = t.TransformPoint(selectedBox.center + new Vector3(0f, 0f, faceSign * Math.Abs(half.z)));
                else return result;
                // TransformVector, unlike a world-AABB approximation, preserves the collider's
                // authored local tangent and its scale. Normalize only after transforming so the
                // dot product below is measured in world metres.
                Vector3 localTangent = axisIsX ? new Vector3(0f, 0f, 1f) : new Vector3(1f, 0f, 0f);
                result.Tangent = t.TransformVector(localTangent).normalized;
                result.Label = CrossingSeedGeometryPolicy.RouteFaceLabel(axis);
                result.Valid = true;
                return result;
            }

            Bounds bounds = selected.bounds;
            Vector3 delta = routeStart - bounds.center;
            delta.y = 0f;
            if (Math.Abs(delta.x) >= Math.Abs(delta.z) && bounds.extents.x > 0.001f)
            {
                float sign = delta.x < 0f ? -1f : 1f;
                result.Position = bounds.center + new Vector3(sign * bounds.extents.x, 0f, 0f);
                result.Label = sign < 0f ? "routeBoundsX-" : "routeBoundsX+";
            }
            else if (bounds.extents.z > 0.001f)
            {
                float sign = delta.z < 0f ? -1f : 1f;
                result.Position = bounds.center + new Vector3(0f, 0f, sign * bounds.extents.z);
                result.Label = sign < 0f ? "routeBoundsZ-" : "routeBoundsZ+";
            }
            else return result;
            result.Valid = true;
            return result;
        }

        private static List<Vector3> SampleApproaches(Zoneline crossing, Vector3 crossingPosition, Vector3 start,
            CrossingInspection inspection, List<SeedDiagnostic> diagnostics, List<string> approachLabels,
            out int generatedSeedCount, out int filteredSeedCount,
            out int primarySeedCount, out int primarySampleCount,
            out bool fallbackTriggered, out int fallbackSeedsAdded,
            out int fallbackSampleCount)
        {
            List<Seed> seeds = new List<Seed>(MaxSeedsPerCrossing);
            filteredSeedCount = 0;
            primarySeedCount = 0;
            primarySampleCount = 0;
            fallbackTriggered = false;
            fallbackSeedsAdded = 0;
            fallbackSampleCount = 0;
            // Resolved once per route build and threaded through every seed measurement below, so
            // adding volume-aware filtering does not turn one GetComponentsInChildren scan into one
            // per seed.
            Collider[] colliders = GetColliders(crossing);
            AddSeed(seeds, crossingPosition, 8f, "raw", crossing, colliders, diagnostics, ref filteredSeedCount);

            Vector3 towardStart = start - crossingPosition;
            towardStart.y = 0f;
            if (towardStart.sqrMagnitude > 0.01f)
            {
                towardStart.Normalize();
                AddSeed(seeds, crossingPosition + towardStart * 2.5f, 3.5f, "towardParty2.5", crossing, colliders, diagnostics, ref filteredSeedCount);
                AddSeed(seeds, crossingPosition + towardStart * 5f, 3.5f, "towardParty5", crossing, colliders, diagnostics, ref filteredSeedCount);
            }

            int colliderLimit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < colliderLimit && seeds.Count < PrimarySeedBudget; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                Bounds bounds = collider.bounds;
                AddSeed(seeds, bounds.center, 4f, "boundsCenter", crossing, colliders, diagnostics, ref filteredSeedCount);
                AddSeed(seeds, ClosestPoint(collider, start), 3f, "closestToParty", crossing, colliders, diagnostics, ref filteredSeedCount);

                // A tall vertical trigger (archway/doorway/cliff-face zoneline) can have its center and
                // transform origin sit far above real walkable ground. See CrossingSeedGeometryPolicy for
                // the field case and reasoning. Only added when the collider is actually tall enough for
                // this to matter, and every resulting seed still has to pass NavMesh.SamplePosition plus
                // the full existing acceptance policy like any other seed.
                CrossingSeedGeometryPolicy.Point3 extents = new CrossingSeedGeometryPolicy.Point3(
                    bounds.extents.x, bounds.extents.y, bounds.extents.z);
                if (CrossingSeedGeometryPolicy.FloorSeedsMeaningfullyDifferFromCenter(extents, FloorSeedRadius))
                {
                    CrossingSeedGeometryPolicy.Point3 center = new CrossingSeedGeometryPolicy.Point3(
                        bounds.center.x, bounds.center.y, bounds.center.z);
                    CrossingSeedGeometryPolicy.Point3[] floorSeeds = CrossingSeedGeometryPolicy.FloorSeeds(center, extents);
                    // Proximity-filtered like every other seed, but against the crossing VOLUME rather
                    // than its raw centre point. On a large trigger these floor corners lie ON the
                    // verified volume - and therefore at acceptance distance 0 - even though they sit
                    // tens of metres from its centre; 0.6.11 discarded all of them for being "far from
                    // centre" and left only the column of seeds directly above the centre.
                    for (int f = 0; f < floorSeeds.Length && seeds.Count < PrimarySeedBudget; f++)
                        AddCrossingProximitySeed(seeds,
                            new Vector3(floorSeeds[f].X, floorSeeds[f].Y, floorSeeds[f].Z),
                            FloorSeedRadius, "floor" + f.ToString(CultureInfo.InvariantCulture),
                            crossing, colliders, diagnostics, ref filteredSeedCount);
                }

                // Oriented (OBB) face seeds plus a bounded vertical probe. The axis-aligned seeds
                // above cluster on one point cloud around the trigger origin and, for a rotated or
                // oversized trigger, their AABB corners fall outside the real volume entirely - the
                // live Hidden -> Duskenlight case where 12 of 14 seeds found no NavMesh and the only
                // two that sampled landed ~40m out. These use the collider's own basis instead, so a
                // rotated/scaled trigger's real faces and its actual height range are searched.
                // See CrossingSeedGeometryPolicy for the field evidence.
                AddOrientedCrossingSeeds(seeds, collider, start, crossing, inspection, colliders, diagnostics, ref filteredSeedCount);
            }

            Vector3[] around =
            {
                new Vector3(4f, 0f, 0f), new Vector3(-4f, 0f, 0f),
                new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, -4f)
            };
            for (int i = 0; i < around.Length && seeds.Count < PrimarySeedBudget; i++)
                AddSeed(seeds, crossingPosition + around[i], 3f,
                    "cardinal" + i.ToString(CultureInfo.InvariantCulture), crossing, colliders, diagnostics, ref filteredSeedCount);

            primarySeedCount = seeds.Count;
            List<Vector3> sampled = new List<Vector3>(MaxApproachesPerCrossing);
            SampleSeedRange(seeds, 0, primarySeedCount, sampled, approachLabels, diagnostics);
            primarySampleCount = sampled.Count;

            // Do not turn every route build into a denser search. The 0.6.13 live diagnostic proved a
            // much narrower condition: player start sampled successfully, the exact large/tall Hidden
            // trigger was resolved, and ALL primary crossing seeds still returned SamplePosition=false.
            // Only then spend eight extra probes on a world-metre inner ring at the already-proven lower
            // intermediate height. This preserves the fast path for every crossing that already works.
            if (sampled.Count == 0)
            {
                fallbackTriggered = true;
                AddZeroSampleInteriorRingSeeds(seeds, colliders, crossing, diagnostics, ref filteredSeedCount);
                fallbackSeedsAdded = seeds.Count - primarySeedCount;
                SampleSeedRange(seeds, primarySeedCount, seeds.Count, sampled, approachLabels, diagnostics);
                fallbackSampleCount = sampled.Count - primarySampleCount;
            }

            generatedSeedCount = seeds.Count;
            return sampled;
        }

        private static void SampleSeedRange(List<Seed> seeds, int startIndex, int endExclusive,
            List<Vector3> sampled, List<string> approachLabels, List<SeedDiagnostic> diagnostics)
        {
            int end = Math.Min(endExclusive, seeds == null ? 0 : seeds.Count);
            for (int i = Math.Max(0, startIndex); i < end && sampled.Count < MaxApproachesPerCrossing; i++)
            {
                NavMeshHit hit;
                bool ok = NavMesh.SamplePosition(seeds[i].Position, out hit, seeds[i].Radius, NavMesh.AllAreas);
                RecordSeedSample(diagnostics, seeds[i], ok, ok ? hit.position : Vector3.zero);
                if (!ok) continue;
                bool duplicate = false;
                for (int j = 0; j < sampled.Count; j++)
                {
                    if (HorizontalDistance(sampled[j], hit.position) <= ApproachDedupDistance)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    sampled.Add(hit.position);
                    if (approachLabels != null) approachLabels.Add(seeds[i].Label);
                }
            }
        }

        // Bounded recovery for a very large/tall BoxCollider whose normal centre/floor/face/mid-layer
        // probes all failed NavMesh.SamplePosition. The ring is generated in the collider's authoritative
        // local OBB but sized in world metres, so rotation and non-uniform scale are both honoured.
        private static void AddZeroSampleInteriorRingSeeds(List<Seed> seeds, Collider[] colliders, Zoneline crossing,
            List<SeedDiagnostic> diagnostics, ref int filteredSeedCount)
        {
            if (seeds == null || colliders == null || seeds.Count >= MaxSeedsPerCrossing) return;
            int colliderLimit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < colliderLimit && seeds.Count < MaxSeedsPerCrossing; i++)
            {
                BoxCollider box = colliders[i] as BoxCollider;
                if (box == null || box.transform == null) continue;
                Vector3 size = box.size;
                Vector3 halfLocal = new Vector3(Math.Abs(size.x) * 0.5f, Math.Abs(size.y) * 0.5f, Math.Abs(size.z) * 0.5f);
                Vector3 scale = box.transform.lossyScale;
                float halfWorldX = halfLocal.x * Math.Abs(scale.x);
                float halfWorldY = halfLocal.y * Math.Abs(scale.y);
                float halfWorldZ = halfLocal.z * Math.Abs(scale.z);
                if (!CrossingSeedGeometryPolicy.IntermediateVerticalLayersMeaningfullyDifferFromCenter(
                        new CrossingSeedGeometryPolicy.Point3(halfWorldX, halfWorldY, halfWorldZ), FloorSeedRadius))
                    continue;

                CrossingSeedGeometryPolicy.Point3[] ring = CrossingSeedGeometryPolicy.LowerIntermediateFallbackRingOffsets(
                    halfWorldX, halfWorldZ, FloorSeedRadius, 8);
                for (int r = 0; r < ring.Length && seeds.Count < PrimarySeedBudget + ZeroSampleFallbackBudget; r++)
                {
                    Vector3 localPoint = box.center + new Vector3(
                        ring[r].X * halfLocal.x, ring[r].Y * halfLocal.y, ring[r].Z * halfLocal.z);
                    AddCrossingProximitySeed(seeds, box.transform.TransformPoint(localPoint), FloorSeedRadius,
                        "midRing" + r.ToString(CultureInfo.InvariantCulture), crossing, colliders, diagnostics, ref filteredSeedCount);
                }
            }
        }

        // Oriented face centres and a bounded vertical probe, built from the collider's real
        // local-to-world transform so rotation and lossyScale are honoured. Every point produced
        // here is still only a SEED: it must pass NavMesh.SamplePosition, CalculatePath and the
        // unchanged acceptance policy exactly like any other.
        private static void AddOrientedCrossingSeeds(List<Seed> seeds, Collider collider, Vector3 routeStart, Zoneline crossing,
            CrossingInspection inspection, Collider[] colliders, List<SeedDiagnostic> diagnostics, ref int filteredSeedCount)
        {
            if (seeds.Count >= PrimarySeedBudget || collider == null) return;
            try
            {
                Transform t = collider.transform;
                Vector3 localCenter;
                Vector3 half;

                // BoxCollider exposes its authoritative local centre + size directly. Using
                // collider.bounds (a WORLD AABB) and then inverse-transforming its extents is not an
                // oriented-box reconstruction: on a rotated/scaled box it produced "face" seeds that
                // were either only ~6m from the centre or ~40m outside the real trigger, exactly what
                // the 0.6.12 live diagnostic showed. Build OBB points from the BoxCollider itself.
                BoxCollider box = collider as BoxCollider;
                if (box != null)
                {
                    localCenter = box.center;
                    Vector3 size = box.size;
                    half = new Vector3(Math.Abs(size.x) * 0.5f, Math.Abs(size.y) * 0.5f, Math.Abs(size.z) * 0.5f);
                }
                else
                {
                    // Generic Collider fallback: bounds are all the shape information Unity exposes
                    // uniformly. Keep this bounded and explicitly best-effort; volume filtering still
                    // prevents a world-AABB artefact from becoming an accepted crossing approach.
                    Bounds world = collider.bounds;
                    localCenter = t.InverseTransformPoint(world.center);
                    Vector3 approximate = t.InverseTransformVector(world.extents);
                    half = new Vector3(Math.Abs(approximate.x), Math.Abs(approximate.y), Math.Abs(approximate.z));
                }

                if (half.sqrMagnitude <= 0.0001f) return;
                Vector3 scaleForDecision = t.lossyScale;
                Vector3 worldHalf = new Vector3(
                    Math.Abs(half.x * scaleForDecision.x),
                    Math.Abs(half.y * scaleForDecision.y),
                    Math.Abs(half.z * scaleForDecision.z));
                if (inspection != null)
                {
                    inspection.LocalHalf = FormatVector(half);
                    inspection.WorldHalf = FormatVector(worldHalf);
                }

                CrossingSeedGeometryPolicy.Point3[] faces = CrossingSeedGeometryPolicy.OrientedFaceOffsets();
                for (int f = 0; f < faces.Length && seeds.Count < PrimarySeedBudget; f++)
                {
                    Vector3 localPoint = localCenter + new Vector3(faces[f].X * half.x, faces[f].Y * half.y, faces[f].Z * half.z);
                    AddCrossingProximitySeed(seeds, t.TransformPoint(localPoint), FloorSeedRadius,
                        "face" + f.ToString(CultureInfo.InvariantCulture), crossing, colliders, diagnostics, ref filteredSeedCount);
                }

                float[] heights = CrossingSeedGeometryPolicy.VerticalProbeOffsets(3);
                for (int h = 0; h < heights.Length && seeds.Count < PrimarySeedBudget; h++)
                {
                    Vector3 localPoint = localCenter + new Vector3(0f, heights[h] * half.y, 0f);
                    AddCrossingProximitySeed(seeds, t.TransformPoint(localPoint), FloorSeedRadius,
                        "vert" + h.ToString(CultureInfo.InvariantCulture), crossing, colliders, diagnostics, ref filteredSeedCount);
                }

                // 0.6.12 proved the large Hidden trigger's face/floor candidates were finally retained,
                // but every retained seed still failed SamplePosition. The live working historical
                // approach (~232.14,50.06,116.71) sits inside the trigger at an INTERMEDIATE height,
                // not at its centre or bottom face. Add one bounded lower-mid interior cross only for
                // triggers tall enough that centre/floor sample spheres leave a real blind band.
                CrossingSeedGeometryPolicy.Point3 worldHalfPoint = new CrossingSeedGeometryPolicy.Point3(
                    worldHalf.x, worldHalf.y, worldHalf.z);
                bool intermediateRequired = CrossingSeedGeometryPolicy.IntermediateVerticalLayersMeaningfullyDifferFromCenter(
                    worldHalfPoint, FloorSeedRadius);
                if (inspection != null) inspection.IntermediateEnabled = intermediateRequired;
                if (intermediateRequired)
                {
                    CrossingSeedGeometryPolicy.Point3[] interior = CrossingSeedGeometryPolicy.LowerIntermediateInteriorOffsets();
                    for (int m = 0; m < interior.Length && seeds.Count < PrimarySeedBudget; m++)
                    {
                        Vector3 localPoint = localCenter + new Vector3(interior[m].X * half.x, interior[m].Y * half.y, interior[m].Z * half.z);
                        AddCrossingProximitySeed(seeds, t.TransformPoint(localPoint), FloorSeedRadius,
                            "midLow" + m.ToString(CultureInfo.InvariantCulture), crossing, colliders, diagnostics, ref filteredSeedCount);
                    }

                    // The live 0.6.12 failure also proved that "intermediate Y" cannot be useful if it
                    // is only sampled down the centre column of a large/rotated volume. Add three
                    // route-facing lower-mid surface samples in the authoritative local OBB: face
                    // centre plus +/- quarter-width tangent offsets. This is a bounded band, not a grid.
                    Vector3 startLocal = t.InverseTransformPoint(routeStart) - localCenter;
                    CrossingSeedGeometryPolicy.Point3 startPoint = new CrossingSeedGeometryPolicy.Point3(startLocal.x, startLocal.y, startLocal.z);
                    CrossingSeedGeometryPolicy.Point3 localHalfPoint = new CrossingSeedGeometryPolicy.Point3(half.x, half.y, half.z);
                    CrossingSeedGeometryPolicy.Point3[] approachFace = CrossingSeedGeometryPolicy.LowerIntermediateApproachFaceOffsets(startPoint, localHalfPoint);
                    for (int m = 0; m < approachFace.Length && seeds.Count < PrimarySeedBudget; m++)
                    {
                        if (inspection != null) inspection.MidApproachGenerated++;
                        Vector3 localPoint = localCenter + new Vector3(approachFace[m].X * half.x, approachFace[m].Y * half.y, approachFace[m].Z * half.z);
                        AddCrossingProximitySeed(seeds, t.TransformPoint(localPoint), FloorSeedRadius,
                            "midApproach" + m.ToString(CultureInfo.InvariantCulture), crossing, colliders, diagnostics, ref filteredSeedCount);
                    }
                }
            }
            catch { }
        }

        // Spends seed budget only on points that could still produce an ACCEPTED approach.
        //
        // Relevance is measured against the verified crossing VOLUME - the same metric
        // DistanceToCrossing uses for acceptance - never against the crossing's raw transform point.
        // Those two diverge by up to ~45m on a large trigger (the live 67.5 x 47.1 x 59.4 Hidden
        // BoxCollider), and measuring against the raw point discarded seeds sitting ON the trigger
        // whose acceptance distance is 0. Because filter and acceptance now share one metric, "a seed
        // beyond acceptance + radius can only ever yield a rejected endpoint" is sound again, so a
        // genuinely remote seed beside a small or rotated trigger stays filtered exactly as before.
        private static void AddCrossingProximitySeed(List<Seed> seeds, Vector3 position, float radius,
            string label, Zoneline crossing, Collider[] colliders, List<SeedDiagnostic> diagnostics,
            ref int filteredSeedCount)
        {
            float volumeDistance = DistanceToCrossingVolume(position, crossing, colliders);
            bool inside = IsInsideAnyCrossingCollider(position, colliders);
            if (!CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume(volumeDistance, inside,
                    RouteCandidatePolicy.NativeProbeApproachNearCrossing, radius))
            {
                filteredSeedCount++;
                RecordSeed(diagnostics, seeds.Count, label, position, radius, crossing, volumeDistance, inside,
                    false, "beyondAcceptancePlusRadius");
                return;
            }
            AddSeed(seeds, position, radius, label, crossing, colliders, diagnostics, ref filteredSeedCount);
        }

        private static void AddSeed(List<Seed> seeds, Vector3 position, float radius, string label,
            Zoneline crossing, Collider[] colliders, List<SeedDiagnostic> diagnostics, ref int filteredSeedCount)
        {
            bool budgetFull = seeds.Count >= MaxSeedsPerCrossing;
            if (budgetFull) filteredSeedCount++;
            RecordSeed(diagnostics, seeds.Count, label, position, radius, crossing,
                DistanceToCrossingVolume(position, crossing, colliders),
                IsInsideAnyCrossingCollider(position, colliders),
                !budgetFull, budgetFull ? "seedBudgetFull" : "kept");
            if (budgetFull) return;
            seeds.Add(new Seed(position, radius, label));
        }

        // True when the point lies within one of the crossing's verified trigger colliders. Uses the
        // live Collider.ClosestPoint (which returns the point itself when it is inside, and honours
        // rotation/scale), falling back to the collider's own bounds arithmetic inside ClosestPoint.
        private static bool IsInsideAnyCrossingCollider(Vector3 point, Collider[] colliders)
        {
            if (colliders == null) return false;
            int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                try
                {
                    if ((ClosestPoint(collider, point) - point).sqrMagnitude <= 0.0001f) return true;
                }
                catch { }
            }
            return false;
        }

        // Identical in meaning to DistanceToCrossing, but reuses an already-resolved collider set so a
        // route build does not re-scan the crossing's children once per seed.
        private static float DistanceToCrossingVolume(Vector3 point, Zoneline crossing, Collider[] colliders)
        {
            if (crossing == null || crossing.gameObject == null) return float.MaxValue;
            float best = HorizontalDistance(point, crossing.transform.position);
            if (colliders == null) return best;
            int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                float distance = HorizontalDistance(point, ClosestPoint(collider, point));
                if (distance < best) best = distance;
            }
            return best;
        }

        private static void RecordSeed(List<SeedDiagnostic> diagnostics, int index, string label, Vector3 position,
            float radius, Zoneline crossing, float volumeDistance, bool inside, bool kept, string reason)
        {
            if (diagnostics == null || diagnostics.Count >= MaxSeedsPerCrossing * 2) return;
            SeedDiagnostic record = new SeedDiagnostic();
            record.Index = index;
            record.Label = label;
            record.Position = position;
            record.Radius = radius;
            record.DistanceToRawCenter = crossing == null || crossing.gameObject == null
                ? float.MaxValue
                : HorizontalDistance(position, crossing.transform.position);
            record.DistanceToColliderVolume = volumeDistance;
            record.InsideCollider = inside;
            record.Kept = kept;
            record.FilterReason = reason;
            record.Sampled = false;
            diagnostics.Add(record);
        }

        private static void RecordSeedSample(List<SeedDiagnostic> diagnostics, Seed seed, bool sampled, Vector3 hit)
        {
            if (diagnostics == null) return;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                SeedDiagnostic record = diagnostics[i];
                if (record == null || !record.Kept || record.Label != seed.Label) continue;
                if ((record.Position - seed.Position).sqrMagnitude > 0.0001f) continue;
                record.Sampled = sampled;
                record.SampleHit = hit;
                return;
            }
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
                BoxCollider box = collider as BoxCollider;
                string localBox = box == null ? string.Empty :
                    " localCenter=" + FormatVector(box.center) + " localSize=" + FormatVector(box.size) +
                    " euler=" + FormatVector(box.transform.eulerAngles) + " lossyScale=" + FormatVector(box.transform.lossyScale);
                into.Add(collider.GetType().Name + " enabled=" + collider.enabled + " trigger=" + collider.isTrigger +
                    " center=" + FormatVector(b.center) + " size=" + FormatVector(b.size) + localBox);
            }
            if (colliders.Length > limit) into.Add("+" + (colliders.Length - limit) + " more collider(s)");
        }

        // Bounded, one-line collider description for the crossing handoff diagnostic. Emitted only
        // at a handoff or a zero-accepted crossing, never per frame. Reports the real transform,
        // rotation and lossyScale alongside the axis-aligned world bounds so a rotated or scaled
        // trigger can be told apart from a genuinely distant one - axis-aligned bounds corners of a
        // rotated volume can sit far outside the trigger itself, which is exactly the case that can
        // make a sampled approach land tens of metres from the verified crossing.
        internal static string DescribeCrossingColliders(Zoneline crossing)
        {
            if (crossing == null) return "none";
            Collider[] colliders = GetColliders(crossing);
            if (colliders == null || colliders.Length == 0) return "none";
            StringBuilder builder = new StringBuilder();
            int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
            for (int i = 0; i < limit; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                if (builder.Length > 0) builder.Append(" | ");
                try
                {
                    Bounds bounds = collider.bounds;
                    Transform t = collider.transform;
                    builder.Append(collider.GetType().Name)
                        .Append(" trigger=").Append(collider.isTrigger)
                        .Append(" pos=").Append(FormatVector(t.position))
                        .Append(" euler=").Append(FormatVector(t.eulerAngles))
                        .Append(" lossyScale=").Append(FormatVector(t.lossyScale))
                        .Append(" boundsCenter=").Append(FormatVector(bounds.center))
                        .Append(" boundsExtents=").Append(FormatVector(bounds.extents));
                    BoxCollider box = collider as BoxCollider;
                    if (box != null)
                        builder.Append(" localCenter=").Append(FormatVector(box.center))
                            .Append(" localSize=").Append(FormatVector(box.size));
                }
                catch { builder.Append("unreadable"); }
            }
            return builder.Length == 0 ? "none" : builder.ToString();
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

        // One bounded line per seed considered for a crossing. See CrossingInspection.SeedDiagnostics.
        private static string DescribeSeed(SeedDiagnostic seed)
        {
            if (seed == null) return "{seed=null}";
            StringBuilder text = new StringBuilder();
            text.Append("{seed").Append(seed.Index.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(seed.Label)
                .Append(" pos=").Append(FormatVector(seed.Position))
                .Append(" r=").Append(seed.Radius.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" dRaw=").Append(seed.DistanceToRawCenter.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" dVol=").Append(seed.DistanceToColliderVolume.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" inside=").Append(seed.InsideCollider)
                .Append(seed.Kept ? " kept" : " filtered")
                .Append(':').Append(seed.FilterReason);
            if (seed.Kept)
            {
                text.Append(" sampled=").Append(seed.Sampled);
                if (seed.Sampled) text.Append(" hit=").Append(FormatVector(seed.SampleHit));
            }
            return text.Append('}').ToString();
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
