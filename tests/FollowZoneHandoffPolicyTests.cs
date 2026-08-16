using System;

namespace ErenshorFollow
{
    internal static class FollowZoneHandoffPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static FollowZoneHandoffPolicy.Inputs Base()
        {
            FollowZoneHandoffPolicy.Inputs input = new FollowZoneHandoffPolicy.Inputs();
            input.ContinuationEnabled = true;
            input.HasPersistentIdentity = true;
            input.TrackingInGroup = true;
            return input;
        }

        private static FollowZoneHandoffDecision Decide(FollowZoneHandoffPolicy.Inputs input,
            FollowZoneHandoffFailure expectedFailure, string name)
        {
            FollowZoneHandoffFailure failure;
            FollowZoneHandoffDecision decision = FollowZoneHandoffPolicy.Evaluate(input, out failure);
            Assert(failure == expectedFailure, name + " failure reason");
            return decision;
        }

        private static void MissingAvatarGetsOnlyBoundedGrace()
        {
            FollowZoneHandoffPolicy.Inputs input = Base();
            Assert(Decide(input, FollowZoneHandoffFailure.None, "missing avatar before timeout") == FollowZoneHandoffDecision.Wait,
                "missing scene avatar waits briefly for native player zoning");
            input.TimedOut = true;
            Assert(Decide(input, FollowZoneHandoffFailure.Timeout, "missing avatar after timeout") == FollowZoneHandoffDecision.Stop,
                "missing avatar cannot create permanent pending Follow");
        }

        private static void NativeZoningHandsOffToRebind()
        {
            FollowZoneHandoffPolicy.Inputs input = Base();
            input.NativeZoning = true;
            Assert(Decide(input, FollowZoneHandoffFailure.None, "native zoning begins") == FollowZoneHandoffDecision.EnterRebind,
                "only actual native zoning enters cross-zone rebind");
        }

        private static void ExactLocalAvatarCanRecoverInPlace()
        {
            FollowZoneHandoffPolicy.Inputs input = Base();
            input.AvatarPresent = true;
            input.SameIdentity = true;
            input.AvatarUsable = true;
            input.LivePartyMember = true;
            Assert(Decide(input, FollowZoneHandoffFailure.None, "exact avatar returns locally") == FollowZoneHandoffDecision.ResumeLocal,
                "same tracking avatar can recover without a scene transition");
        }

        private static void UnsafeIdentityStatesStop()
        {
            FollowZoneHandoffPolicy.Inputs input = Base();
            input.TrackingInGroup = false;
            Assert(Decide(input, FollowZoneHandoffFailure.LeftParty, "tracking left party") == FollowZoneHandoffDecision.Stop,
                "party loss cancels handoff");

            input = Base();
            input.AvatarPresent = true;
            input.SameIdentity = false;
            input.AvatarUsable = true;
            input.LivePartyMember = true;
            Assert(Decide(input, FollowZoneHandoffFailure.IdentityMismatch, "same-name replacement") == FollowZoneHandoffDecision.Stop,
                "different identity can never satisfy handoff");

            input = Base();
            input.AvatarPresent = true;
            input.SameIdentity = true;
            input.AvatarUsable = true;
            input.LivePartyMember = true;
            input.RemoteAuthority = true;
            Assert(Decide(input, FollowZoneHandoffFailure.RemoteAuthority, "remote authority") == FollowZoneHandoffDecision.Stop,
                "remote COOP avatar cannot resume handoff");

            input = Base();
            input.AvatarPresent = true;
            input.SameIdentity = true;
            input.AvatarUsable = false;
            input.LivePartyMember = true;
            Assert(Decide(input, FollowZoneHandoffFailure.TargetUnavailable, "dead or disabled avatar") == FollowZoneHandoffDecision.Stop,
                "dead or disabled avatar cannot resume handoff");
        }

        private static void FeatureGateFailsClosed()
        {
            FollowZoneHandoffPolicy.Inputs input = Base();
            input.ContinuationEnabled = false;
            Assert(Decide(input, FollowZoneHandoffFailure.ContinuationDisabled, "feature disabled") == FollowZoneHandoffDecision.Stop,
                "OFF-by-default flag prevents pending cross-zone behavior");
            input = Base();
            input.HasPersistentIdentity = false;
            Assert(Decide(input, FollowZoneHandoffFailure.MissingIdentity, "identity missing") == FollowZoneHandoffDecision.Stop,
                "no tracking identity means no handoff grace");
        }

        public static int Main()
        {
            MissingAvatarGetsOnlyBoundedGrace();
            NativeZoningHandsOffToRebind();
            ExactLocalAvatarCanRecoverInPlace();
            UnsafeIdentityStatesStop();
            FeatureGateFailsClosed();
            Console.WriteLine("All deterministic Follow pre-zone handoff tests passed (" + _passed + " assertions)." );
            return 0;
        }
    }
}
