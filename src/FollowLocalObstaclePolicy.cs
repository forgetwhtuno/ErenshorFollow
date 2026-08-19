using System;

namespace ErenshorFollow
{
    // Why the player currently is not making progress toward the leader. A pure classification used to
    // choose HOW the next bounded repath attempt behaves (a local lateral side-step vs an ordinary path
    // recompute) and for diagnostics. It does NOT replace FollowStuckRecoveryPolicy's strike-count/
    // timeout bound, which remains the single source of truth for when following genuinely gives up -
    // this only tries to make one of those bounded attempts actually succeed instead of repeating the
    // identical blocked path.
    internal enum FollowStallReason
    {
        None,
        MovingTargetRepathPending,
        BlockedByObstacle,
        LeaderTooFar,
        NoRoute
    }

    internal enum FollowRepathStrategy
    {
        Plain,
        Sidestep
    }

    // Ordinary formation vs. how urgently the player needs to close ground on the leader. Purely a
    // classification of measured leader distance; FormationSpeedMultiplier is the only thing this feeds
    // into runtime movement, and it is always applied on top of the player's own native run speed, never
    // in place of it and never written back to any persistent stat.
    internal enum FollowFormationBand
    {
        Close,
        Normal,
        CatchUp,
        StrongCatchUp
    }

    // Unity-free. Runtime code supplies measurements (movement delta, leader displacement, path
    // validity); this file only classifies why and computes where to look next. Every candidate point
    // this produces still has to pass the real NavMesh.SamplePosition/CalculatePath pipeline in
    // FollowController - it is never treated as an accepted route on its own.
    internal static class FollowLocalObstaclePolicy
    {
        // A player actively steered at full speed toward a waypoint but displacing less than this over
        // one repath cycle (~0.35s) is not "hasn't reached the corner yet" - it is physically blocked by
        // geometry the coarse NavMesh corner sequence walked straight at (e.g. a tree not carved from
        // the bake). Field case: Duskenlight -> ... -> Azure leg, PathComplete/13 corners, player visibly
        // running in place against a tree.
        internal const float StuckMovementThreshold = 0.15f;
        // The leader moving at least this far since the path was last computed means the cached corners
        // are simply stale for a moving target - a fresh repath, not evidence the player is stuck.
        internal const float LeaderMovedInvalidatesPathDistance = 2.5f;
        // Beyond this, the leader is outrunning the player rather than the player being blocked; treat
        // it with its own patience instead of the same bucket as a physical obstruction. Measured
        // against the leader's REAL position (not the trailing nav-target), so it stays meaningful
        // however the trailing offset or formation bands below are tuned.
        internal const float LeaderTooFarDistance = 25f;
        internal const float SidestepRadius = 2.5f;
        internal const float TrailDistance = 2.0f;

        // ------------------------------------------------------------------------------------------
        // Formation distance bands (0.6.8). All measured against the leader's REAL position, not the
        // trailing nav-target (which sits TrailDistance behind it) - "how far behind am I really" and
        // "where do I walk to" are deliberately separate questions. Design targets: 0-2m comfortably
        // close, ~2.5-5m normal trailing formation, ~5-8m catch-up, ~8m+ stronger catch-up. Values below
        // sit just inside those bands so the system starts closing the gap before it becomes visually
        // uncomfortable, and lets go of catch-up again once solidly back in the normal band (hysteresis:
        // CatchUpDisengageDistance < CatchUpEngageDistance so it cannot flicker at one boundary).
        internal const float CloseFormationDistance = 2.0f;
        internal const float DesiredFollowDistance = 3.5f;
        internal const float CatchUpEngageDistance = 6.0f;
        internal const float CatchUpDisengageDistance = 4.5f;
        internal const float StrongCatchUpDistance = 9.0f;

        // Bounded, modest speed multipliers on top of the player's own native run speed - never a
        // replacement for it, never written back to any persistent stat, and never applied outside the
        // CatchUp/StrongCatchUp bands. Erring toward the minimum necessary to actually regain formation
        // against a leader moving at the same native speed, not a sprint cheat.
        internal const float ModerateCatchUpMultiplier = 1.15f;
        internal const float StrongCatchUpMultiplier = 1.30f;

        // Hysteresis-aware: once catch-up is active it stays active until the player is back under
        // CatchUpDisengageDistance (solidly inside the normal band), not merely under the higher engage
        // threshold, so the multiplier cannot flicker on/off right at one boundary.
        internal static FollowFormationBand ClassifyFormation(float leaderDistance, bool catchupCurrentlyActive)
        {
            if (leaderDistance >= StrongCatchUpDistance) return FollowFormationBand.StrongCatchUp;
            if (leaderDistance >= CatchUpEngageDistance) return FollowFormationBand.CatchUp;
            if (catchupCurrentlyActive && leaderDistance >= CatchUpDisengageDistance) return FollowFormationBand.CatchUp;
            return leaderDistance <= CloseFormationDistance ? FollowFormationBand.Close : FollowFormationBand.Normal;
        }

        internal static float FormationSpeedMultiplier(FollowFormationBand band)
        {
            switch (band)
            {
                case FollowFormationBand.StrongCatchUp: return StrongCatchUpMultiplier;
                case FollowFormationBand.CatchUp: return ModerateCatchUpMultiplier;
                default: return 1.0f;
            }
        }

        internal static bool IsCatchUpActive(FollowFormationBand band)
        {
            return band == FollowFormationBand.CatchUp || band == FollowFormationBand.StrongCatchUp;
        }

        internal static FollowStallReason Classify(bool pathInvalidNoCorners, bool leaderMovedSincePath,
            float leaderDistance, bool steeringWasActive, float movementSinceLastAttempt)
        {
            if (pathInvalidNoCorners) return FollowStallReason.NoRoute;
            if (leaderMovedSincePath) return FollowStallReason.MovingTargetRepathPending;
            if (leaderDistance > LeaderTooFarDistance) return FollowStallReason.LeaderTooFar;
            if (steeringWasActive && movementSinceLastAttempt < StuckMovementThreshold) return FollowStallReason.BlockedByObstacle;
            return FollowStallReason.None;
        }

        // Only a genuine physical block is worth spending a lateral probe on; every other reason is
        // better served by an ordinary plain repath toward the (possibly just-updated) target.
        internal static FollowRepathStrategy ChooseStrategy(FollowStallReason reason)
        {
            return reason == FollowStallReason.BlockedByObstacle ? FollowRepathStrategy.Sidestep : FollowRepathStrategy.Plain;
        }

        // A point a short distance behind the leader's recent movement heading instead of its exact
        // transform. The player does not need to stand on the leader's exact spot, and this keeps both
        // actors from needing the identical tight gap at the same moment. Pure 2D (x,z) math so it is
        // testable without UnityEngine.Vector3. Falls back to the leader's own position when it has not
        // moved enough to have a meaningful heading (e.g. standing still) rather than inventing one.
        internal static void TrailingTarget(float leaderX, float leaderZ, float leaderDirX, float leaderDirZ,
            float trailDistance, out float targetX, out float targetZ)
        {
            float lenSq = leaderDirX * leaderDirX + leaderDirZ * leaderDirZ;
            if (lenSq < 0.0001f || trailDistance <= 0f)
            {
                targetX = leaderX;
                targetZ = leaderZ;
                return;
            }
            float len = (float)Math.Sqrt(lenSq);
            targetX = leaderX - (leaderDirX / len) * trailDistance;
            targetZ = leaderZ - (leaderDirZ / len) * trailDistance;
        }

        // The two lateral candidate points to probe when physically blocked: perpendicular to the
        // current steering direction, left and right, at a small bounded radius. This only computes
        // WHERE to look - the caller must still NavMesh-sample and CalculatePath each candidate and
        // accept it only if that succeeds, exactly like every other approach point in this mod.
        internal static void SidestepCandidates(float fromX, float fromZ, float dirX, float dirZ, float radius,
            out float leftX, out float leftZ, out float rightX, out float rightZ)
        {
            float lenSq = dirX * dirX + dirZ * dirZ;
            if (lenSq < 0.0001f)
            {
                leftX = fromX; leftZ = fromZ; rightX = fromX; rightZ = fromZ;
                return;
            }
            float len = (float)Math.Sqrt(lenSq);
            float nx = dirX / len;
            float nz = dirZ / len;
            // Perpendicular to (dirX, dirZ) in the XZ plane is (dirZ, -dirX) and its negation.
            leftX = fromX + nz * radius;
            leftZ = fromZ - nx * radius;
            rightX = fromX - nz * radius;
            rightZ = fromZ + nx * radius;
        }
    }
}
