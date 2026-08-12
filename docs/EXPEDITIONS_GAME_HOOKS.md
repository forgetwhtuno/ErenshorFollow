# Sim-Led Expeditions — Game Hooks & Local Verification Reference

Status: Research reference / implementation reconnaissance  
Research date: 2026-08-10

This document identifies the public code paths that appear most relevant to Sim-Led Expeditions. It is intentionally split into:

- **PUBLICLY VERIFIED**
- **INFERRED**
- **LOCAL VERIFICATION REQUIRED**

The local installed `Assembly-CSharp.dll` and the current working tree are authoritative.

---

## 1. Current public Erenshor Follow baseline

Repository: `forgetwhtuno/ErenshorFollow`  
Observed public version: `0.3.2`  
Observed public main commit: `091c5c20842f2383501856ab160d558f28cd4c7c`

Files of interest:

```text
src/ErenshorFollowPlugin.cs
src/FollowController.cs
src/LeaderController.cs
src/CoopCompatibility.cs
src/SimActionMenu.cs
src/TravelStatusOverlay.cs
```

### 1.1 FollowController

#### PUBLICLY VERIFIED

Current concepts:

```csharp
StopReason
{
    None,
    Explicit,
    ManualMovement,
    RouteUnavailable,
    TargetUnavailable
}

DriveState
{
    Idle,
    Waiting,
    Turning,
    Moving,
    PartialPathRetry,
    NoProgress
}
```

Important behavior:

- patched entry point: `PlayerControl.LandMovement`;
- movement owner: local `PlayerControl`;
- target: local `SimPlayer`;
- current movement uses:
  - `NavMesh.SamplePosition`
  - `NavMesh.CalculatePath`
  - `NavMeshPath`
  - `CharacterController.SimpleMove`
  - player rotation toward next corner;
- current stop/resume proximity:
  - stop ~3.0 m;
  - resume ~4.5 m;
- manual WASD/strafe/jump stops Follow;
- no-progress / partial route retries are bounded;
- target validity excludes remote COOP/networked Sims.

#### Expedition use

Do not replace this for v1.

Add an owner/context contract so Expedition can distinguish:

```text
plain Follow
vs
Follow being used as an expedition leg
```

This is needed primarily for manual-movement handling.

Potential design:

```csharp
enum FollowOwner
{
    None,
    DirectFollow,
    Expedition
}
```

or a callback/status arrangement that avoids public behavior changes.

Do not allow `FollowController` and `ExpeditionCoordinator` to recursively stop each other.

---

## 2. LeaderController

### 2.1 Current travel states

#### PUBLICLY VERIFIED

```csharp
TravelState
{
    Idle,
    Moving,
    PausedForCombat,
    ResumingAfterCombat,
    WaitingForPlayer,
    PartialRouteRetry,
    NoProgress
}
```

These are **leg execution states**, not a complete expedition lifecycle.

### 2.2 StartSmart

#### PUBLICLY VERIFIED

Current resolution order:

```text
requested string
 -> active adjacent Zoneline
 -> current-zone Sim
 -> nearby living hostile monster
 -> reject
```

### Expedition recommendation

Do not use the full `StartSmart` fallback chain for v1 expeditions.

A zone expedition should call a destination resolver that returns only:

```text
AdjacentZoneDestination
```

Ad-hoc Sim/monster lead behavior may remain as existing `/elead` functionality but should not silently become an expedition purpose.

---

## 3. Existing native-backed leader movement

### PUBLICLY VERIFIED in current Follow source

`LeaderController.ApplyTravelOrder()` currently performs the critical sequence:

```text
destination transform position
 -> NavMesh.SamplePosition
 -> route validation
 -> leader.AssignGuardSpot(hit.position)
 -> leader NPC.HighPriorityNavUpdate(hit.position)
 -> FollowController.Start(leader)
```

`RefreshNativeNavigation()` periodically invokes:

```text
leader.MyStats.Myself.MyNPC.HighPriorityNavUpdate(target)
```

### Architectural consequence

The Sim leader already moves through Erenshor's NPC/Sim navigation path.

Expeditions should coordinate this behavior, not implement another Sim movement solver.

---

## 4. Current combat hooks/signals

### PUBLICLY VERIFIED in current Follow source

`LeaderController.InCombat(SimPlayer sim)` currently checks:

```text
GameData.InCombat
OR
sim.IsSimGroupInCombat()
OR
sim.MyStats.Myself.MyNPC.CurrentAggroTarget != null
```

On verified combat:

```text
leader.FreeFollow()
FollowController.Stop()
```

After combat remains clear for approximately five seconds:

```text
ApplyTravelOrder()
```

### Expedition recommendation

Move the **lifecycle meaning** of these transitions into `ExpeditionCoordinator`, but reuse the same detector and leg mechanics unless a concrete local test proves a gap.

### LOCAL VERIFICATION REQUIRED

Test whether these signals cover:

- player attacked first;
- leader attacked first;
- healer/support party member acquires combat;
- another grouped Sim pulls;
- pet/summon combat;
- post-combat lingering aggro;
- death;
- native Run Away;
- encounter reset.

Do not add another detector preemptively.

---

## 5. Current regroup behavior

### PUBLICLY VERIFIED

`LeaderController.PlayerGap()` uses horizontal-ish world distance between leader and local player.

Current hysteresis:

```text
gap > 8 m       -> leader waits
gap <= 4.5 m    -> travel resumes
wait >= 12 s    -> current Lead stops
```

Wait order:

```text
leader.AssignGuardSpot(leader.transform.position)
```

### Expedition recommendation

Preserve these thresholds initially.

Add an expedition-level `Regrouping` state and require a stable safe condition before reissuing movement after combat/zoning.

Potential later change:

```text
12-second catch-up exhaustion
 -> Paused
```

rather than immediate `Failed`, but only after live usability testing.

---

## 6. Current zone destination hooks

### PUBLICLY VERIFIED in Follow

Destination discovery scans active:

```csharp
Zoneline
```

and reads:

```csharp
Zoneline.DestinationZone
```

Resolution:

- exact case-insensitive match wins;
- one unique substring match is accepted;
- ambiguous substring is rejected.

Arrival check:

```text
SceneManager.GetActiveScene().name == DestinationZone
```

### Expedition v1

Use exactly this runtime destination source.

Do not use:

- LLM-generated coordinates;
- wiki route coordinates;
- fictional names;
- a hidden hard-coded world graph unless deliberately added and runtime-validated later.

---

## 7. Potential zone lifecycle hooks from public mod source

### PUBLICLY VERIFIED IN THIRD-PARTY MOD SOURCE, NOT OFFICIAL API

Current Erenshor COOP public/decompiled source references hooks for:

```text
Zoneline.OnTriggerEnter
SceneChange.ChangeScene
SimPlayerMngr.BringPlayerGroupToZone
SimPlayerMngr.SpawnSimsInZone
SimPlayer.FollowPlayer
NPC.UpdateNav
SimPlayerGrouping.InviteToGroup
SimPlayerGrouping.DismissMember1
SimPlayerGrouping.DismissMember2
SimPlayerGrouping.DismissMember3
SimPlayerGrouping.DismissMember4
Character.GroupMemberAlive
```

It also references `GameData.GroupMembers` / Sim tracking state in its group-zone handling.

### Why these matter

They point the local implementation AI toward the likely places where Erenshor:

- starts a zone transition;
- recreates/rebinds grouped Sims;
- restores party members after zoning;
- runs native Sim follow/nav;
- changes group membership.

### LOCAL VERIFICATION REQUIRED

Inspect the installed DLL and record exact:

- signatures;
- access modifiers;
- call order;
- side effects;
- whether hooks are still present in the installed build.

Do **not** copy COOP's replacement behavior into Expeditions.

The COOP source is reconnaissance, not an implementation template for local movement authority.

---

## 8. Likely Assembly-CSharp symbols to inspect first

The following are the high-value local targets.

### 8.1 Player movement / input

#### PUBLICLY VERIFIED through current Follow build/source

```text
PlayerControl
  LandMovement
  CanMove
  LeftClick
  CurrentTarget
  Myself
```

#### LOCAL VERIFICATION REQUIRED

Inspect:

- overloads/signatures;
- what else calls `LandMovement`;
- whether movement suppression is safe during scene load/death;
- exact private `moving` field behavior;
- any native auto-follow/autowalk state that would be safer than `SimpleMove`.

---

### 8.2 SimPlayer

#### PUBLICLY VERIFIED through current Follow/current public mods

```text
SimPlayer
  InGroup
  GuardSpot
  GetGuardPos()
  AssignGuardSpot(Vector3)
  FreeFollow()
  IsSimGroupInCombat()
```

#### PUBLIC THIRD-PARTY SOURCE / LOCAL VERIFY

```text
SimPlayer.FollowPlayer()
```

Public COOP source reflects it as non-public in at least one build.

#### Inspect locally for

- stable identity fields;
- current scene / destination / activity;
- task/activity state;
- group-owner/controller state;
- fields that indicate whether native combat has control;
- fields that should be restored after temporary Guard orders;
- whether a Sim can have an existing independent activity that Expedition must preserve.

---

### 8.3 NPC navigation

#### PUBLICLY VERIFIED through current Follow

```text
NPC
  CurrentAggroTarget
  HighPriorityNavUpdate(Vector3)
```

#### PUBLIC THIRD-PARTY SOURCE / LOCAL VERIFY

```text
NPC.UpdateNav
NPC.NeedsNavUpdate(...)
```

#### Inspect locally for

- `NavMeshAgent` ownership;
- destination fields;
- path completion semantics;
- stop/reset methods;
- whether `AssignGuardSpot + HighPriorityNavUpdate` is the intended combination;
- whether another native method represents "go to this Sim activity destination" more cleanly.

---

### 8.4 Grouping

#### PUBLICLY VERIFIED through current Follow

```text
GameData.SimPlayerGrouping
SimPlayerGrouping.IsSimInPlayerGroup(SimPlayer)
SimPlayer.InGroup
```

#### PUBLIC THIRD-PARTY SOURCE / LOCAL VERIFY

```text
SimPlayerGrouping.InviteToGroup
SimPlayerGrouping.DismissMember*
GameData.GroupMembers
SimPlayerTracking
  MyAvatar
  CurScene
  simIndex
```

#### Inspect locally for

- stable group identity;
- authoritative current party roster;
- leader reacquisition after zoning;
- whether `simIndex` is stable enough to identify the same Sim across scenes;
- whether renamed Sims keep stable backing identity;
- when `MyAvatar` becomes valid after zone load.

Do not assume `simIndex` is safe until verified.

---

### 8.5 Zone transitions

#### PUBLICLY VERIFIED through current Follow

```text
Zoneline
  DestinationZone

SceneManager.GetActiveScene()
```

#### PUBLIC THIRD-PARTY SOURCE / LOCAL VERIFY

```text
Zoneline.OnTriggerEnter
SceneChange.ChangeScene(...)
SimPlayerMngr.BringPlayerGroupToZone
SimPlayerMngr.SpawnSimsInZone
```

#### Inspect locally for

- exact event order:
  - Zoneline trigger
  - scene change
  - old scene unload
  - new scene load
  - player placement
  - party Sim spawn/rebind;
- special portals/dungeons;
- whether destination zone name and scene name are always identical;
- whether there is a global zone-connection data structure.

---

### 8.6 Combat/death/retreat

#### PUBLICLY VERIFIED

```text
GameData.InCombat
SimPlayer.IsSimGroupInCombat()
NPC.CurrentAggroTarget
```

#### PUBLIC THIRD-PARTY SOURCE / LOCAL VERIFY

```text
Character.GroupMemberAlive
Respawn.RespawnPlayer
```

#### Inspect locally for

- player death flag/source;
- Sim death and revival flow;
- native Run Away command implementation;
- how running to a zoneline revives group members;
- whether combat remains true during loading/respawn;
- whether party Sims temporarily leave/rejoin the group.

---

## 9. Native group command surface

### PUBLICLY VERIFIED — official wiki

Native party commands include:

```text
Follow
Guard / Wait / Stay
Run Away / Run / Flee / Escape
Attack
Pull
Stop pulling
Cautious
Aggressive
```

### Expedition interaction policy

Explicit native group commands should beat Expedition automation.

Recommended semantics:

```text
native Guard/Wait/Stay
 -> expedition Paused(PlayerGroupOrder)

native Follow/Come while paused
 -> optional Resume only if destination/session still valid

native Run/Flee/Escape
 -> external emergency override
 -> expedition Cancelled or Failed after resulting transition

native Attack/Pull
 -> do not reinterpret as expedition command
 -> combat detector handles resulting combat
```

### LOCAL VERIFICATION REQUIRED

Identify the deterministic method/command-handler locations behind these native orders.

Do not infer them from displayed chat strings if a direct method can be observed.

---

## 10. Deep Sims integration hooks

Repository: `forgetwhtuno/DeepSim-erenshor`  
Observed public version: `0.7.0`  
Observed public main latest commit during research: `5449401570e923017f820d1585305247d97cde28`

### PUBLICLY VERIFIED

`DeepSimsPlugin.NotifyObservedGameEvent(...)` currently takes:

```text
type
description
importance
importantMemory
baseChance
```

The Social Director then decides whether the type is promoted to an event conversation.

`EventConversationDirector` already holds richer structured candidate information:

```text
Type
ObservedUtc
InvolvedNames
EligibleSpeakerNames
VerifiedEntities
Trust
Importance
Novelty
CooldownCategory
VerifiedContext
BaseChance
```

### Current limitation

At public 0.7.0, expedition event types are not yet in the Social Director's supported observed-event allowlist.

`SocialPolicy.PriorityOf` already mentions `travel_arrival`, but generic arrival handling is not yet a complete expedition bridge.

### Recommended implementation sequence

#### Phase 4A — Follow side

Create one integration sink:

```text
ExpeditionIntegrationBridge.Emit(verifiedEvent)
```

This bridge:

- has no gameplay side effects;
- may reflect into Deep Sims if present;
- silently does nothing if absent.

#### Phase 4B — Deep Sims side

Prefer a structured primitive-only method such as:

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

Then Deep Sims can construct a `SocialEventCandidate` without parsing leader role from prose.

#### Compatibility fallback

The current Practice Duels pattern reflects:

```text
ErenshorDeepSims.DeepSimsPlugin
 -> static Instance
 -> NotifyObservedGameEvent(...)
```

That is a valid temporary compatibility pattern.

Do not treat generic prose as proof of `leaderName` for permanent first-person memories.

---

## 11. Practice Duels as a companion-mod pattern

### PUBLICLY VERIFIED

`forgetwhtuno/Erenshor-Duel` is a useful example because it:

- has no hard Deep Sims dependency;
- checks optional companion behavior at runtime;
- emits lifecycle events through reflection;
- uses `SceneManager.sceneLoaded` / `sceneUnloaded` to cancel/clean state across transitions;
- preserves gameplay ownership in Erenshor.

### Reuse conceptually

Reuse:

- optional runtime bridge;
- one owner for lifecycle;
- exact-once terminal event;
- scene cleanup discipline.

Do not copy duel-specific combat patches into Expeditions.

---

## 12. COOP safety hooks

### PUBLICLY VERIFIED in current Follow

`CoopCompatibility.IsRemoteHuman(SimPlayer)` detects reflected types:

```text
ErenshorCoop.NetworkedPlayer
ErenshorCoop.Client.NetworkedPlayer
ErenshorCoop.NetworkedSim
```

It retries type resolution if assemblies load later.

### PUBLICLY VERIFIED in current COOP public source

COOP contains special navigation handling/prevention for networked Sims/players.

### Expedition rule

Every leader acquisition/reacquisition must re-run local-ownership safety checks.

Especially after zoning:

```text
reacquired SimPlayer
 -> usable?
 -> living?
 -> current party?
 -> not remote/networked?
 -> only then issue a nav order
```

---

## 13. Recommended Expedition internal hooks

Avoid broad Harmony patches where existing update polling is enough.

### Coordinator entry points

```text
Start(leader, destination, source)
Pause(reason)
Resume()
TryReturn()
Cancel(reason)
Tick()
HandleSceneLoaded(scene)
HandleSceneUnloaded(scene)
GetStatusSnapshot()
```

### LeaderController reporting contract

Rather than `LeaderController` directly emitting final Expedition chat/state, expose leg outcomes:

```text
LegStarted
CombatDetected
CombatCleared
WaitingForPlayer
PlayerRegrouped
FollowManualOverride
RouteFailed
LeaderInvalid
SceneChanged
LegArrived
```

This can be callbacks, a polled status snapshot, or a small result enum.

Use the least invasive option that fits the current local code.

### Avoid

- multiple controllers independently calling `Stop()` on each other;
- property getters with side effects;
- UI reads that mutate gameplay state;
- scene-transition work from `OnGUI`;
- background-thread Unity access.

The current Follow source has already moved side-effecting validity work out of hot getters; preserve that discipline.

---

## 14. Potential command hook

### PUBLICLY VERIFIED current Follow

Follow patches:

```text
TypeText.CheckCommands
```

and deterministically consumes `/elead`, `/efollow`, and supported natural `/p|/party|/group` lead phrases.

### Recommendation

Extend the same command owner rather than adding a second Harmony command parser in another DLL.

Ordering goal:

```text
Expedition deterministic command
 -> consumed by Follow
 -> optional verified social acknowledgement later

ordinary social party chat
 -> available to Deep Sims/vanilla as appropriate
```

This prevents an LLM from racing the gameplay command parser.

---

## 15. UI hooks

### PUBLICLY VERIFIED current Follow

Action menu uses:

```text
PlayerControl.LeftClick
Character.TargetMe
```

to identify the actually clicked Sim, with F8/middle-click fallbacks.

Travel overlay is immediate-mode UI and queries read-only status snapshots.

### Recommendation

Keep Expedition UI in these existing surfaces.

Do not add a new permanent window unless live usability testing shows the current menu/overlay cannot scale.

---

## 16. Questions the local Assembly investigation must answer

Record the answers in implementation notes before major refactors.

### Identity

1. What stable Sim identity survives zone transitions?
2. Does a renamed Sim retain an internal key/index?
3. Can two active Sims share the same display name?
4. Is `SimPlayerTracking.simIndex` stable and unique across scenes/saves?

### Zone lifecycle

5. Is a SimPlayer destroyed and recreated during zoning?
6. When is `SimPlayerTracking.MyAvatar` valid?
7. When does `BringPlayerGroupToZone` run relative to `sceneLoaded`?
8. Does `SpawnSimsInZone` recreate all party Sims?
9. Is `DestinationZone` always the actual scene name?
10. Are special portals represented by ordinary `Zoneline` instances?
11. Is there a game-owned global zone graph?

### Navigation

12. What is the native meaning of `AssignGuardSpot`?
13. Does `HighPriorityNavUpdate` override or complement normal Sim task state?
14. Is there a cleaner native "go to location/task" API?
15. Is `NPC.UpdateNav` safe to observe without replacing?
16. How does native `FollowPlayer` choose targets/offsets?

### Combat

17. Does `IsSimGroupInCombat()` include combat involving every local group Sim?
18. What conditions keep `GameData.InCombat` true after an encounter?
19. What does native Run Away do to Guard/Follow/task fields?
20. How are dead Sims revived after zone escape?
21. What is the exact player death/respawn lifecycle?

### Group orders

22. Which methods implement `/group wait/guard/stay`?
23. Which methods implement `/group follow/come`?
24. Which methods implement `/group run/flee/escape`?
25. Can those be observed so player orders cleanly pause/cancel Expeditions?

### Existing Sim activities

26. Which field/method expresses a Sim's current independent activity/destination?
27. What must be restored when an expedition ends?
28. Does temporary grouping already override that activity in a safe reversible way?

---

## 17. Local reconnaissance checklist

Before changing source:

```text
git status
git branch --show-current
git log -n 10 --oneline
git diff
```

Then inspect:

```text
current ErenshorFollow source
current local docs
installed Assembly-CSharp.dll
installed Erenshor COOP assembly if present
installed Deep Sims assembly/source if local tree differs from GitHub
```

Create a short findings note containing:

```text
Symbol
Installed signature
Where called from
Observed side effects
Confidence
Public assumption matched? yes/no
```

Only after that should implementation begin.

---

## 18. Source references

### User projects

- https://github.com/forgetwhtuno/ErenshorFollow
- https://github.com/forgetwhtuno/DeepSim-erenshor
- https://github.com/forgetwhtuno/Erenshor-Duel

### Official Erenshor wiki

- https://erenshor.wiki.gg/wiki/Simulated_Players
- https://erenshor.wiki.gg/wiki/Player_Guide
- https://erenshor.wiki.gg/wiki/Zones
- https://erenshor.wiki.gg/wiki/Port_Azure

### Public mod source used for reconnaissance

- https://thunderstore.io/c/erenshor/p/mizuki/Erenshor_COOP/source/
- https://thunderstore.io/c/erenshor/p/mizuki/Erenshor_COOP/changelog/

---

## 19. Bottom line

The installed Assembly investigation should focus on **lifecycle correctness**, not on discovering a replacement pathfinder.

Public Follow already demonstrates that the key movement chain is viable:

```text
verified Zoneline
 -> Sim Guard destination
 -> native NPC nav update
 -> local player follows Sim
 -> combat suspends movement
 -> regroup
 -> scene transition
```

The open engineering problem is making that chain reliable as a named, resumable, observable **Expedition**.
