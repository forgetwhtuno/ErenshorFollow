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

        public static int Main()
        {
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
