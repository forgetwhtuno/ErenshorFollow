using System;

namespace ErenshorFollow
{
    internal static class PostZoneRouteReadinessPolicyTests
    {
        private static int _passed;

        private static void Assert(bool value, string name)
        {
            if (!value) throw new Exception("FAILED: " + name);
            _passed++;
        }

        private static PostZoneRouteReadinessInputs Ready()
        {
            PostZoneRouteReadinessInputs input = new PostZoneRouteReadinessInputs();
            input.AttemptCount = 1;
            input.AtlasRouteAvailable = true;
            input.NextLegResolved = true;
            input.LiveCrossingCount = 1;
            input.StartSampled = true;
            input.AcceptedCandidateCount = 1;
            return input;
        }

        public static int Main()
        {
            PostZoneRouteReadinessInputs input = Ready();
            Assert(PostZoneRouteReadinessPolicy.Evaluate(input) == PostZoneRouteReadinessDecision.Ready,
                "fully fresh route evidence starts the next leg");

            input.AcceptedCandidateCount = 0;
            Assert(PostZoneRouteReadinessPolicy.Evaluate(input) == PostZoneRouteReadinessDecision.Waiting,
                "zero accepted candidates immediately after zoning is retryable");
            Assert(PostZoneRouteReadinessPolicy.DescribePending(input).IndexOf("NavMesh policy", StringComparison.Ordinal) >= 0,
                "candidate rejection has a precise pending classification");

            input.LiveCrossingCount = 0;
            Assert(PostZoneRouteReadinessPolicy.Evaluate(input) == PostZoneRouteReadinessDecision.Waiting,
                "temporarily absent Zoneline is retryable before the bound");
            input.ElapsedSeconds = PostZoneRouteReadinessPolicy.TimeoutSeconds;
            Assert(PostZoneRouteReadinessPolicy.Evaluate(input) == PostZoneRouteReadinessDecision.Failed,
                "unready geometry becomes terminal only at the bounded timeout");

            input = Ready();
            input.AttemptCount = PostZoneRouteReadinessPolicy.MaximumAttempts;
            input.AcceptedCandidateCount = 0;
            Assert(PostZoneRouteReadinessPolicy.Evaluate(input) == PostZoneRouteReadinessDecision.Failed,
                "retry count is bounded even when game time stalls");
            Console.WriteLine("PostZoneRouteReadinessPolicyTests: PASS (" + _passed + " assertions)");
            return 0;
        }
    }
}
