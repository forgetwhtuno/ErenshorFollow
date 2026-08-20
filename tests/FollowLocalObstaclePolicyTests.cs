using System;

namespace ErenshorFollow
{
    internal static class FollowLocalObstaclePolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        // 1. A player actively steering but not actually displacing - classic "running into a tree" -
        // must classify as BlockedByObstacle, which chooses the Sidestep strategy (a bounded local probe
        // attempt), not an immediate failure.
        private static void BlockedByObstacleChoosesSidestepNotFailure()
        {
            FollowStallReason reason = FollowLocalObstaclePolicy.Classify(
                pathInvalidNoCorners: false, leaderMovedSincePath: false, leaderDistance: 5f,
                steeringWasActive: true, movementSinceLastAttempt: 0.02f);
            Assert(reason == FollowStallReason.BlockedByObstacle, "actively steering with near-zero displacement classifies as blocked by obstacle");
            Assert(FollowLocalObstaclePolicy.ChooseStrategy(reason) == FollowRepathStrategy.Sidestep,
                "a blocked-by-obstacle classification chooses the local side-step recovery strategy, not an outright stop");
        }

        // 2. A moving leader invalidating a cached path is an entirely different situation from being
        // physically stuck - it must classify separately and only ever request a plain bounded repath.
        private static void MovingLeaderRequestsBoundedPlainRepath()
        {
            FollowStallReason reason = FollowLocalObstaclePolicy.Classify(
                pathInvalidNoCorners: false, leaderMovedSincePath: true, leaderDistance: 6f,
                steeringWasActive: true, movementSinceLastAttempt: 3f);
            Assert(reason == FollowStallReason.MovingTargetRepathPending, "a leader that moved since the last computed path is a moving-target repath, not a stall");
            Assert(FollowLocalObstaclePolicy.ChooseStrategy(reason) == FollowRepathStrategy.Plain,
                "a moving target never spends a local side-step probe - it just repaths toward the new position");
        }

        // 3. The trailing target sits behind the leader's heading, not on top of its exact position -
        // proving the player is never required to stand on the leader's exact spot.
        private static void TrailingTargetIsBehindLeaderNotOnItsExactPosition()
        {
            float targetX, targetZ;
            FollowLocalObstaclePolicy.TrailingTarget(100f, 50f, 1f, 0f, 2f, out targetX, out targetZ);
            Assert(Math.Abs(targetX - 98f) < 0.001f && Math.Abs(targetZ - 50f) < 0.001f,
                "trailing target sits 2m behind the leader's heading, not on its exact position (100,50)");

            // A leader with no meaningful heading (standing still / just spawned) falls back to its own
            // position rather than inventing a direction from noise.
            float stillX, stillZ;
            FollowLocalObstaclePolicy.TrailingTarget(10f, 10f, 0f, 0f, 2f, out stillX, out stillZ);
            Assert(Math.Abs(stillX - 10f) < 0.001f && Math.Abs(stillZ - 10f) < 0.001f,
                "no heading falls back to the leader's own position instead of an invented direction");
        }

        // 5. The same blocked-by-obstacle classification is what drives the recovery attempt: proves the
        // detector actually fires (not just that its downstream strategy is correct) for a range of
        // near-zero-movement values, and stays silent for genuinely healthy movement.
        private static void StuckDetectionTriggersRepath()
        {
            Assert(FollowLocalObstaclePolicy.Classify(false, false, 5f, true, 0f) == FollowStallReason.BlockedByObstacle,
                "zero displacement while steering triggers the stuck classification");
            Assert(FollowLocalObstaclePolicy.Classify(false, false, 5f, true, 0.1f) == FollowStallReason.BlockedByObstacle,
                "near-zero displacement while steering still triggers the stuck classification");
            Assert(FollowLocalObstaclePolicy.Classify(false, false, 5f, true, 1.5f) == FollowStallReason.None,
                "ordinary per-cycle movement does not trigger a stuck classification");
            Assert(FollowLocalObstaclePolicy.Classify(false, false, 5f, false, 0f) == FollowStallReason.None,
                "zero displacement while NOT actively steering (e.g. just started) is not a stuck classification");
        }

        // 7. A single stall classification alone must never cause FollowStuckRecoveryPolicy to fail the
        // expedition outright - it only ever feeds into that policy's own bounded noProgress/attempts
        // gate, which still requires real elapsed time and exhausted attempts before it stops. This
        // proves the two policies compose safely: classifying a cause is not the same as declaring
        // failure.
        private static void TemporaryStallClassificationDoesNotFailImmediately()
        {
            FollowStallReason reason = FollowLocalObstaclePolicy.Classify(false, false, 5f, true, 0f);
            Assert(reason == FollowStallReason.BlockedByObstacle, "setup: classified as blocked");
            // The very first cycle after a stall begins: no elapsed noProgress time yet, zero recovery
            // attempts spent. FollowStuckRecoveryPolicy must still say "keep going", not "stop".
            FollowStuckRecoveryDecision decision = FollowStuckRecoveryPolicy.Evaluate(
                noProgressSeconds: 0f, routeProblem: false, recoveryAttempts: 0, retryDue: true);
            Assert(decision == FollowStuckRecoveryDecision.None,
                "a fresh stall classification does not, by itself, fail an otherwise valid expedition");
        }

        // Leader-too-far and no-route remain distinct from a physical block, each with their own
        // classification so diagnostics and recovery strategy never conflate them.
        private static void LeaderTooFarAndNoRouteAreDistinctFromBlocked()
        {
            Assert(FollowLocalObstaclePolicy.Classify(false, false, 30f, true, 0.5f) == FollowStallReason.LeaderTooFar,
                "a leader far beyond the too-far distance classifies separately from a physical block");
            Assert(FollowLocalObstaclePolicy.Classify(true, false, 5f, true, 0.5f) == FollowStallReason.NoRoute,
                "an outright invalid path with no corners at all classifies as no-route, taking priority over any other signal");
            Assert(FollowLocalObstaclePolicy.ChooseStrategy(FollowStallReason.LeaderTooFar) == FollowRepathStrategy.Plain,
                "leader-too-far never spends a local side-step probe");
            Assert(FollowLocalObstaclePolicy.ChooseStrategy(FollowStallReason.NoRoute) == FollowRepathStrategy.Plain,
                "no-route never spends a local side-step probe");
        }

        private static void SidestepCandidatesAreLateralNotForwardOrBackward()
        {
            float leftX, leftZ, rightX, rightZ;
            FollowLocalObstaclePolicy.SidestepCandidates(0f, 0f, 0f, 1f, 2f, out leftX, out leftZ, out rightX, out rightZ);
            // Steering straight along +Z: lateral candidates must be offset along X, not further along Z.
            Assert(Math.Abs(leftZ) < 0.001f && Math.Abs(rightZ) < 0.001f,
                "side-step candidates for forward steering stay at the same Z, they do not push further forward or backward");
            Assert(Math.Abs(Math.Abs(leftX) - 2f) < 0.001f && Math.Abs(Math.Abs(rightX) - 2f) < 0.001f,
                "side-step candidates are offset by the full radius laterally");
            Assert(Math.Sign(leftX) != Math.Sign(rightX), "left and right side-step candidates are on opposite sides");
            Assert(leftX < 0f && rightX > 0f, "Unity +Z handedness labels -X as left and +X as right");
        }

        private static void SidestepSelectionUsesBothCandidates()
        {
            FollowSidestepCandidate left = new FollowSidestepCandidate
            {
                Sampled = true, PathValid = true, ContinuationValid = true,
                Progress = 1f, CombinedRouteLength = 12f, TieBreakX = -2f, TieBreakZ = 0f
            };
            FollowSidestepCandidate right = new FollowSidestepCandidate
            {
                Sampled = true, PathValid = true, ContinuationValid = true,
                Progress = 3f, CombinedRouteLength = 8f, TieBreakX = 2f, TieBreakZ = 0f
            };
            Assert(FollowLocalObstaclePolicy.ChooseSidestep(left, right) == FollowSidestepChoice.Right,
                "both usable sides select the objectively better continuation, not the first candidate");
            Assert(FollowLocalObstaclePolicy.ChooseSidestep(right, left) == FollowSidestepChoice.Left,
                "candidate enumeration order alone cannot force the chosen direction");

            left.PathValid = false;
            Assert(FollowLocalObstaclePolicy.ChooseSidestep(left, right) == FollowSidestepChoice.Right,
                "right-only usable chooses right");
            right.PathValid = false;
            left.PathValid = true;
            Assert(FollowLocalObstaclePolicy.ChooseSidestep(left, right) == FollowSidestepChoice.Left,
                "left-only usable chooses left");
            left.PathValid = false;
            Assert(FollowLocalObstaclePolicy.ChooseSidestep(left, right) == FollowSidestepChoice.None,
                "neither usable falls back to existing bounded ordinary recovery");
        }

        // ---------------------------------------------------------------------------------------------
        // 0.6.8 formation / catch-up distance bands. All measured against the leader's REAL position.
        // ---------------------------------------------------------------------------------------------

        // 1. close distance -> normal speed. Comfortably close never boosts speed - there is nothing to
        // catch up on, and boosting here would crowd the leader.
        private static void CloseDistanceUsesNormalSpeed()
        {
            FollowFormationBand band = FollowLocalObstaclePolicy.ClassifyFormation(1.0f, false);
            Assert(band == FollowFormationBand.Close, "1m behind the leader classifies as comfortably close");
            Assert(FollowLocalObstaclePolicy.FormationSpeedMultiplier(band) == 1.0f,
                "the close band never applies a speed multiplier");
            Assert(!FollowLocalObstaclePolicy.IsCatchUpActive(band), "the close band is never catch-up");
        }

        // 2. desired band -> stable trailing. The ordinary 2.5-5m trailing formation is just normal
        // native speed - no boost, no lag correction needed.
        private static void DesiredBandIsStableTrailingAtNormalSpeed()
        {
            FollowFormationBand band = FollowLocalObstaclePolicy.ClassifyFormation(
                FollowLocalObstaclePolicy.DesiredFollowDistance, false);
            Assert(band == FollowFormationBand.Normal, "the design-target desired follow distance classifies as normal trailing formation");
            Assert(FollowLocalObstaclePolicy.FormationSpeedMultiplier(band) == 1.0f,
                "normal trailing formation applies no speed multiplier");
            Assert(!FollowLocalObstaclePolicy.IsCatchUpActive(band), "normal trailing formation is never catch-up");
        }

        // 3. moderate separation -> catch-up engages, and stronger separation engages a stronger boost -
        // both bounded, modest multipliers on top of native speed, never a sprint cheat.
        private static void ModerateAndStrongSeparationEngageCatchUp()
        {
            FollowFormationBand moderate = FollowLocalObstaclePolicy.ClassifyFormation(
                FollowLocalObstaclePolicy.CatchUpEngageDistance + 0.5f, false);
            Assert(moderate == FollowFormationBand.CatchUp, "crossing the catch-up engage distance engages ordinary catch-up");
            float moderateMultiplier = FollowLocalObstaclePolicy.FormationSpeedMultiplier(moderate);
            Assert(moderateMultiplier > 1.0f && moderateMultiplier <= 1.25f,
                "ordinary catch-up is a modest, bounded boost over native speed, not a sprint cheat: " + moderateMultiplier);
            Assert(FollowLocalObstaclePolicy.IsCatchUpActive(moderate), "moderate separation is catch-up-active");

            FollowFormationBand strong = FollowLocalObstaclePolicy.ClassifyFormation(
                FollowLocalObstaclePolicy.StrongCatchUpDistance + 1f, false);
            Assert(strong == FollowFormationBand.StrongCatchUp, "crossing the strong catch-up distance engages the stronger tier");
            float strongMultiplier = FollowLocalObstaclePolicy.FormationSpeedMultiplier(strong);
            Assert(strongMultiplier > moderateMultiplier && strongMultiplier <= 1.5f,
                "strong catch-up is a larger but still bounded boost, well short of an unbounded sprint: " + strongMultiplier);
            Assert(FollowLocalObstaclePolicy.IsCatchUpActive(strong), "strong separation is catch-up-active");
        }

        // 4. formation recovered -> catch-up immediately disengages once solidly back in the normal band
        // (below the disengage distance), even though hysteresis keeps it active in the narrow gap
        // between the disengage and engage thresholds to avoid flicker right at one boundary.
        private static void CatchUpDisengagesOnceFormationRecovered()
        {
            FollowFormationBand recovered = FollowLocalObstaclePolicy.ClassifyFormation(
                FollowLocalObstaclePolicy.CatchUpDisengageDistance - 0.5f, true);
            Assert(recovered != FollowFormationBand.CatchUp && recovered != FollowFormationBand.StrongCatchUp,
                "once back under the disengage distance, catch-up turns off even though it was previously active");
            Assert(FollowLocalObstaclePolicy.FormationSpeedMultiplier(recovered) == 1.0f,
                "recovered formation returns to exactly native speed, not a lingering partial boost");

            // Hysteresis: the same distance range does NOT engage catch-up fresh if it was not already
            // active, and DOES stay engaged if it was - this is what prevents flicker at one boundary.
            float midBand = (FollowLocalObstaclePolicy.CatchUpDisengageDistance + FollowLocalObstaclePolicy.CatchUpEngageDistance) / 2f;
            Assert(FollowLocalObstaclePolicy.ClassifyFormation(midBand, false) == FollowFormationBand.Normal,
                "the hysteresis gap does not freshly engage catch-up if it was not already active");
            Assert(FollowLocalObstaclePolicy.ClassifyFormation(midBand, true) == FollowFormationBand.CatchUp,
                "the hysteresis gap stays in catch-up if it was already active, avoiding flicker");
        }

        // 5. temporary large gap (8-10m, terrain-caused) does not fail an otherwise healthy expedition -
        // it engages strong catch-up, and FollowStuckRecoveryPolicy (unchanged) still requires real
        // elapsed time and exhausted attempts before it would ever stop.
        private static void TemporaryLargeGapDoesNotFail()
        {
            FollowFormationBand band = FollowLocalObstaclePolicy.ClassifyFormation(9.5f, false);
            Assert(band == FollowFormationBand.StrongCatchUp, "an 8-10m terrain-caused gap engages strong catch-up, not a failure state");
            FollowStuckRecoveryDecision decision = FollowStuckRecoveryPolicy.Evaluate(
                noProgressSeconds: 0.5f, routeProblem: false, recoveryAttempts: 0, retryDue: true);
            Assert(decision == FollowStuckRecoveryDecision.None,
                "a large-but-temporary gap alone does not, by itself, fail an otherwise healthy expedition");
        }

        public static int Main()
        {
            BlockedByObstacleChoosesSidestepNotFailure();
            MovingLeaderRequestsBoundedPlainRepath();
            TrailingTargetIsBehindLeaderNotOnItsExactPosition();
            StuckDetectionTriggersRepath();
            TemporaryStallClassificationDoesNotFailImmediately();
            LeaderTooFarAndNoRouteAreDistinctFromBlocked();
            SidestepCandidatesAreLateralNotForwardOrBackward();
            SidestepSelectionUsesBothCandidates();
            CloseDistanceUsesNormalSpeed();
            DesiredBandIsStableTrailingAtNormalSpeed();
            ModerateAndStrongSeparationEngageCatchUp();
            CatchUpDisengagesOnceFormationRecovered();
            TemporaryLargeGapDoesNotFail();
            Console.WriteLine("All deterministic Follow local-obstacle policy tests passed (" + _passed + " assertions).");
            return 0;
        }
    }
}
