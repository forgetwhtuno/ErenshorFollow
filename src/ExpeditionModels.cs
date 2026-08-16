using System;
using System.Collections.Generic;

namespace ErenshorFollow
{
    // Lifecycle of exactly one outing. ExpeditionCoordinator is the only writer.
    internal enum ExpeditionState
    {
        Idle,
        Forming,
        Traveling,
        CombatInterrupted,
        Regrouping,
        Paused,
        Transitioning,
        Arrived,
        Cancelled,
        Failed
    }

    // Outbound and Return run the same state machine; only the destination differs.
    internal enum ExpeditionObjective { Outbound, Return }
    internal enum ExpeditionPurpose { TravelToZone, ReturnToOrigin }

    internal enum ExpeditionInitiation { Command, ActionMenu, NaturalPartyCommand }

    internal enum ExpeditionPauseReason
    {
        None,
        PlayerRequest,
        PlayerManualMovement,
        PlayerGroupOrder,
        GroupCouldNotCatchUp
    }

    internal enum ExpeditionFailureReason
    {
        None,
        LeaderUnavailable,
        LeaderLeftParty,
        LeaderRemote,
        RouteFailed,
        DestinationLost,
        UnexpectedZone,
        LeaderNotReacquired,
        InternalError
    }

    // v1 recognizes exactly one destination kind. The enum exists so a later landmark/POI kind cannot
    // be introduced by accident through an untyped string.
    internal enum ExpeditionDestinationKind { AdjacentZone }

    // A canonical destination may have several real crossings in the loaded scene. The distinction is
    // intentional: duplicate Zonelines to the same zone are alternatives, not name ambiguity. Route
    // selection evaluates every retained crossing locally rather than trusting enumeration order.
    internal sealed class ExpeditionDestination
    {
        internal readonly ExpeditionDestinationKind Kind;
        internal readonly string CanonicalName;
        internal readonly List<Zoneline> Crossings;

        internal ExpeditionDestination(ExpeditionDestinationKind kind, string canonicalName, IList<Zoneline> crossings)
        {
            Kind = kind;
            CanonicalName = canonicalName;
            Crossings = new List<Zoneline>();
            if (crossings == null) return;
            for (int i = 0; i < crossings.Count; i++)
                if (crossings[i] != null) Crossings.Add(crossings[i]);
        }

        internal int CrossingCount { get { return Crossings.Count; } }
    }

    // Only facts the game or the mod can verify. No coordinates, no generated names, no copied NavMesh
    // route, no cached party roster: control decisions always re-query live game state.
    internal sealed class ExpeditionSession
    {
        internal readonly int SessionId;
        internal ExpeditionState State;
        internal ExpeditionObjective Objective;
        internal ExpeditionPurpose Purpose;

        // LeaderRuntime is scene-bound and is dropped at a zone transition. LeaderTracking is the stable
        // key: SimPlayerTracking is a plain object that survives scene loads and is re-pointed at the
        // respawned avatar by SimPlayerMngr.BringPlayerGroupToZone. See EXPEDITIONS_LOCAL_ASSEMBLY_FINDINGS.
        internal SimPlayer LeaderRuntime;
        internal SimPlayerTracking LeaderTracking;
        internal string LeaderName;

        internal string OriginZone;
        internal string CurrentZone;
        // Destination is the live, scene-bound next leg. FinalDestinationName and PlannedZones are
        // canonical game-authored names and are the only route facts retained across scene loads.
        internal ExpeditionDestination Destination;
        internal string FinalDestinationName;
        internal readonly List<string> PlannedZones = new List<string>();
        internal int CurrentRouteIndex;

        internal DateTime StartedUtc;
        internal ExpeditionPauseReason PauseReason;
        internal ExpeditionFailureReason FailureReason;
        internal string FailureDetail;
        internal int CombatInterruptions;
        internal readonly List<string> VerifiedZonesCrossed = new List<string>();
        internal ExpeditionInitiation InitiationSource;

        internal ExpeditionSession(int sessionId)
        {
            SessionId = sessionId;
            State = ExpeditionState.Forming;
        }

        internal string DestinationName
        {
            get { return string.IsNullOrWhiteSpace(FinalDestinationName)
                ? (Destination == null ? null : Destination.CanonicalName)
                : FinalDestinationName; }
        }

        internal string CurrentLegDestinationName
        {
            get { return Destination == null ? null : Destination.CanonicalName; }
        }
    }

    // Read-only view for UI. Nothing here may mutate gameplay state.
    internal struct ExpeditionStatusSnapshot
    {
        internal readonly int SessionId;
        internal readonly bool Active;
        internal readonly ExpeditionState State;
        internal readonly ExpeditionObjective Objective;
        internal readonly string LeaderName;
        internal readonly string DestinationName;
        internal readonly string CurrentZone;
        internal readonly string NextZone;
        internal readonly int RemainingTransitions;
        internal readonly ExpeditionPauseReason PauseReason;
        internal readonly int CombatInterruptions;

        internal ExpeditionStatusSnapshot(int sessionId, bool active, ExpeditionState state, ExpeditionObjective objective,
            string leaderName, string destinationName, string currentZone, string nextZone, int remainingTransitions,
            ExpeditionPauseReason pauseReason, int combatInterruptions)
        {
            SessionId = sessionId;
            Active = active;
            State = state;
            Objective = objective;
            LeaderName = leaderName;
            DestinationName = destinationName;
            CurrentZone = currentZone;
            NextZone = nextZone;
            RemainingTransitions = Math.Max(0, remainingTransitions);
            PauseReason = pauseReason;
            CombatInterruptions = combatInterruptions;
        }
    }
}
