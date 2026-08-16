using System;
using System.Collections.Generic;

namespace ErenshorFollow
{
    // Pure, Unity-free policy used by both runtime route planning and deterministic tests.
    // Runtime code supplies measurements; this file only decides whether a candidate is useful and
    // how candidates compare. Keeping it free of NavMesh/GameObject types makes the safety rules testable.
    internal static class RouteCandidatePolicy
    {
        internal const float PartialEndpointNearCrossing = 6.0f;
        internal const float CompleteApproachNearCrossing = 8.0f;
        internal const float NativeProbeApproachNearCrossing = 8.0f;
        internal const float PartialMinimumProgressFloor = 1.0f;
        internal const float PartialMinimumProgressCeiling = 3.0f;
        internal const float PartialMinimumProgressFraction = 0.20f;

        internal enum PathKind { Invalid, Partial, Complete }
        internal enum AcceptanceKind { Rejected, NativeProof, PartialNearCrossing, Complete }

        internal sealed class DestinationNameCandidate
        {
            internal readonly string CanonicalName;
            internal readonly bool Active;
            internal readonly bool RemoveParty;

            internal DestinationNameCandidate(string canonicalName, bool active, bool removeParty)
            {
                CanonicalName = canonicalName;
                Active = active;
                RemoveParty = removeParty;
            }
        }

        internal sealed class Candidate
        {
            internal string StableKey;
            internal bool Active = true;
            internal bool RemoveParty;
            internal bool Sampled;
            internal PathKind Path;
            internal int CornerCount;
            internal float StartDistanceToCrossing;
            internal float EndpointDistanceToCrossing;
            internal float ApproachDistanceToCrossing;
            internal float RouteLength;
        }

        internal sealed class Evaluation
        {
            internal readonly Candidate Candidate;
            internal readonly AcceptanceKind Acceptance;
            internal readonly bool Accepted;
            internal readonly bool NeedsNativeProof;
            internal readonly string Reason;

            internal Evaluation(Candidate candidate, AcceptanceKind acceptance, string reason)
            {
                Candidate = candidate;
                Acceptance = acceptance;
                Accepted = acceptance != AcceptanceKind.Rejected;
                NeedsNativeProof = acceptance == AcceptanceKind.NativeProof;
                Reason = reason;
            }
        }

        internal static string ResolveCanonicalName(IList<DestinationNameCandidate> candidates, string requested,
            out bool ambiguous, out bool removingOnly)
        {
            ambiguous = false;
            removingOnly = false;
            if (candidates == null || string.IsNullOrWhiteSpace(requested)) return null;

            string query = requested.Trim();
            List<string> exactUsable = new List<string>();
            bool exactRemoving = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                DestinationNameCandidate candidate = candidates[i];
                if (candidate == null || !candidate.Active || string.IsNullOrWhiteSpace(candidate.CanonicalName)) continue;
                string name = candidate.CanonicalName.Trim();
                if (!name.Equals(query, StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate.RemoveParty) exactRemoving = true;
                else AddDistinct(exactUsable, name);
            }
            if (exactUsable.Count > 0) return exactUsable[0];
            if (exactRemoving)
            {
                removingOnly = true;
                return null;
            }

            List<string> partialUsable = new List<string>();
            bool partialRemoving = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                DestinationNameCandidate candidate = candidates[i];
                if (candidate == null || !candidate.Active || string.IsNullOrWhiteSpace(candidate.CanonicalName)) continue;
                string name = candidate.CanonicalName.Trim();
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (candidate.RemoveParty) partialRemoving = true;
                else AddDistinct(partialUsable, name);
            }
            if (partialUsable.Count == 1) return partialUsable[0];
            if (partialUsable.Count > 1)
            {
                ambiguous = true;
                return null;
            }
            removingOnly = partialRemoving;
            return null;
        }

        internal static Evaluation Evaluate(Candidate candidate)
        {
            if (candidate == null) return new Evaluation(null, AcceptanceKind.Rejected, "candidate missing");
            if (!candidate.Active) return new Evaluation(candidate, AcceptanceKind.Rejected, "crossing inactive");
            if (candidate.RemoveParty) return new Evaluation(candidate, AcceptanceKind.Rejected, "crossing removes party");
            if (!candidate.Sampled) return new Evaluation(candidate, AcceptanceKind.Rejected, "no nearby NavMesh approach");

            if (candidate.Path == PathKind.Complete && candidate.CornerCount >= 2)
            {
                if (candidate.ApproachDistanceToCrossing <= CompleteApproachNearCrossing)
                    return new Evaluation(candidate, AcceptanceKind.Complete, "complete NavMesh route to a verified crossing approach");
                return new Evaluation(candidate, AcceptanceKind.Rejected, "complete path ends at a sampled point too far from the verified crossing");
            }

            if (candidate.Path == PathKind.Partial && candidate.CornerCount >= 2)
            {
                float progress = candidate.StartDistanceToCrossing - candidate.EndpointDistanceToCrossing;
                float required = RequiredPartialProgress(candidate.StartDistanceToCrossing);
                if (candidate.EndpointDistanceToCrossing <= PartialEndpointNearCrossing && progress >= required)
                    return new Evaluation(candidate, AcceptanceKind.PartialNearCrossing,
                        "partial route makes meaningful progress and ends near the verified crossing");
                if (candidate.EndpointDistanceToCrossing > PartialEndpointNearCrossing)
                    return new Evaluation(candidate, AcceptanceKind.Rejected,
                        "partial endpoint is too far from the verified crossing");
                return new Evaluation(candidate, AcceptanceKind.Rejected,
                    "partial route does not make meaningful progress toward the verified crossing");
            }

            // A verified crossing with a sampled approach very near its real trigger/geometry gets one
            // bounded native-navigation attempt. This is deliberately ranked below proven complete/partial
            // paths and still requires observed movement before it is trusted.
            if (candidate.ApproachDistanceToCrossing <= NativeProbeApproachNearCrossing)
                return new Evaluation(candidate, AcceptanceKind.NativeProof,
                    "startup NavMesh preflight is inconclusive; bounded native-navigation proof allowed");

            return new Evaluation(candidate, AcceptanceKind.Rejected, "no useful local route evidence");
        }

        internal static List<Evaluation> RankAccepted(IList<Candidate> candidates)
        {
            List<Evaluation> accepted = new List<Evaluation>();
            if (candidates == null) return accepted;
            for (int i = 0; i < candidates.Count; i++)
            {
                Evaluation evaluation = Evaluate(candidates[i]);
                if (evaluation.Accepted) accepted.Add(evaluation);
            }
            accepted.Sort(CompareEvaluations);
            return accepted;
        }

        internal static int CompareEvaluations(Evaluation a, Evaluation b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int tier = RankTier(a.Acceptance).CompareTo(RankTier(b.Acceptance));
            if (tier != 0) return tier;

            Candidate ac = a.Candidate;
            Candidate bc = b.Candidate;
            int route = SafeMetric(ac == null ? float.MaxValue : ac.RouteLength)
                .CompareTo(SafeMetric(bc == null ? float.MaxValue : bc.RouteLength));
            if (route != 0) return route;

            int endpoint = SafeMetric(ac == null ? float.MaxValue : ac.EndpointDistanceToCrossing)
                .CompareTo(SafeMetric(bc == null ? float.MaxValue : bc.EndpointDistanceToCrossing));
            if (endpoint != 0) return endpoint;

            int approach = SafeMetric(ac == null ? float.MaxValue : ac.ApproachDistanceToCrossing)
                .CompareTo(SafeMetric(bc == null ? float.MaxValue : bc.ApproachDistanceToCrossing));
            if (approach != 0) return approach;

            return string.Compare(ac == null ? string.Empty : ac.StableKey,
                bc == null ? string.Empty : bc.StableKey, StringComparison.Ordinal);
        }

        internal static float RequiredPartialProgress(float startDistanceToCrossing)
        {
            float proportional = Math.Max(0f, startDistanceToCrossing) * PartialMinimumProgressFraction;
            return Math.Max(PartialMinimumProgressFloor, Math.Min(PartialMinimumProgressCeiling, proportional));
        }

        // Why a travel leg stopped, as a semantic fact supplied by the failure site itself. Never inferred
        // by parsing a reason string: the same route-failure funnel carries crossing-specific failures and
        // ordinary travel-execution failures (regrouping, player follow, native path invalidation), and
        // those must not all claim the crossing could not be reached.
        internal enum RouteFailureKind
        {
            NoAcceptedRoute,
            CrossingApproachFailed,
            CrossingTransitionFailed,
            TravelExecutionFailed
        }

        // Acceptance is authoritative: if no approach ever passed RouteCandidatePolicy this leg, the failure
        // is NoAcceptedRoute regardless of what the call site believed. A site that has an accepted route but
        // no specific crossing claim degrades to TravelExecutionFailed rather than overstating.
        internal static RouteFailureKind ResolveFailureKind(bool hadAcceptedCandidate, RouteFailureKind siteKind)
        {
            if (!hadAcceptedCandidate) return RouteFailureKind.NoAcceptedRoute;
            return siteKind == RouteFailureKind.NoAcceptedRoute ? RouteFailureKind.TravelExecutionFailed : siteKind;
        }

        // A terminal expedition failure must not say "no walkable route" when a verified, initially-complete
        // NavMesh path to a crossing approach genuinely existed -- that phrasing implies route discovery
        // itself failed. Equally, it must not claim a crossing could not be reached when travel actually
        // failed for an unrelated reason. Kept Unity-free and pure so the wording is covered by
        // deterministic tests rather than only exercised live.
        internal static string DescribeRouteFailure(string destinationName, RouteFailureKind kind, string reason)
        {
            string target = string.IsNullOrWhiteSpace(destinationName) ? "the destination" : destinationName.Trim();
            switch (kind)
            {
                case RouteFailureKind.CrossingApproachFailed:
                    return "could not reach a valid crossing approach to " + target + FailureDetail(reason);
                case RouteFailureKind.CrossingTransitionFailed:
                    return "native crossing to " + target + " did not complete" + FailureDetail(reason);
                case RouteFailureKind.TravelExecutionFailed:
                    return "travel to " + target + " failed" + FailureDetail(reason);
                default:
                    return "no walkable route to " + target + ".";
            }
        }

        private static string FailureDetail(string reason)
        {
            return string.IsNullOrWhiteSpace(reason) ? "." : " (" + reason.Trim() + ").";
        }

        private static int RankTier(AcceptanceKind kind)
        {
            switch (kind)
            {
                case AcceptanceKind.Complete: return 0;
                case AcceptanceKind.PartialNearCrossing: return 1;
                case AcceptanceKind.NativeProof: return 2;
                default: return 3;
            }
        }

        private static float SafeMetric(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? float.MaxValue : Math.Max(0f, value);
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (values.Exists(delegate(string x) { return x.Equals(value, StringComparison.OrdinalIgnoreCase); })) return;
            values.Add(value);
        }
    }
}
