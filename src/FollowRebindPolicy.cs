using System;

namespace ErenshorFollow
{
    // Pure lifecycle logic for direct Follow's zone suspension/rebind path. It deliberately knows
    // nothing about Unity or Erenshor objects so the transition contract can be tested deterministically.
    internal enum FollowIntentPhase
    {
        Idle,
        Following,
        Rebinding
    }

    internal enum FollowRebindDecision
    {
        Waiting,
        Resume,
        Stop
    }

    internal enum FollowRebindFailure
    {
        None,
        Timeout,
        LeftParty,
        IdentityMismatch,
        TargetUnavailable,
        RemoteAuthority
    }

    internal sealed class FollowIntentState<TIdentity> where TIdentity : class
    {
        internal FollowIntentPhase Phase { get; private set; }
        internal TIdentity Identity { get; private set; }

        internal bool Active { get { return Phase != FollowIntentPhase.Idle; } }

        internal void Begin(TIdentity identity)
        {
            Identity = identity;
            Phase = FollowIntentPhase.Following;
        }

        internal bool BeginRebinding()
        {
            if (Phase != FollowIntentPhase.Following || Identity == null) return false;
            Phase = FollowIntentPhase.Rebinding;
            return true;
        }

        internal bool ResumeAfterRebind()
        {
            if (Phase != FollowIntentPhase.Rebinding || Identity == null) return false;
            Phase = FollowIntentPhase.Following;
            return true;
        }

        internal void Cancel()
        {
            Identity = null;
            Phase = FollowIntentPhase.Idle;
        }
    }

    internal struct FollowRebindInputs
    {
        internal bool Zoning;
        internal bool SceneChanged;
        internal bool GameReady;
        internal bool Settled;
        internal bool TrackingInGroup;
        internal bool AvatarPresent;
        internal bool SameTracking;
        internal bool AvatarUsable;
        internal bool LivePartyMember;
        internal bool RemoteAuthority;
        internal bool TimedOut;
    }

    internal static class FollowRebindPolicy
    {
        internal static bool CanSuspendForZone(bool directFollow, bool verifiedZoning, bool hasPersistentIdentity)
        {
            return directFollow && verifiedZoning && hasPersistentIdentity;
        }

        internal static bool SameIdentity<TIdentity>(TIdentity expected, TIdentity actual) where TIdentity : class
        {
            return expected != null && object.ReferenceEquals(expected, actual);
        }

        internal static FollowRebindDecision Evaluate(FollowRebindInputs input, out FollowRebindFailure failure)
        {
            failure = FollowRebindFailure.None;

            if (input.Zoning || !input.SceneChanged || !input.GameReady || !input.Settled)
            {
                if (!input.TimedOut) return FollowRebindDecision.Waiting;
                failure = FollowRebindFailure.Timeout;
                return FollowRebindDecision.Stop;
            }

            if (!input.TrackingInGroup)
            {
                failure = FollowRebindFailure.LeftParty;
                return FollowRebindDecision.Stop;
            }

            // MyAvatar is rebound by Erenshor after the group is recreated. A temporarily null avatar is
            // expected and remains a bounded wait rather than being treated as ordinary target loss.
            if (!input.AvatarPresent)
            {
                if (!input.TimedOut) return FollowRebindDecision.Waiting;
                failure = FollowRebindFailure.Timeout;
                return FollowRebindDecision.Stop;
            }

            if (!input.SameTracking)
            {
                failure = FollowRebindFailure.IdentityMismatch;
                return FollowRebindDecision.Stop;
            }

            if (input.RemoteAuthority)
            {
                failure = FollowRebindFailure.RemoteAuthority;
                return FollowRebindDecision.Stop;
            }

            if (!input.AvatarUsable)
            {
                failure = FollowRebindFailure.TargetUnavailable;
                return FollowRebindDecision.Stop;
            }

            if (!input.LivePartyMember)
            {
                failure = FollowRebindFailure.LeftParty;
                return FollowRebindDecision.Stop;
            }

            return FollowRebindDecision.Resume;
        }
    }
}
