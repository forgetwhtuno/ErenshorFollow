using System;

namespace ErenshorFollow
{
    internal static class ExpeditionMovementPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static ExpeditionMovementObservation Healthy(float elapsed)
        {
            ExpeditionMovementObservation value = new ExpeditionMovementObservation();
            value.NpcResolved = true;
            value.AgentPresent = true;
            value.AgentEnabled = true;
            value.AgentOnNavMesh = true;
            value.DestinationAccepted = true;
            value.HasPath = true;
            value.ElapsedSeconds = elapsed;
            // These fixtures all describe a leader still en route to its ordered point, so they must
            // carry a real mid-route remaining distance. Leaving this at the struct default of 0
            // would mean "standing on the target", which is arrival, not a stall - the very
            // distinction this policy now makes. Production never passes an unmeasured 0 either: it
            // initializes DistanceToTarget to float.MaxValue and only lowers it from a real leader
            // position (see LeaderController.CaptureMovementObservation).
            value.DistanceToTarget = 25f;
            return value;
        }

        private static void ProgressWins()
        {
            ExpeditionMovementObservation o = Healthy(0.5f);
            o.MovedDistance = 0.6f;
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.ProgressObserved,
                "visible leader movement proves native order ownership");
        }

        private static void CombatYields()
        {
            ExpeditionMovementObservation o = Healthy(3f);
            o.CombatControl = true;
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.Waiting &&
                issue == ExpeditionMovementIssue.CombatControl, "combat keeps native movement priority");
        }

        private static void SittingOrStoppedGetsBoundedReissueThenFailure()
        {
            ExpeditionMovementObservation o = Healthy(2f);
            o.AgentStopped = true;
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.ReissueNativeOrder &&
                issue == ExpeditionMovementIssue.AgentStopped, "stopped/sitting startup gets a bounded native-order reissue");
            o.ElapsedSeconds = 5f;
            o.ReissueCount = ExpeditionMovementPolicy.MaximumReissues;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.FailMovementOwnership &&
                issue == ExpeditionMovementIssue.AgentStopped, "persistently stopped movement fails ownership instead of cycling routes");
        }

        private static void MissingNpcFailsOwnership()
        {
            ExpeditionMovementObservation o = Healthy(2f);
            o.NpcResolved = false;
            o.AgentPresent = false;
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.FailMovementOwnership &&
                issue == ExpeditionMovementIssue.NpcUnavailable, "missing native NPC owner fails closed");
        }

        private static void InvalidPathTriesGeometryCandidate()
        {
            ExpeditionMovementObservation o = Healthy(2f);
            o.PathInvalid = true;
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.TryNextRouteCandidate &&
                issue == ExpeditionMovementIssue.PathInvalid, "invalid native path may try the next verified approach candidate");
        }

        private static void DestinationNotAppliedIsDiagnosed()
        {
            ExpeditionMovementObservation o = Healthy(2f);
            o.HasPath = false;
            o.DestinationAccepted = false;
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(o, out issue) == ExpeditionMovementDecision.ReissueNativeOrder &&
                issue == ExpeditionMovementIssue.DestinationNotApplied, "unretained destination is distinguished from bad route geometry");
        }

        // Live Failure A: the leader reached its zoneline approach and reported
        // destination=0.0m-from-order, PathComplete, velocity=0.00, desiredVelocity=0.00. Because the
        // observation carried no remaining-distance, arrival was numerically identical to a stall, so
        // the order was reissued 1/2 then 2/2 and the route failed with "the native movement owner
        // made no useful progress". Arrival must instead hand off to trigger traversal.
        private static ExpeditionMovementObservation ArrivedAtApproach()
        {
            ExpeditionMovementObservation o = new ExpeditionMovementObservation();
            o.NpcResolved = true;
            o.AgentPresent = true;
            o.AgentEnabled = true;
            o.AgentOnNavMesh = true;
            o.HasPath = true;
            o.DestinationAccepted = true;
            o.ElapsedSeconds = 3f;      // past the observation grace
            o.MovedDistance = 0f;       // standing still, because it has arrived
            o.DistanceImprovement = 0f; // no further improvement possible
            o.VelocityMagnitude = 0f;   // velocity=0.00 exactly as logged
            o.DistanceToTarget = 0f;    // destination=0.0m-from-order
            return o;
        }

        private static void ArrivalHandsOffToTraversal()
        {
            ExpeditionMovementIssue issue;
            Assert(ExpeditionMovementPolicy.Evaluate(ArrivedAtApproach(), out issue) ==
                   ExpeditionMovementDecision.ArrivedAtTarget &&
                   issue == ExpeditionMovementIssue.ApproachReachedTraversalPending,
                "reaching the approach reports arrival/traversal-pending, not a movement stall");
        }

        private static void ArrivalIsNotReissuedOrFailed()
        {
            // The exact live sequence: even after the reissue budget is spent and the failure
            // deadline has passed, an arrived leader must never be reissued or failed.
            for (int reissues = 0; reissues <= ExpeditionMovementPolicy.MaximumReissues; reissues++)
            {
                ExpeditionMovementObservation o = ArrivedAtApproach();
                o.ReissueCount = reissues;
                o.ElapsedSeconds = ExpeditionMovementPolicy.FailureSeconds + 5f;
                ExpeditionMovementIssue issue;
                ExpeditionMovementDecision decision = ExpeditionMovementPolicy.Evaluate(o, out issue);
                Assert(decision != ExpeditionMovementDecision.ReissueNativeOrder,
                    "an arrived leader must never have the same approach order reissued");
                Assert(decision != ExpeditionMovementDecision.FailMovementOwnership,
                    "an arrived leader must never fail as a movement-ownership problem");
                Assert(decision == ExpeditionMovementDecision.ArrivedAtTarget,
                    "arrival stays arrival regardless of elapsed time or reissue count");
            }
        }

        private static void ArrivalRadiusMatchesCrossingHandoff()
        {
            // Arrival and the crossing machine's approach-ready distance must agree, otherwise a
            // leader could be classified as arrived in a window where the crossing state machine is
            // not yet willing to take over (or vice versa) and stall between the two.
            Assert(Math.Abs(ExpeditionMovementPolicy.ArrivalRadius - 1.75f) < 0.0001f,
                "arrival radius must match ExpeditionCrossingPolicy.ApproachReadyDistance (1.75m)");

            ExpeditionMovementObservation justInside = ArrivedAtApproach();
            justInside.DistanceToTarget = ExpeditionMovementPolicy.ArrivalRadius - 0.01f;
            ExpeditionMovementIssue insideIssue;
            Assert(ExpeditionMovementPolicy.Evaluate(justInside, out insideIssue) ==
                   ExpeditionMovementDecision.ArrivedAtTarget, "just inside the arrival radius counts as arrived");

            // Still genuinely far away and not moving: the original stall handling must be intact.
            ExpeditionMovementObservation stillFar = ArrivedAtApproach();
            stillFar.DistanceToTarget = 25f;
            stillFar.ElapsedSeconds = 3f;
            stillFar.ReissueCount = 0;
            ExpeditionMovementIssue farIssue;
            Assert(ExpeditionMovementPolicy.Evaluate(stillFar, out farIssue) ==
                   ExpeditionMovementDecision.ReissueNativeOrder,
                "a genuinely stalled leader far from its target still gets the original bounded reissue");
        }

        private static void ArrivalNeverPreemptsCombatOrRealProgress()
        {
            ExpeditionMovementObservation combat = ArrivedAtApproach();
            combat.CombatControl = true;
            ExpeditionMovementIssue combatIssue;
            Assert(ExpeditionMovementPolicy.Evaluate(combat, out combatIssue) == ExpeditionMovementDecision.Waiting,
                "native combat still outranks the arrival handoff");

            // An unknown/unmeasured distance must never be read as arrival at the origin.
            ExpeditionMovementObservation unknown = ArrivedAtApproach();
            unknown.DistanceToTarget = float.MaxValue;
            unknown.VelocityMagnitude = 5f;
            ExpeditionMovementIssue movingIssue;
            Assert(ExpeditionMovementPolicy.Evaluate(unknown, out movingIssue) ==
                   ExpeditionMovementDecision.ProgressObserved,
                "a moving leader with unknown remaining distance is still ordinary progress");
        }

        public static int Main()
        {
            ArrivalHandsOffToTraversal();
            ArrivalIsNotReissuedOrFailed();
            ArrivalRadiusMatchesCrossingHandoff();
            ArrivalNeverPreemptsCombatOrRealProgress();
            ProgressWins();
            CombatYields();
            SittingOrStoppedGetsBoundedReissueThenFailure();
            MissingNpcFailsOwnership();
            InvalidPathTriesGeometryCandidate();
            DestinationNotAppliedIsDiagnosed();
            Console.WriteLine("Expedition movement policy tests passed: " + _passed);
            return 0;
        }
    }
}
