using System;

namespace ErenshorFollow
{
    internal static class FollowRebindPolicyTests
    {
        private static int _passed;

        private static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static FollowRebindInputs ReadyInputs()
        {
            FollowRebindInputs input = new FollowRebindInputs();
            input.SceneChanged = true;
            input.GameReady = true;
            input.Settled = true;
            input.TrackingInGroup = true;
            input.AvatarPresent = true;
            input.SameTracking = true;
            input.AvatarUsable = true;
            input.LivePartyMember = true;
            return input;
        }

        private static FollowRebindDecision Evaluate(FollowRebindInputs input, FollowRebindFailure expectedFailure, string name)
        {
            FollowRebindFailure actualFailure;
            FollowRebindDecision decision = FollowRebindPolicy.Evaluate(input, out actualFailure);
            Assert(actualFailure == expectedFailure, name + " failure reason");
            return decision;
        }

        private static void ActiveFollowZoningPreservesTrackingIntent()
        {
            object tracking = new object();
            FollowIntentState<object> intent = new FollowIntentState<object>();
            intent.Begin(tracking);
            Assert(FollowRebindPolicy.CanSuspendForZone(true, true, intent.Identity != null), "verified zoning permits direct Follow suspension");
            Assert(intent.BeginRebinding(), "active Follow enters rebind");
            Assert(intent.Phase == FollowIntentPhase.Rebinding, "rebind phase active");
            Assert(object.ReferenceEquals(tracking, intent.Identity), "persistent tracking identity preserved");
        }

        private static void OldAvatarDestroyedDuringZoningWaits()
        {
            FollowRebindInputs input = ReadyInputs();
            input.Zoning = true;
            input.SceneChanged = false;
            input.GameReady = false;
            input.Settled = false;
            input.TrackingInGroup = false;
            input.AvatarPresent = false;
            input.SameTracking = false;
            input.AvatarUsable = false;
            input.LivePartyMember = false;
            Assert(Evaluate(input, FollowRebindFailure.None, "old avatar loss during zoning") == FollowRebindDecision.Waiting,
                "old avatar destruction during zoning does not stop Follow");
        }

        private static void MatchingTrackingResumes()
        {
            FollowRebindInputs input = ReadyInputs();
            Assert(Evaluate(input, FollowRebindFailure.None, "valid rebound avatar") == FollowRebindDecision.Resume,
                "matching rebound avatar resumes Follow");
        }

        private static void NewAvatarNoLongerGroupedStops()
        {
            FollowRebindInputs input = ReadyInputs();
            input.TrackingInGroup = false;
            Assert(Evaluate(input, FollowRebindFailure.LeftParty, "tracking removed from group") == FollowRebindDecision.Stop,
                "target leaving party stops rebind");
        }

        private static void RemoteCoopAvatarStops()
        {
            FollowRebindInputs input = ReadyInputs();
            input.RemoteAuthority = true;
            Assert(Evaluate(input, FollowRebindFailure.RemoteAuthority, "remote COOP avatar") == FollowRebindDecision.Stop,
                "remote COOP avatar stops rebind");
        }

        private static void TimeoutStops()
        {
            FollowRebindInputs input = new FollowRebindInputs();
            input.Zoning = true;
            input.TimedOut = true;
            Assert(Evaluate(input, FollowRebindFailure.Timeout, "rebind timeout") == FollowRebindDecision.Stop,
                "rebind timeout stops Follow");
        }

        private static void ExplicitCancelDuringRebindStaysStopped()
        {
            FollowIntentState<object> intent = new FollowIntentState<object>();
            intent.Begin(new object());
            Assert(intent.BeginRebinding(), "cancel test enters rebind");
            intent.Cancel();
            Assert(intent.Phase == FollowIntentPhase.Idle, "explicit cancel clears rebind phase");
            Assert(intent.Identity == null, "explicit cancel clears persistent identity");
            Assert(!intent.ResumeAfterRebind(), "cancelled Follow cannot resume after zone load");
        }

        private static void SameNameDifferentSimNeverSubstitutes()
        {
            object capturedTracking = new object();
            object sameNameOtherTracking = new object();
            Assert(!FollowRebindPolicy.SameIdentity(capturedTracking, sameNameOtherTracking),
                "different tracking object is rejected regardless of display name");
            Assert(FollowRebindPolicy.SameIdentity(capturedTracking, capturedTracking),
                "captured tracking object matches itself");
        }

        private static void RepeatedZonesPreserveSameTracking()
        {
            object tracking = new object();
            FollowIntentState<object> intent = new FollowIntentState<object>();
            intent.Begin(tracking);
            Assert(intent.BeginRebinding(), "first zone begins rebind");
            Assert(intent.ResumeAfterRebind(), "first zone resumes");
            Assert(object.ReferenceEquals(tracking, intent.Identity), "identity preserved after first zone");
            Assert(intent.BeginRebinding(), "second zone begins rebind");
            Assert(object.ReferenceEquals(tracking, intent.Identity), "same identity preserved for second zone");
        }

        private static void OrdinaryTargetLossDoesNotBecomeResumable()
        {
            Assert(!FollowRebindPolicy.CanSuspendForZone(true, false, true),
                "ordinary target loss without verified zoning cannot suspend");
            Assert(!FollowRebindPolicy.CanSuspendForZone(false, true, true),
                "leader-owned Follow does not enter direct rebind state");
        }

        private static void AvatarMayAppearLateButIsBounded()
        {
            FollowRebindInputs input = ReadyInputs();
            input.AvatarPresent = false;
            input.SameTracking = false;
            input.AvatarUsable = false;
            input.LivePartyMember = false;
            Assert(Evaluate(input, FollowRebindFailure.None, "late MyAvatar before timeout") == FollowRebindDecision.Waiting,
                "temporarily null MyAvatar waits");
            input.TimedOut = true;
            Assert(Evaluate(input, FollowRebindFailure.Timeout, "late MyAvatar after timeout") == FollowRebindDecision.Stop,
                "temporarily null MyAvatar cannot wait forever");
        }

        private static void IdentityMismatchStops()
        {
            FollowRebindInputs input = ReadyInputs();
            input.SameTracking = false;
            Assert(Evaluate(input, FollowRebindFailure.IdentityMismatch, "avatar identity mismatch") == FollowRebindDecision.Stop,
                "mismatched avatar identity stops rebind");
        }

        public static int Main()
        {
            ActiveFollowZoningPreservesTrackingIntent();
            OldAvatarDestroyedDuringZoningWaits();
            MatchingTrackingResumes();
            NewAvatarNoLongerGroupedStops();
            RemoteCoopAvatarStops();
            TimeoutStops();
            ExplicitCancelDuringRebindStaysStopped();
            SameNameDifferentSimNeverSubstitutes();
            RepeatedZonesPreserveSameTracking();
            OrdinaryTargetLossDoesNotBecomeResumable();
            AvatarMayAppearLateButIsBounded();
            IdentityMismatchStops();
            Console.WriteLine("All deterministic Follow rebind tests passed (" + _passed + " assertions).");
            return 0;
        }
    }
}
