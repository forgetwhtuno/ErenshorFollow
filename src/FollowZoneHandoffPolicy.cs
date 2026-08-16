using System;

namespace ErenshorFollow
{
    internal enum FollowZoneHandoffDecision
    {
        Wait,
        EnterRebind,
        ResumeLocal,
        Stop
    }

    internal enum FollowZoneHandoffFailure
    {
        None,
        ContinuationDisabled,
        MissingIdentity,
        LeftParty,
        IdentityMismatch,
        TargetUnavailable,
        RemoteAuthority,
        Timeout
    }

    // Pure policy for the narrow ordering race where a party Sim's old scene avatar disappears just
    // before the player's own collider begins native zoning. Runtime code does not move toward a
    // guessed boundary during this grace period; it yields PlayerControl and waits for Erenshor either
    // to start GameData.Zoning or to restore the exact local avatar.
    internal static class FollowZoneHandoffPolicy
    {
        internal const float NativeZoneGraceSeconds = 2.5f;

        internal struct Inputs
        {
            internal bool ContinuationEnabled;
            internal bool HasPersistentIdentity;
            internal bool NativeZoning;
            internal bool TrackingInGroup;
            internal bool AvatarPresent;
            internal bool SameIdentity;
            internal bool AvatarUsable;
            internal bool LivePartyMember;
            internal bool RemoteAuthority;
            internal bool TimedOut;
        }

        internal static FollowZoneHandoffDecision Evaluate(Inputs input, out FollowZoneHandoffFailure failure)
        {
            failure = FollowZoneHandoffFailure.None;

            if (!input.ContinuationEnabled)
            {
                failure = FollowZoneHandoffFailure.ContinuationDisabled;
                return FollowZoneHandoffDecision.Stop;
            }
            if (!input.HasPersistentIdentity)
            {
                failure = FollowZoneHandoffFailure.MissingIdentity;
                return FollowZoneHandoffDecision.Stop;
            }
            if (input.NativeZoning) return FollowZoneHandoffDecision.EnterRebind;
            if (!input.TrackingInGroup)
            {
                failure = FollowZoneHandoffFailure.LeftParty;
                return FollowZoneHandoffDecision.Stop;
            }

            if (input.AvatarPresent)
            {
                if (!input.SameIdentity)
                {
                    failure = FollowZoneHandoffFailure.IdentityMismatch;
                    return FollowZoneHandoffDecision.Stop;
                }
                if (input.RemoteAuthority)
                {
                    failure = FollowZoneHandoffFailure.RemoteAuthority;
                    return FollowZoneHandoffDecision.Stop;
                }
                if (!input.AvatarUsable)
                {
                    failure = FollowZoneHandoffFailure.TargetUnavailable;
                    return FollowZoneHandoffDecision.Stop;
                }
                if (!input.LivePartyMember)
                {
                    failure = FollowZoneHandoffFailure.LeftParty;
                    return FollowZoneHandoffDecision.Stop;
                }
                return FollowZoneHandoffDecision.ResumeLocal;
            }

            if (input.TimedOut)
            {
                failure = FollowZoneHandoffFailure.Timeout;
                return FollowZoneHandoffDecision.Stop;
            }
            return FollowZoneHandoffDecision.Wait;
        }
    }
}
