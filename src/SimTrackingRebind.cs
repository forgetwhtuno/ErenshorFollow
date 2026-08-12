namespace ErenshorFollow
{
    // Narrow wrapper around the same game-owned identity primitives already proven by ExpeditionCoordinator.
    // SimPlayer is scene-bound; SimPlayerTracking is the persistent identity and MyAvatar is the current avatar.
    internal static class SimTrackingRebind
    {
        internal static SimPlayerTracking Capture(SimPlayer sim)
        {
            try { return sim == null ? null : sim.MySimTracking; }
            catch { return null; }
        }

        internal static SimPlayer CurrentAvatar(SimPlayerTracking tracking)
        {
            try { return tracking == null ? null : tracking.MyAvatar; }
            catch { return null; }
        }

        internal static bool AvatarMatchesTracking(SimPlayerTracking tracking, SimPlayer avatar)
        {
            if (tracking == null || avatar == null) return false;
            try { return object.ReferenceEquals(tracking, avatar.MySimTracking); }
            catch { return false; }
        }

        internal static bool TrackingIsInPlayerGroup(SimPlayerTracking tracking)
        {
            if (tracking == null) return false;
            try
            {
                SimPlayerTracking[] members = GameData.GroupMembers;
                if (members == null) return false;
                for (int i = 0; i < members.Length; i++)
                {
                    if (object.ReferenceEquals(tracking, members[i])) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
