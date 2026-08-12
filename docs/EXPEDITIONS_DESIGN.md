# Sim-Led Expeditions — Design Specification

Status: Draft architecture / research handoff  
Target project: Erenshor Follow, with optional Deep Sims and future Camp/Relax integrations  
Research date: 2026-08-10  
Implementation status: **Not implemented by this document**

## 1. Design intent

Sim-Led Expeditions turns the existing one-leg **Lead** behavior in Erenshor Follow into an explicit, deterministic outing lifecycle.

The intended player experience is:

> I joined another MMO player's outing and they are leading the party somewhere.

It should **not** feel like:

> I clicked an NPC autopath button.

The system should preserve the fiction that a SimPlayer is the current expedition leader while Erenshor continues to own ordinary group behavior, combat, zoning, and Sim AI.

### Core rules

1. **Orchestrate; do not replace.**
   - Reuse the current Follow/Lead controllers.
   - Reuse Erenshor Sim navigation.
   - Reuse Erenshor combat.
   - Reuse native grouping and zone transitions.
2. **The deterministic layer owns gameplay actions.**
   - Commands, menus, and natural-language command parsing resolve to deterministic actions.
   - An LLM may phrase acknowledgements or invitations, but it never chooses arbitrary coordinates, invents a destination, or controls movement/combat.
3. **Fail closed.**
   - If leader identity, party membership, destination, route, ownership, or zone state is ambiguous, pause/cancel/fail rather than guessing.
4. **Keep optional projects loosely coupled.**
   - Follow/Expeditions should work without Deep Sims, Ollama, Practice Duels, Camp/Relax, or COOP.
   - Social memory belongs to Deep Sims, not Follow.
5. **Local installed game wins.**
   - Public GitHub/source research is an architecture aid.
   - The implementation AI must re-check the current working tree and installed `Assembly-CSharp.dll` before relying on any class/member described here.

---

## 2. Evidence labels

This document uses three evidence labels.

### PUBLICLY VERIFIED

Observed in the current public repositories, official Erenshor wiki, or current public mod source as of 2026-08-10.

### INFERRED

A design conclusion supported by public behavior/source, but not itself an official Erenshor contract.

### LOCAL VERIFICATION REQUIRED

A member, lifecycle detail, identity rule, or behavior that must be re-checked against the user's installed game and current local working tree before implementation.

---

## 3. What current Erenshor Follow already solves

### PUBLICLY VERIFIED — current public Erenshor Follow 0.3.2

Repository: `forgetwhtuno/ErenshorFollow`  
Public main observed at commit: `091c5c20842f2383501856ab160d558f28cd4c7c`

The current public code already provides most of the movement substrate needed for Expeditions:

- `FollowController`
  - drives the local player toward a SimPlayer;
  - uses local `NavMesh` path calculation;
  - drives `CharacterController.SimpleMove`;
  - stops plain Follow on manual movement/jump;
  - retries partial paths;
  - fails after sustained no-progress;
  - excludes remote COOP humans/networked Sims.
- `LeaderController`
  - requires a living Sim in the player's current party;
  - resolves current-zone `Zoneline` objects by `DestinationZone`;
  - assigns the leader a guard destination with `SimPlayer.AssignGuardSpot(...)`;
  - refreshes native Sim navigation through the leader NPC's `HighPriorityNavUpdate(...)`;
  - starts `FollowController` so the player follows the Sim;
  - pauses movement for real combat;
  - clears leader travel orders while combat owns the Sim;
  - waits five seconds after combat clears;
  - waits when the player is too far behind;
  - resumes after the player catches up;
  - validates leader progress/path status;
  - recognizes successful one-zone arrival by an actual active-scene change to the requested destination;
  - restores the leader's prior Guard/Follow state on ordinary stop.
- deterministic command support:
  - `/elead zones`
  - `/elead <SimName> <adjacent zone>`
  - `/elead status`
  - `/elead resume`
  - `/elead off`
- deterministic natural party-chat parsing already recognizes forms equivalent to:
  - `Phanty, lead us to Azure`
  - `Phanty, lead me to Azure`
  - `Phanty, take us to Azure`
  - `Phanty, take me to Azure`
- `SimActionMenu`
  - opens only for living local party Sims;
  - offers Follow, optional Practice Duel, verified Lead destinations, and Stop;
  - displays up to three runtime-discovered adjacent zone destinations.
- `TravelStatusOverlay`
  - shows Follow/Lead target and movement state;
  - includes Stop.
- `CoopCompatibility`
  - rejects `ErenshorCoop.NetworkedPlayer`;
  - rejects legacy `ErenshorCoop.Client.NetworkedPlayer`;
  - rejects `ErenshorCoop.NetworkedSim`;
  - invalidates cached reflected types when assemblies load later.

### PUBLICLY VERIFIED — current Lead thresholds

Current public `LeaderController` behavior includes:

- wait for player when gap exceeds approximately **8 m**;
- resume when gap is approximately **4.5 m or less**;
- stop if the player fails to catch up for approximately **12 s**;
- post-combat safety delay approximately **5 s**;
- partial-route retry budget approximately **3 s**;
- no-progress failure approximately **5 s**.

These should be treated as current implementation defaults, not universal game truths.

### Conclusion

**Expeditions should not start by rewriting locomotion.**

The missing layer is a durable **outing/session coordinator** around the current Lead behavior: explicit state, pause reasons, return semantics, zone-transition handling, arrival semantics, optional integration events, and future multi-leg routing.

---

## 4. Native Erenshor systems to preserve

### PUBLICLY VERIFIED — official wiki

Erenshor already exposes a party-control vocabulary that strongly supports an orchestration design:

- Follow
- Guard / Wait / Stay
- Run Away / Flee / Escape
- Attack / Pull / stop pulling
- cautious/aggressive group behavior

The official Simulated Players wiki describes:

- **Follow** as the native party command to resume following the player;
- **Guard** as the native party command to remain in place;
- **Run Away** as a command that makes the group attempt a zone-boundary transition to exit combat and revive the party.

The wiki also documents Sim responses to location/activity questions, indicating that current zone/activity state is part of normal SimPlayer behavior.

### PUBLICLY VERIFIED — current Follow implementation

Current Follow uses the following Erenshor-facing operations:

- `SimPlayer.AssignGuardSpot(Vector3)`
- `SimPlayer.FreeFollow()`
- `SimPlayer.GetGuardPos()`
- `SimPlayer.GuardSpot`
- `NPC.HighPriorityNavUpdate(Vector3)`
- `GameData.InCombat`
- `SimPlayer.IsSimGroupInCombat()`
- `NPC.CurrentAggroTarget`
- `GameData.SimPlayerGrouping.IsSimInPlayerGroup(sim)`
- `Zoneline.DestinationZone`

### Design rule

Expeditions should express higher-level intent such as:

- travel to a verified destination;
- wait for the party;
- pause because combat owns movement;
- resume travel;
- end at destination;

and delegate the actual low-level behavior to current/native systems.

---

## 5. Recommended module architecture

### Recommendation: implement inside Erenshor Follow first

Do **not** create a second movement-owning DLL for v1.

The lowest-risk arrangement is a new set of files/classes inside the existing Erenshor Follow project:

```text
ErenshorFollowPlugin
    |
    +-- ExpeditionCoordinator
    |      owns exactly one active ExpeditionSession
    |      owns lifecycle transitions
    |      owns pause/cancel/fail/arrival semantics
    |
    +-- ExpeditionDestinationResolver
    |      returns only game-verified destination candidates
    |
    +-- LeaderController
    |      existing per-leg executor
    |      native Sim navigation + combat/regroup mechanics
    |
    +-- FollowController
    |      existing local-player follower
    |
    +-- ExpeditionIntegrationBridge
    |      optional runtime-reflection integrations
    |
    +-- SimActionMenu
    |      existing UI, extended
    |
    +-- TravelStatusOverlay
           existing UI, extended
```

### INFERRED

A separate Expeditions plugin would either:

- duplicate movement authority;
- depend directly on Follow internals; or
- require an external Follow API before it can be useful.

Keeping v1 in Follow makes one component the sole owner of player travel and minimizes Harmony/controller conflicts.

If Expeditions later becomes a separate DLL, first expose a stable movement/lifecycle API from Follow.

---

## 6. Expedition state model

### Recommended active state enum

```text
Idle
Forming
Traveling
CombatInterrupted
Regrouping
Paused
Transitioning
Arrived
Cancelled
Failed
```

Terminal states (`Arrived`, `Cancelled`, `Failed`) should be observable long enough to emit one lifecycle result, then clear back to `Idle`.

### Why several proposed states are not top-level v1 states

- **Proposed**
  - keep outside the active expedition lifecycle;
  - future Sim invitations have their own proposal object/state;
  - accepting a proposal creates a normal expedition in `Forming`.
- **Departing**
  - model as a lifecycle event on `Forming -> Traveling`, not a persistent state.
- **Returning**
  - model as an objective/mode (`Outbound` vs `Return`) while using the same travel states.
- **None**
  - use `Idle`.

This reduces state explosion while preserving all meaningful behavior.

### Objective mode

```text
Outbound
Return
```

The same state machine is used for both.

### State transitions

```text
Idle
  -> Forming
  -> Traveling

Traveling
  -> CombatInterrupted
  -> Regrouping
  -> Paused
  -> Transitioning
  -> Cancelled
  -> Failed

CombatInterrupted
  -> Regrouping
  -> Cancelled
  -> Failed

Regrouping
  -> Traveling
  -> Paused
  -> Cancelled
  -> Failed

Paused
  -> Traveling      (only after full revalidation)
  -> Cancelled
  -> Failed

Transitioning
  -> Arrived        (final destination verified)
  -> Forming        (future verified next leg)
  -> Failed         (unexpected/invalid transition)

Arrived / Cancelled / Failed
  -> Idle
```

### Transition ownership

`ExpeditionCoordinator` should be the only class that changes expedition state.

`LeaderController` should report leg-level outcomes/conditions; it should not invent expedition lifecycle transitions independently once the coordinator exists.

---

## 7. Expedition session record

Store only facts the game/mod can actually verify.

### Recommended v1 session fields

```text
SessionId                    // local monotonic or GUID; diagnostic only
State
ObjectiveMode                // Outbound | Return
LeaderName
LeaderRuntimeRef             // ephemeral; never persisted
LeaderStableKey              // optional, only if locally verified
OriginZone
CurrentZone
Destination                  // structured DestinationRef
StartedUtc
PauseReason
FailureReason
CombatInterruptions
VerifiedZonesCrossed         // observed scene names only
InitiationSource             // Command | ActionMenu | NaturalPartyCommand
```

### DestinationRef

```text
Kind                         // AdjacentZone in v1
CanonicalName
RuntimeKey/identifier        // only if game exposes a stable one
```

### Do not store as authoritative state

- arbitrary coordinates supplied by an LLM;
- generated place names;
- a copied NavMesh route/corner list;
- speculative world-path state;
- stale party-member object references;
- arbitrary natural-language "purpose";
- wiki-derived coordinates/topology;
- a party roster that overrides live game grouping.

### Purpose

For v1 use a deterministic enum:

```text
TravelToZone
ReturnToOrigin
```

Do not persist free-form LLM intent as gameplay truth.

### Party members

Query the current native group when making control decisions.

If an integration event needs a participant snapshot, capture names only at the time the verified event is emitted.

---

## 8. Destination model

### Version 1 recommendation: verified adjacent zones only

Use the live `Zoneline` objects already discovered by `LeaderController`.

A valid v1 expedition destination is:

> a currently loaded, active, unambiguous `Zoneline.DestinationZone` reachable through the existing local route validation.

### Why this is safest

It is:

- already implemented;
- deterministic;
- grounded in the loaded game;
- naturally compatible with actual zone transitions;
- independent of the LLM;
- robust against wiki staleness;
- easy to test.

### Natural aliases

The current implementation already permits a unique substring match.

Therefore a phrase like:

> `Phanty, lead us to Azure.`

may resolve deterministically to `Port Azure` **only when the current runtime destination candidates make that match unique**.

If curated aliases are added later:

```text
azure -> Port Azure
```

the alias must still resolve to a canonical destination that exists in the current runtime candidate set.

An alias table is a text normalization aid, **not** a route source.

### Defer from v1

#### Named landmarks

Add only after local `Assembly-CSharp.dll` inspection finds a stable game-defined point-of-interest/travel-node identity and a safe navigation anchor.

#### Custom user markers

Defer. They are coordinate-centric and conflict with the initial goal of using game-defined navigation.

#### Global world routing

Defer until one of these is locally verified:

1. Erenshor exposes a stable global zone-connection graph; or
2. a deterministic route table can be built and validated against live adjacent exits on every leg.

The official wiki is useful as a test/reference corpus, but should **not** be the runtime routing authority.

### Important separation from current `StartSmart`

Current `StartSmart` can also lead to:

- a current-zone Sim;
- a nearby monster.

Do not silently promote those fallbacks into v1 **Expedition** destination types.

Keep ad-hoc Lead/Hunt behavior separate until explicit expedition purposes are designed.

---

## 9. Commands and UI

### Preserve compatibility

Keep existing:

```text
/elead zones
/elead <SimName> <adjacent zone>
/elead status
/elead resume
/elead off
```

A zone-based `/elead <Sim> <zone>` can become a compatibility route into `ExpeditionCoordinator.Start(...)`.

### Minimal explicit expedition commands

Recommended:

```text
/expedition
/expedition status
/expedition pause
/expedition resume
/expedition cancel
/expedition return
```

Rules:

- `/expedition` alone shows current status/help.
- `return` is available only when a deterministic return route can be verified.
- Do not add `/expedition with` if the existing action menu and `/elead` already choose the leader cleanly.

### Action-menu integration

Extend the existing Sim action menu rather than creating a second UI.

When no expedition is active:

```text
Follow Phanty

START EXPEDITION
Port Azure
Hidden Hills
...

Challenge to friendly duel
```

When an expedition is active:

```text
EXPEDITION
Phanty -> Port Azure

Pause expedition
Resume expedition    // only if paused
Return               // only if currently resolvable
Cancel expedition
```

### Travel overlay

Extend the current status overlay to display expedition state:

```text
Phanty leading to Port Azure
Traveling
```

or:

```text
Phanty leading to Port Azure
Combat interrupted
```

or:

```text
Phanty leading to Port Azure
Waiting for group
```

Do not add a second travel HUD.

---

## 10. Natural party-chat controls

### PUBLICLY VERIFIED

Current Follow already performs deterministic parsing of party-chat phrases such as:

```text
Phanty, lead us to Azure.
Phanty, take me to Azure.
```

This happens in Follow's command layer, not in the LLM.

### Recommendation

Keep this deterministic-first architecture.

Possible future phrases:

```text
Hold here.
Keep going.
Let's head back.
Cancel the expedition.
```

Only implement them if their interpretation is unambiguous in the current expedition context.

Suggested routing:

```text
"hold here"        -> ExpeditionCoordinator.Pause(PlayerRequest)
"keep going"       -> ExpeditionCoordinator.Resume()
"let's head back"  -> TryBeginReturn()
```

Deep Sims may produce an acknowledgement after the action is already validated/executed.

It must not be the action parser.

---

## 11. Combat interruption

### PUBLICLY VERIFIED

Current Follow detects real combat using a combined signal:

```text
GameData.InCombat
OR leader.IsSimGroupInCombat()
OR leader NPC.CurrentAggroTarget != null
```

On combat entry current Lead:

- clears the leader's travel order through `FreeFollow()`;
- stops automated local-player follow;
- lets native combat own behavior.

After combat is clear for approximately five seconds it reapplies the travel order.

### Recommendation

Reuse that exact detector initially.

Do not create an independent Expedition combat detector unless local testing proves the current one misses a concrete case.

### Expedition transition

```text
Traveling
 -> CombatInterrupted
```

Increment `CombatInterruptions` once per distinct interruption.

While interrupted:

- no travel NavMesh commands;
- no Follow player-driving;
- no automatic target manipulation;
- no replacement combat AI;
- no expedition "helpful" Run Away;
- Erenshor owns combat.

After combat becomes clear:

```text
CombatInterrupted
 -> Regrouping
```

Only after the safety delay and a safe regroup check:

```text
Regrouping
 -> Traveling
```

### Death / wipe policy

#### Leader death

Fail the expedition. Do not substitute a new leader automatically in v1.

#### Player death

Cancel/fail the active expedition. Do not auto-resurrect or auto-run.

#### Other party-member death

Do not invent behavior. Remain interrupted/regrouping while native combat/group state says the party is unsafe.

**LOCAL VERIFICATION REQUIRED:** determine how death/revival affects `IsSimGroupInCombat`, group membership, `GroupMemberAlive`, and post-zone revival.

#### Wipe / Run Away

If the player explicitly uses native Run Away and the group transitions through an unexpected border, treat that as an external override.

Do not force the old route after the escape.

---

## 12. Regrouping

### PUBLICLY VERIFIED current behavior

Current Lead already uses hysteresis:

- leader waits when player gap > ~8 m;
- leader resumes when gap <= ~4.5 m;
- current wait timeout ~12 s.

The leader is held by assigning a guard spot at the leader's current position.

### Recommended expedition behavior

Reuse the existing thresholds for the first implementation so behavior changes are isolated.

Add a small stability/settle requirement before reissuing travel after:

- combat;
- zoning;
- a wait condition.

This prevents rapid wait/resume oscillation.

### Important player-manual-input issue

Current plain Follow stops on WASD/jump.

Current Lead then sees Follow inactive and stops the entire leader trip.

That behavior is acceptable for plain `/efollow`, but is fragile for an Expedition.

### Recommended expedition-specific behavior

When `FollowController` stops because of **manual player movement** during an active expedition:

1. do not destroy the expedition;
2. hold the leader;
3. transition to `Paused` with `PauseReason=PlayerManualMovement`;
4. allow explicit Resume/Keep Going to revalidate and continue.

Plain Follow should retain its current cancellation behavior.

This is a high-value lifecycle change because it prevents a single movement key from silently destroying an outing.

### Timeout reconsideration

The current 12-second catch-up failure may feel too punitive for a longer outing.

For the first build, preserve it unless live testing proves it frustrating.

A later expedition-specific policy could turn a long regroup failure into `Paused` rather than `Failed`.

---

## 13. Zone transitions

### PUBLICLY VERIFIED current behavior

Current Lead stores the starting scene name.

When the active scene changes:

- if it matches the requested `DestinationZone`, current Lead reports arrival;
- if it is an unexpected scene, current Lead stops;
- monster hunt Lead stops on any zone change.

### v1 policy

For adjacent-zone expeditions:

1. leader walks toward the loaded `Zoneline`;
2. player follows through normal local movement;
3. the actual Erenshor transition occurs;
4. observe the new active scene;
5. verify it matches the canonical destination;
6. reacquire/settle party state if needed;
7. report `Arrived`.

Do not directly call scene-change functions just to make Expeditions work.

### LOCAL VERIFICATION REQUIRED

Live-test the exact transition mechanism:

- Does the leader cross the Zoneline first?
- Does only the player's crossing trigger scene load?
- When are grouped Sims recreated/rebound?
- Is the old `SimPlayer` destroyed during transition?
- How soon is `GameData.SimMngr`/group data valid in the new scene?
- Does `Zoneline.DestinationZone` always equal `SceneManager.GetActiveScene().name`?
- Are there portal/special-case zones whose display name and scene name differ?

### Future multi-leg routing

Do not retain a destroyed old leader reference across zoning.

After `sceneLoaded`:

1. wait for game/group settlement;
2. reacquire the leader by a **locally verified stable identity**;
3. revalidate local ownership, party membership, and alive state;
4. resolve the next adjacent `Zoneline`;
5. start the next leg.

A `SceneManager.sceneLoaded/sceneUnloaded` listener is already a proven pattern in Practice Duels, but exact Expedition timing still requires local testing.

---

## 14. Arrival

### v1 deterministic arrival condition

For an adjacent-zone destination:

> Arrival occurs only after the game actually transitions and the active scene matches the canonical verified destination.

A leader merely reaching the border is not arrival.

### On arrival

1. stop expedition-owned movement;
2. stop player auto-follow;
3. settle/reacquire party state if needed;
4. decide leader Guard/Follow restoration policy;
5. emit exactly one `expedition_arrived` lifecycle event;
6. expose post-arrival actions;
7. clear the active session after the terminal event has been observed.

### Default outcome

**End the expedition.**

Do not force Camp/Relax.

### Optional post-arrival actions

```text
Guard Here
Camp Here      // if supported
Relax Here     // if supported
Continue       // future multi-leg / new destination flow
```

---

## 15. Return behavior

### Do not return to a stored coordinate

The origin coordinate is not a safe world-route abstraction.

### Safe v1 Return

Return is available only if, from the current zone:

- the origin zone is a live verified adjacent destination; and
- the existing route checks accept it.

Then:

```text
ObjectiveMode = Return
Destination = verified origin-zone Zoneline
```

and the same expedition state machine runs in reverse intent.

### Future multi-zone Return

If multi-zone travel is later implemented:

- record only zones that were **actually observed** during the outbound trip;
- attempt to reverse that verified zone sequence;
- revalidate every next adjacent leg in the live scene;
- stop if the expected leg is unavailable.

Never replay coordinates blindly.

---

## 16. Failure and cancellation policy

### Cancelled

Use when the player or an external game action intentionally supersedes the trip:

- `/expedition cancel`;
- Stop button;
- native command that clearly changes travel intent;
- player logs out / plugin unload;
- possibly native Run Away causing a different zone.

### Failed

Use when the expedition can no longer safely satisfy its contract:

- leader unavailable/dead;
- leader no longer in current party;
- leader became remote/network-owned;
- route made no progress;
- destination vanished;
- unexpected zone transition;
- leader cannot be reacquired after zoning;
- required local game state is ambiguous.

### Never silently recover by

- teleporting;
- inventing a route;
- swapping leaders;
- moving remote COOP entities;
- issuing combat actions;
- guessing which same-named Sim is the original leader.

---

## 17. Co-op policy

### PUBLICLY VERIFIED

Current Follow already excludes remote/network-owned COOP entities, including networked Sims.

Current Erenshor COOP source also contains explicit navigation interception for networked players/Sims, reinforcing that a second mod should not issue local nav orders to them.

### v1 policy

- Expedition leader must be a locally controlled SimPlayer.
- Remote human players are never valid leaders for this feature.
- Remote/network-owned SimPlayers are never valid movement targets.
- Expeditions do not attempt to synchronize their state to peers in v1.
- Deep Sims social output retains its existing host-authority policy.
- If ownership is ambiguous, reject/cancel.

---

## 18. Deep Sims integration

### PUBLICLY VERIFIED current architecture

Deep Sims 0.7.0 separates:

```text
verified event
 -> social admission
 -> expression
```

Its `EventConversationDirector` can:

- suppress duplicates;
- prioritize events;
- enforce participant/speaker eligibility;
- apply cooldowns and probability;
- decide that no one should speak.

Practice Duels already demonstrates a loose runtime-reflection bridge into Deep Sims.

### Design rule

**Expeditions emits verified lifecycle facts. Deep Sims decides whether those facts matter socially.**

Follow/Expeditions never calls Ollama.

### Recommended event set

```text
expedition_started
expedition_departed
expedition_combat_interrupted
expedition_resumed
expedition_zone_entered
expedition_paused
expedition_arrived
expedition_returning
expedition_cancelled
expedition_failed
```

`expedition_proposed` is reserved for the future proposal layer.

### Not every event should create chat

Recommended social treatment:

| Event | Social use | Persistent memory |
|---|---|---|
| started | context, usually silent | no |
| departed | low-value context | no |
| combat_interrupted | usually silent during combat | no |
| resumed | usually silent | no |
| zone_entered | context for multi-leg travel | no |
| paused | usually silent | no |
| arrived | medium-value candidate | yes, if completed and useful |
| returning | low-value context | no |
| cancelled | usually silent | generally no |
| failed | optional medium candidate if meaningful | generally no |

### Structured event bridge recommended

Do not rely on parsing prose to determine who led whom.

A compatible primitive-only bridge could look conceptually like:

```text
NotifyExpeditionEvent(
    eventType,
    leaderName,
    originZone,
    destinationName,
    currentZone,
    objectiveMode,
    combatInterruptions,
    reasonCode)
```

The precise API should be chosen after inspecting the latest local Deep Sims tree.

Why structured roles matter:

A future line such as:

> "Last time I led you through Azure..."

is only grounded if memory proves:

- that specific Sim was the expedition leader; and
- a verified expedition arrival/completion occurred.

A generic text event does not provide a strong enough role contract by itself.

### Backward-compatible first bridge

If the structured Deep Sims method does not yet exist, Follow may temporarily use the same runtime-reflection pattern Practice Duels uses for `NotifyObservedGameEvent`.

However, leader-specific long-term memories should wait for the structured bridge.

---

## 19. Camp / Relax integration

Current Deep Sims already has a social Camp mode based around sitting/meditation, but Expeditions should not depend on it.

### Interface/event model

On verified arrival, Expeditions emits:

```text
expedition_arrived
leader=Phanty
destination=Port Azure
current_zone=Port Azure
```

An optional Camp/Relax module may:

- offer `Camp Here`;
- enter a social relax mode;
- react to arrival.

Expeditions should not call private Camp internals or require Camp to be installed.

---

## 20. Future Sim-initiated expeditions

Do not let the LLM choose a raw destination.

Future architecture:

```text
live game state
 -> deterministic valid destination candidates
 -> deterministic/policy eligibility
 -> policy chooses candidate
 -> LLM/templates phrase invitation
 -> player accepts or declines
 -> candidate is revalidated
 -> normal ExpeditionCoordinator.Start(...)
```

### Proposal object

Keep proposal state separate from an active session:

```text
ProposalId
ProposerSim
CandidateDestination
CreatedUtc
ExpiresUtc
Status = Proposed | Accepted | Declined | Expired
```

The candidate must already be deterministic and valid before the LLM sees it.

### Example

Deterministic layer:

```text
candidate = Port Azure
leader = Phanty
```

Expression layer:

> "Want to head to Azure?"

Acceptance invokes the same validated start path as a menu or command.

---

## 21. Persistence policy

### v1

Do **not** persist an active Expedition across save/load/game restart.

Reasons:

- runtime Sim references are scene-bound;
- group state can change;
- current destination availability can change;
- automatic movement on load is surprising and unsafe.

On plugin unload or game exit:

- stop owned movement;
- clear active session.

### Social persistence

Deep Sims may persist a compact verified completed-expedition memory after a confirmed arrival.

Follow/Expeditions does not own that memory.

Do not persist every pause/combat interruption as long-term social history.

---

## 22. Recommended phased roadmap

### Phase 1 — Formalize current Lead

- add `ExpeditionSession`;
- add `ExpeditionCoordinator`;
- wrap zone-based `LeaderController` Lead in explicit lifecycle;
- expose read-only status;
- emit internal lifecycle events;
- preserve existing Lead movement behavior;
- keep `/elead` compatibility.

### Phase 2 — Destination, controls, arrival, return

- formal `DestinationRef`;
- adjacent-zone resolver;
- action-menu labels/actions;
- `/expedition status|pause|resume|cancel`;
- deterministic arrival;
- safe one-hop Return only when origin is currently resolvable;
- manual movement becomes expedition pause rather than total destruction.

### Phase 3 — Combat/regroup/zone hardening

- preserve existing combat detector;
- explicit `CombatInterrupted -> Regrouping -> Traveling`;
- verify all death/wipe/retreat cases;
- scene-loaded settlement/rebind tests;
- resolve leader identity across zone transitions;
- only add multi-leg routing if the local DLL provides a safe identity/topology basis.

### Phase 4 — Deep Sims integration

- structured expedition event bridge;
- add expedition event types to Deep Sims' supported verified-event pipeline;
- arrival as the primary social candidate;
- grounded leader-specific completed-expedition memory;
- deterministic/template responses remain available when LLM is unavailable.

### Phase 5 — Camp / Relax

- optional arrival subscriber;
- no hard dependency;
- offer Camp/Relax post-arrival only when capability exists.

### Phase 6 — Sim-proposed expeditions

- deterministic candidate generation;
- bounded willingness/policy;
- LLM/templates phrase invitation;
- accept/decline lifecycle;
- revalidate candidate on acceptance.

---

## 23. MVP definition

A successful MVP is deliberately narrow:

1. Player chooses a **living local party Sim**.
2. Player chooses a **live verified adjacent zone**.
3. Expedition session starts.
4. Sim travels using the existing native-backed LeaderController.
5. Player follows using the existing FollowController.
6. Real combat pauses travel.
7. Combat resolves entirely in Erenshor.
8. Expedition waits for safety/group catch-up.
9. Travel resumes.
10. A real zone transition occurs.
11. New scene is verified as the destination.
12. Expedition emits one arrival and ends.
13. Manual pause/resume/cancel works.
14. Remote COOP humans/networked Sims are never driven.
15. No LLM is required.

That is already enough to create the intended "another player is leading the outing" feeling.

---

## 24. Acceptance test plan

### A. Destination grounding

- `/elead zones` and expedition UI list only live `Zoneline` destinations.
- Exact destination resolves.
- Unique abbreviation such as `Azure` resolves only when unambiguous.
- Ambiguous substring is rejected.
- Fictional place name is rejected.
- LLM-off/Templates-off configuration does not affect travel.

### B. Leader eligibility

- living local party Sim: accepted;
- local non-party Sim: rejected for v1;
- dead Sim: rejected;
- remote COOP human: rejected;
- networked Sim: rejected;
- leader leaves party mid-trip: fail cleanly.

### C. Movement

- leader starts walking through native navigation;
- player follows leader;
- ordinary corners work;
- partial route retries;
- sustained no progress fails without teleporting;
- Stop/cancel restores control.

### D. Manual input

- plain `/efollow` retains existing cancel behavior;
- active Expedition + WASD/jump transitions to manual pause rather than destroying the outing;
- Resume revalidates state then continues;
- resume is refused in combat.

### E. Combat interruption

Test separately:

- player enters real combat;
- leader enters real combat;
- another grouped Sim pulls aggro;
- leader acquires aggro before player;
- combat ends normally;
- combat clears then reappears during safety delay.

Expected:

- no expedition movement fights combat;
- one interruption count per incident;
- five-second existing safety delay is preserved initially;
- group regroups;
- travel resumes once safe.

### F. Regroup

- player deliberately lags >8 m;
- leader waits;
- player catches up <=4.5 m;
- leader resumes;
- player never catches up;
- confirm timeout behavior and whether v1 should fail or pause.

### G. Zone transition

- start adjacent-zone expedition;
- approach boundary;
- observe actual Erenshor scene transition;
- verify leader/group lifecycle;
- destination scene exact match -> one arrival;
- wrong/unexpected scene -> fail/cancel;
- special portal/dungeon transitions -> document differences.

### H. Death / escape

- player death;
- leader death;
- party member death;
- wipe;
- native Run Away;
- respawn after cancellation.

No automatic expedition combat/respawn logic should appear.

### I. Optional integrations

With Deep Sims absent:
- no errors;
- expedition fully works.

With Deep Sims present:
- arrival event is delivered once;
- social layer may remain silent;
- no combat-interruption chatter spam;
- no fabricated leader memory.

With COOP present:
- remote/networked targets never become expedition leaders.

### J. Cleanup

- plugin unload;
- return to menu;
- zone load failure;
- internal exception;
- cancel while waiting;
- cancel during post-combat delay.

No stuck movement, guard spot, or Follow state.

---

## 25. Open questions for local implementation

1. What stable identity survives a SimPlayer zone respawn?
2. Are `SimPlayer` objects destroyed/recreated during every zone transition?
3. Is `Zoneline.DestinationZone` always the exact Unity scene name?
4. Are there multiple active Zonelines with the same destination/display name?
5. What exactly causes the player's scene transition while following a leader?
6. When is the group manager valid after `sceneLoaded`?
7. Does `IsSimGroupInCombat()` include combat involving every grouped local Sim?
8. What is the exact player/sim death-revival lifecycle after native Run Away?
9. Is there a game-owned global zone graph or route/travel-node system?
10. Does a stable POI/landmark class exist that could support safe named-landmark destinations?
11. Does `SimPlayer.FollowPlayer()` provide a useful native operation for restoring post-expedition state?
12. Which method is the safest observer for native group Guard/Follow/Run commands so explicit user orders can pause/cancel an expedition instead of being fought?
13. Are there existing Sim activity/destination fields that should be restored after an expedition ends?
14. Can leader Guard state be safely restored across a zone transition, or must arrival use current-zone defaults?

---

## 26. Research sources

### Current public project repositories

- https://github.com/forgetwhtuno/ErenshorFollow
- https://github.com/forgetwhtuno/DeepSim-erenshor
- https://github.com/forgetwhtuno/Erenshor-Duel

### Official Erenshor wiki

- https://erenshor.wiki.gg/wiki/Simulated_Players
- https://erenshor.wiki.gg/wiki/Player_Guide
- https://erenshor.wiki.gg/wiki/Zones
- https://erenshor.wiki.gg/wiki/Port_Azure

### Relevant public mod source

- Erenshor COOP public/decompiled source:
  https://thunderstore.io/c/erenshor/p/mizuki/Erenshor_COOP/source/

### Research caution

The wiki and third-party mod source are not substitutes for the installed `Assembly-CSharp.dll`.

Every runtime member/signature relied on by implementation must be validated locally before coding against it.
