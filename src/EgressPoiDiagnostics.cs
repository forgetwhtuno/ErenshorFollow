using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    // READ-ONLY scene inspection for `/elead egress`.
    //
    // This command exists to answer one architectural question with live evidence instead of
    // inference: do current Erenshor scenes author PointOfInterest entries with
    // Use == POIType.zoneline that could serve as first-class Expedition crossing targets, and do
    // those POIs associate unambiguously with a specific live Zoneline?
    //
    // It changes NOTHING. It never assigns MyPOI or MyTask, never issues a movement order, never
    // touches grouping, never calls Flee/RunAway/ZoneSim/SceneChange, and never warps anything. It
    // runs only when the player types the command - there is no per-frame work and no Update hook.
    // Every enumeration below is bounded by an explicit cap so a dense scene cannot produce an
    // unbounded chat dump.
    internal static class EgressPoiDiagnostics
    {
        private const int MaxPoisReported = 24;
        private const int MaxZonelinesReported = 12;
        private const int MaxCollidersPerCrossing = 4;
        private const int MaxCandidatesPerPoi = 4;
        // Follow's own planner radius, reported alongside the native 2m result so the two acceptance
        // models can be compared directly. Neither is changed by this pass.
        private const float FollowSampleRadius = 4f;
        // A tight probe answering "is the authored transform itself already essentially on NavMesh",
        // distinct from "is there NavMesh within the native 2m sphere".
        private const float OnMeshProbeRadius = 0.5f;

        private static readonly NavMeshPath ProbePath = new NavMeshPath();

        private sealed class EgressRecord
        {
            internal int Index;
            internal PointOfInterest Poi;
            internal Vector3 Position;
            internal string AreaName;
            internal int LvlRec;
            internal bool ActiveInHierarchy;
            internal bool InEgressList;
            internal bool NativeSampled;
            internal Vector3 NativeHit;
            internal float VerticalDifference;
            internal bool NativeAccepted;
            internal bool FollowSampled;
            internal Vector3 FollowHit;
            internal bool RawAlreadyOnMesh;
            internal readonly List<Candidate> Candidates = new List<Candidate>();
            internal EgressAssociationPolicy.AssociationKind Association =
                EgressAssociationPolicy.AssociationKind.None;
        }

        private sealed class Candidate
        {
            internal string Key;
            internal Zoneline Crossing;
            internal string DestinationZone;
            internal bool Inside;
            internal float VolumeDistance;
            internal float RawDistance;
            internal Vector3 NearestColliderPoint;
        }

        internal static void Report()
        {
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                Say("[Erenshor Egress Diag] Scene: " + Safe(scene) + " (READ-ONLY; no movement, POI, task, or group state is modified)");

                List<Zoneline> crossings = CollectCrossings();
                List<EgressRecord> records = CollectEgressPois(crossings);

                Say("[Erenshor Egress Diag] EgressLocations=" + CountEgressList() +
                    " zonelinePOIsInScene=" + records.Count +
                    " liveZonelines=" + crossings.Count);

                ReportCrossings(crossings);

                if (records.Count == 0)
                {
                    Say("[Erenshor Egress Diag] No PointOfInterest with Use=zoneline exists in this scene. " +
                        "Native Sims here would fall back to a random POI or have no egress at all.");
                }

                Vector3 start;
                string startLabel;
                bool startSampled = ResolveRouteStart(out start, out startLabel);
                Say("[Erenshor Egress Diag] Route start: " + startLabel + " " + FormatVector(start) +
                    " sampled=" + startSampled);

                int reported = 0;
                for (int i = 0; i < records.Count && reported < MaxPoisReported; i++, reported++)
                    ReportPoi(records[i], start, startSampled);
                if (records.Count > reported)
                    Say("[Erenshor Egress Diag] +" + (records.Count - reported) + " further zoneline POI(s) not listed (report cap).");

                ReportReverseView(crossings, records);
            }
            catch (Exception ex)
            {
                Say("[Erenshor Egress Diag] Diagnostic failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // ---- collection -------------------------------------------------------------------------

        private static int CountEgressList()
        {
            try { return GameData.EgressLocations == null ? 0 : GameData.EgressLocations.Count; }
            catch { return 0; }
        }

        // GameData.EgressLocations is populated by PointOfInterest.Awake and cleared by
        // SceneChange, so it should already hold exactly the zoneline POIs. POI.POIs is scanned as
        // well and any divergence is reported, because a POI that failed to register would be
        // invisible to native egress selection and that itself is evidence worth having.
        private static List<EgressRecord> CollectEgressPois(List<Zoneline> crossings)
        {
            List<EgressRecord> records = new List<EgressRecord>();
            List<PointOfInterest> seen = new List<PointOfInterest>();

            try
            {
                if (GameData.EgressLocations != null)
                {
                    for (int i = 0; i < GameData.EgressLocations.Count; i++)
                        AddPoi(records, seen, GameData.EgressLocations[i], true, crossings);
                }
            }
            catch { }

            try
            {
                if (POI.POIs != null)
                {
                    for (int i = 0; i < POI.POIs.Count; i++)
                    {
                        PointOfInterest poi = POI.POIs[i];
                        if (poi == null || poi.Use != PointOfInterest.POIType.zoneline) continue;
                        AddPoi(records, seen, poi, false, crossings);
                    }
                }
            }
            catch { }

            records.Sort(delegate(EgressRecord a, EgressRecord b)
            {
                return string.Compare(PoiKey(a.Poi), PoiKey(b.Poi), StringComparison.Ordinal);
            });
            for (int i = 0; i < records.Count; i++) records[i].Index = i;
            return records;
        }

        private static void AddPoi(List<EgressRecord> records, List<PointOfInterest> seen,
            PointOfInterest poi, bool fromEgressList, List<Zoneline> crossings)
        {
            if (poi == null || poi.transform == null) return;
            for (int i = 0; i < seen.Count; i++)
            {
                if (!ReferenceEquals(seen[i], poi)) continue;
                if (fromEgressList) records[i].InEgressList = true;
                return;
            }
            seen.Add(poi);

            EgressRecord record = new EgressRecord();
            record.Poi = poi;
            record.InEgressList = fromEgressList;
            record.Position = poi.transform.position;
            record.AreaName = poi.AreaName;
            record.LvlRec = poi.LvlRec;
            record.ActiveInHierarchy = poi.gameObject != null && poi.gameObject.activeInHierarchy;

            NavMeshHit hit;
            record.NativeSampled = NavMesh.SamplePosition(record.Position, out hit,
                EgressAssociationPolicy.NativeSampleRadius, NavMesh.AllAreas);
            record.NativeHit = record.NativeSampled ? hit.position : record.Position;
            record.VerticalDifference = record.NativeSampled ? record.NativeHit.y - record.Position.y : float.NaN;
            record.NativeAccepted = EgressAssociationPolicy.NativeSampleAccepted(
                record.NativeSampled, record.VerticalDifference);

            NavMeshHit wide;
            record.FollowSampled = NavMesh.SamplePosition(record.Position, out wide, FollowSampleRadius, NavMesh.AllAreas);
            record.FollowHit = record.FollowSampled ? wide.position : record.Position;

            NavMeshHit tight;
            record.RawAlreadyOnMesh = NavMesh.SamplePosition(record.Position, out tight, OnMeshProbeRadius, NavMesh.AllAreas);

            BuildCandidates(record, crossings);
            records.Add(record);
        }

        private static List<Zoneline> CollectCrossings()
        {
            List<Zoneline> crossings = new List<Zoneline>();
            try
            {
                Zoneline[] all = UnityEngine.Object.FindObjectsOfType<Zoneline>();
                for (int i = 0; i < all.Length; i++)
                {
                    Zoneline line = all[i];
                    if (line == null || line.gameObject == null || !line.gameObject.activeInHierarchy) continue;
                    crossings.Add(line);
                }
            }
            catch { }
            crossings.Sort(delegate(Zoneline a, Zoneline b)
            {
                return string.Compare(CrossingKey(a), CrossingKey(b), StringComparison.Ordinal);
            });
            return crossings;
        }

        // ---- association ------------------------------------------------------------------------

        private static void BuildCandidates(EgressRecord record, List<Zoneline> crossings)
        {
            for (int i = 0; i < crossings.Count; i++)
            {
                Zoneline crossing = crossings[i];
                if (crossing == null) continue;
                Candidate candidate = new Candidate();
                candidate.Crossing = crossing;
                candidate.Key = CrossingKey(crossing);
                candidate.DestinationZone = crossing.DestinationZone;
                candidate.RawDistance = crossing.transform == null
                    ? float.MaxValue
                    : Vector3.Distance(record.Position, crossing.transform.position);
                candidate.VolumeDistance = MeasureToVolume(record.Position, crossing,
                    out candidate.Inside, out candidate.NearestColliderPoint);
                record.Candidates.Add(candidate);
            }

            record.Candidates.Sort(delegate(Candidate a, Candidate b)
            {
                return EgressAssociationPolicy.CompareCandidates(a.Inside, a.VolumeDistance, a.Key,
                    b.Inside, b.VolumeDistance, b.Key);
            });

            Candidate best = record.Candidates.Count > 0 ? record.Candidates[0] : null;
            Candidate second = record.Candidates.Count > 1 ? record.Candidates[1] : null;
            record.Association = EgressAssociationPolicy.Classify(record.Candidates.Count,
                best != null && best.Inside, best == null ? float.MaxValue : best.VolumeDistance,
                second != null && second.Inside, second == null ? float.MaxValue : second.VolumeDistance,
                EgressAssociationPolicy.UniqueAssociationMargin,
                EgressAssociationPolicy.MaxAssociationDistance);
        }

        // Distance to the crossing's REAL trigger volume via the live Collider.ClosestPoint, which
        // honours rotation and non-uniform scale. This is deliberately NOT the raw transform
        // distance: on a large oriented trigger the two differ by tens of metres, and measuring to
        // the centre is the exact defect that made earlier passes mis-associate geometry.
        private static float MeasureToVolume(Vector3 point, Zoneline crossing, out bool inside, out Vector3 nearest)
        {
            inside = false;
            nearest = point;
            if (crossing == null || crossing.gameObject == null) return float.MaxValue;
            float best = float.MaxValue;
            try
            {
                Collider[] colliders = crossing.GetComponentsInChildren<Collider>(true);
                int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
                for (int i = 0; i < limit; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null || !collider.enabled || !collider.isTrigger) continue;
                    if (collider.gameObject == null || !collider.gameObject.activeInHierarchy) continue;
                    Vector3 closest;
                    try { closest = collider.ClosestPoint(point); }
                    catch { closest = collider.bounds.ClosestPoint(point); }
                    float distance = Vector3.Distance(point, closest);
                    if (distance >= best) continue;
                    best = distance;
                    nearest = closest;
                    inside = distance <= 0.01f;
                }
            }
            catch { }
            return best;
        }

        // ---- reporting --------------------------------------------------------------------------

        private static void ReportCrossings(List<Zoneline> crossings)
        {
            int reported = 0;
            for (int i = 0; i < crossings.Count && reported < MaxZonelinesReported; i++, reported++)
            {
                Zoneline crossing = crossings[i];
                StringBuilder text = new StringBuilder();
                text.Append("[Erenshor Egress Diag] crossing ").Append(CrossingKey(crossing))
                    .Append(" dest=").Append(Safe(crossing.DestinationZone))
                    .Append(" removeParty=").Append(crossing.RemoveParty)
                    .Append(" rawPos=").Append(FormatVector(crossing.transform == null ? Vector3.zero : crossing.transform.position))
                    .Append(" landing=").Append(FormatVector(crossing.LandingPosition));
                try
                {
                    Collider[] colliders = crossing.GetComponentsInChildren<Collider>(true);
                    int limit = Math.Min(MaxCollidersPerCrossing, colliders.Length);
                    int triggers = 0;
                    for (int c = 0; c < limit; c++)
                    {
                        Collider collider = colliders[c];
                        if (collider == null || !collider.enabled || !collider.isTrigger) continue;
                        triggers++;
                        text.Append(" {").Append(collider.GetType().Name)
                            .Append(" worldSize=").Append(FormatVector(collider.bounds.size)).Append('}');
                    }
                    text.Append(" triggers=").Append(triggers).Append('/').Append(colliders.Length);
                }
                catch { }
                Say(text.ToString());
            }
            if (crossings.Count > reported)
                Say("[Erenshor Egress Diag] +" + (crossings.Count - reported) + " further live Zoneline(s) not listed (report cap).");
        }

        private static void ReportPoi(EgressRecord record, Vector3 start, bool startSampled)
        {
            StringBuilder text = new StringBuilder();
            text.Append("[Erenshor Egress Diag] poi").Append(record.Index.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(PoiKey(record.Poi))
                .Append(" pos=").Append(FormatVector(record.Position))
                .Append(" areaName=").Append(Safe(record.AreaName))
                .Append(" lvlRec=").Append(record.LvlRec.ToString(CultureInfo.InvariantCulture))
                .Append(" active=").Append(record.ActiveInHierarchy)
                .Append(" inEgressList=").Append(record.InEgressList)
                .Append(" native2m=").Append(record.NativeSampled ? "PASS" : "FAIL")
                .Append(" nativeAccepted=").Append(record.NativeAccepted)
                .Append(" hit=").Append(FormatVector(record.NativeHit))
                .Append(" dY=").Append(Metres(record.VerticalDifference))
                .Append(" rawOnMesh=").Append(record.RawAlreadyOnMesh)
                .Append(" follow4m=").Append(record.FollowSampled ? "PASS" : "FAIL");
            Say(text.ToString());

            Candidate best = record.Candidates.Count > 0 ? record.Candidates[0] : null;
            Say("[Erenshor Egress Diag]   " + EgressAssociationPolicy.DescribeAssociation(record.Association,
                best == null ? null : best.Key + "->" + Safe(best.DestinationZone),
                best != null && best.Inside,
                best == null ? float.MaxValue : best.VolumeDistance,
                best == null ? float.MaxValue : best.RawDistance));

            int shown = Math.Min(MaxCandidatesPerPoi, record.Candidates.Count);
            for (int i = 0; i < shown; i++)
            {
                Candidate candidate = record.Candidates[i];
                Say("[Erenshor Egress Diag]   candidate" + i.ToString(CultureInfo.InvariantCulture) +
                    " " + candidate.Key + " dest=" + Safe(candidate.DestinationZone) +
                    " dVol=" + Metres(candidate.VolumeDistance) +
                    " dRaw=" + Metres(candidate.RawDistance) +
                    " inside=" + candidate.Inside +
                    " nearestColliderPoint=" + FormatVector(candidate.NearestColliderPoint));
            }

            if (!startSampled || !record.NativeSampled) return;
            string status;
            int corners;
            float length;
            DescribePath(start, record.NativeHit, out status, out corners, out length);
            Say("[Erenshor Egress Diag]   pathFromStart=" + status +
                " corners=" + corners.ToString(CultureInfo.InvariantCulture) +
                " routeLength=" + Metres(length));
        }

        // Per-crossing view, so "does the Vitheo exit have an egress POI at all" is answerable
        // directly rather than by reading every POI line.
        private static void ReportReverseView(List<Zoneline> crossings, List<EgressRecord> records)
        {
            int reported = 0;
            for (int i = 0; i < crossings.Count && reported < MaxZonelinesReported; i++, reported++)
            {
                Zoneline crossing = crossings[i];
                string key = CrossingKey(crossing);
                List<string> matches = new List<string>();
                for (int r = 0; r < records.Count; r++)
                {
                    EgressRecord record = records[r];
                    if (record.Association != EgressAssociationPolicy.AssociationKind.Unique) continue;
                    if (record.Candidates.Count == 0 || record.Candidates[0].Key != key) continue;
                    matches.Add("poi" + record.Index.ToString(CultureInfo.InvariantCulture) +
                        (record.NativeAccepted ? "(nativeOK)" : "(nativeFAIL)"));
                }
                Say("[Erenshor Egress Diag] exit " + key + " dest=" + Safe(crossing.DestinationZone) +
                    " uniquelyAssociatedPOIs=" + matches.Count +
                    (matches.Count == 0 ? "" : " [" + string.Join(", ", matches.ToArray()) + "]"));
            }
        }

        // ---- helpers ----------------------------------------------------------------------------

        // The live start the report measures from: the active expedition leader when one exists,
        // otherwise the player. Read only - no order is issued to either.
        private static bool ResolveRouteStart(out Vector3 start, out string label)
        {
            start = Vector3.zero;
            label = "<none>";
            try
            {
                SimPlayer leader = LeaderController.CurrentLeader;
                if (leader != null && leader.transform != null)
                {
                    start = leader.transform.position;
                    label = "leader";
                }
                else if (GameData.PlayerControl != null && GameData.PlayerControl.transform != null)
                {
                    start = GameData.PlayerControl.transform.position;
                    label = "player";
                }
                else return false;
            }
            catch { return false; }

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(start, out hit, 5f, NavMesh.AllAreas)) return false;
            start = hit.position;
            return true;
        }

        private static void DescribePath(Vector3 from, Vector3 to, out string status, out int corners, out float length)
        {
            status = "Invalid";
            corners = 0;
            length = float.NaN;
            try
            {
                if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, ProbePath)) return;
                if (ProbePath.corners == null || ProbePath.corners.Length < 2)
                {
                    status = ProbePath.status.ToString();
                    return;
                }
                status = ProbePath.status.ToString();
                corners = ProbePath.corners.Length;
                float total = 0f;
                for (int i = 1; i < ProbePath.corners.Length; i++)
                    total += Vector3.Distance(ProbePath.corners[i - 1], ProbePath.corners[i]);
                length = total;
            }
            catch { }
        }

        private static string PoiKey(PointOfInterest poi)
        {
            if (poi == null || poi.transform == null) return "<null>";
            return Safe(poi.transform.name) + "#" + poi.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        private static string CrossingKey(Zoneline crossing)
        {
            if (crossing == null || crossing.transform == null) return "<null>";
            return Safe(crossing.transform.name) + "#" + crossing.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                value.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                value.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        }

        private static string Metres(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value >= float.MaxValue) return "n/a";
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<unknown>" : value.Trim();
        }

        private static void Say(string message)
        {
            try
            {
                if (ErenshorFollowPlugin.Instance != null)
                {
                    ErenshorFollowPlugin.Instance.Chat(message, "lightblue");
                    ErenshorFollowPlugin.Instance.LogDebug(message);
                }
            }
            catch { }
        }
    }

    // Command routing only, mirroring the existing `/elead diag` owner so this pass does not disturb
    // the deterministic chat-routing logic. Runs on an explicit typed command; never per frame.
    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class EgressDiagnosticCommandPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 210)]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                if (__instance == null || __instance.typed == null || string.IsNullOrWhiteSpace(__instance.typed.text)) return true;
                string raw = __instance.typed.text.Trim();
                const string prefix = "/elead egress";
                if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    (raw.Length > prefix.Length && !char.IsWhiteSpace(raw[prefix.Length]))) return true;
                __instance.typed.text = string.Empty;
                EgressPoiDiagnostics.Report();
                return false;
            }
            catch (Exception ex)
            {
                try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogError("Egress diagnostic failed: " + ex); } catch { }
                return true;
            }
        }
    }
}
