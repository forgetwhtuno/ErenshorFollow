# Sim-Led Expeditions — Local Coding AI Handoff

You are implementing the first working version of **Sim-Led Expeditions** for Erenshor.

This handoff is intentionally implementation-oriented.

Read these first:

```text
docs/EXPEDITIONS_DESIGN.md
docs/EXPEDITIONS_GAME_HOOKS.md
```

The public research in those documents is not authoritative over the user's local working tree or installed game.

---

## 1. Mission

Turn the existing Erenshor Follow **Lead** behavior into an explicit Expedition lifecycle while preserving the current working combat/navigation boundary.

The desired experience:

```text
player asks a grouped local Sim to lead
-> expedition session starts
-> Sim leads using current/native navigation
-> local player follows
-> native party/combat behavior remains in control
-> combat interrupts movement
-> combat resolves normally
-> group regroups
-> Sim resumes
-> real zone transition occurs
-> destination is verified
-> expedition arrives and ends
```

Do not build a replacement combat AI.

Do not build a second general navigation engine.

Do not let an LLM choose gameplay coordinates or unverified destinations.

---

## 2. Hard safety requirements

1. **Inspect the current local working tree first.**
   - It may be newer than GitHub.
   - Do not reset it to public main.
2. **Inspect the installed `Assembly-CSharp.dll` before depending on public assumptions.**
3. **Do not overwrite or revert unrelated local changes.**
4. **Do not push, publish, tag, or create a PR unless explicitly asked.**
5. **Build against the user's installed Erenshor assemblies.**
6. **Keep Erenshor Follow the sole owner of local-player travel.**
7. **Never issue local movement commands to remote COOP humans or network-owned Sims.**
8. **Erenshor owns real combat.**
9. **No teleport fallback.**
10. **No LLM dependency.**
11. **Inspect the final diff before declaring completion.**

---

## 3. First step: establish local truth

Before editing anything, run/read the equivalent of:

```text
git status
git branch --show-current
git log -n 15 --oneline
git diff
```

Then inspect the current local versions of at least:

```text
src/ErenshorFollowPlugin.cs
src/FollowController.cs
src/LeaderController.cs
src/CoopCompatibility.cs
src/SimActionMenu.cs
src/TravelStatusOverlay.cs
README.md
CHANGELOG.md
```

If the tree differs from the public 0.3.2 architecture, adapt this brief to the local code.

Do not rewrite newer local design back to the public shape.

---

## 4. Inspect the installed game

Decompile/reflect the installed `Assembly-CSharp.dll`.

Verify exact signatures and behavior for at least:

```text
PlayerControl.LandMovement
PlayerControl.CanMove
PlayerControl.LeftClick

SimPlayer.AssignGuardSpot
SimPlayer.FreeFollow
SimPlayer.GetGuardPos
SimPlayer.GuardSpot
SimPlayer.InGroup
SimPlayer.IsSimGroupInCombat

NPC.HighPriorityNavUpdate
NPC.CurrentAggroTarget
NPC.UpdateNav              // if present
NPC.NeedsNavUpdate         // if present

Zoneline.DestinationZone
Zoneline.OnTriggerEnter

SimPlayerGrouping.IsSimInPlayerGroup
SimPlayerGrouping.InviteToGroup
SimPlayerGrouping.DismissMember*

SimPlayerMngr.BringPlayerGroupToZone
SimPlayerMngr.SpawnSimsInZone

SceneChange.ChangeScene
Character.GroupMemberAlive
Respawn.RespawnPlayer
```

Also investigate:

```text
stable Sim identity across zoning
SimPlayerTracking / simIndex / MyAvatar / CurScene
native Sim FollowPlayer
current Sim activity/destination fields
global zone graph/travel-node data, if any
```

Write down what was verified and what contradicted the public assumptions.

Do not code multi-zone routing until identity/topology is understood.

---

## 5. Target implementation scope

Implement **adjacent-zone Expeditions first**.

A valid destination must come from a live, active, unambiguous game destination already exposed by current Follow, preferably the existing `Zoneline.DestinationZone` path.

Do not implement:

- arbitrary coordinates;
- wiki-driven runtime routes;
- LLM-generated places;
- landmark routing without verified game POIs;
- custom markers;
- automatic leader replacement;
- new combat AI;
- automatic Run Away;
- multi-zone route planning unless the installed game exposes a trustworthy deterministic graph and leader rebind path.

---

## 6. Preferred code shape

Unless the local tree already has a better abstraction, introduce approximately:

```text
ExpeditionCoordinator.cs
ExpeditionModels.cs
ExpeditionDestinationResolver.cs
ExpeditionIntegrationBridge.cs
```

Do not create a second BepInEx movement plugin.

### ExpeditionCoordinator

Own exactly one active session.

Recommended states:

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

Recommended objective mode:

```text
Outbound
Return
```

The coordinator is the only owner of expedition lifecycle transitions.

### ExpeditionSession

Keep only verified facts:

```text
state
objective mode
leader runtime ref
leader display name
stable leader key only if locally verified
origin zone
current zone
structured destination
start time
pause/failure reason
combat interruption count
observed zones crossed
initiation source
```

Do not duplicate NavMesh path state inside the session.

---

## 7. Preserve the existing leg executor

The current public Lead implementation already uses the intended movement chain:

```text
Zoneline target
 -> NavMesh validation
 -> SimPlayer.AssignGuardSpot
 -> NPC.HighPriorityNavUpdate
 -> FollowController.Start(leader)
```

If the local tree still does this, preserve it.

Refactor only enough to let `ExpeditionCoordinator` own:

- start;
- pause;
- combat-interruption state;
- regroup state;
- transition state;
- arrival;
- cancel/fail;
- status/events.

Avoid a large rewrite of `LeaderController` before live regression tests.

---

## 8. Combat behavior

Start with the current local Lead combat detector.

In public 0.3.2 it is:

```text
GameData.InCombat
OR leader.IsSimGroupInCombat()
OR leader NPC.CurrentAggroTarget != null
```

Verify this locally.

On combat:

```text
Traveling
 -> CombatInterrupted
```

Then:

- clear expedition travel orders;
- stop local auto-follow;
- let Erenshor own combat;
- do not alter targets;
- do not issue attacks;
- do not teleport;
- do not automatically flee.

After the existing safety delay and verified combat clear:

```text
CombatInterrupted
 -> Regrouping
```

Only then:

```text
Regrouping
 -> Traveling
```

Test player, leader, and other-party-member combat separately.

---

## 9. Regroup behavior

Preserve current local thresholds first.

Public 0.3.2 currently uses approximately:

```text
wait if player gap > 8 m
resume if gap <= 4.5 m
current catch-up timeout ~12 s
```

Add state semantics around it.

After combat and after zoning, require a stable settled condition before reissuing travel.

Do not let the leader repeatedly bounce between movement and wait orders every frame.

---

## 10. Manual player movement

This is a required Expedition-quality improvement.

Plain Follow should keep its existing behavior.

During an active Expedition, if `FollowController` stops specifically because the player presses movement/jump:

```text
do not destroy the expedition
-> hold the leader
-> state = Paused
-> pause reason = PlayerManualMovement
```

Then explicit Resume / "keep going" may revalidate and continue.

Implement this without making Follow and Expedition recursively stop each other.

Use the current `FollowController.StopReason` if still present locally.

---

## 11. Zone transition and arrival

For v1, success is a real adjacent-zone transition.

Do not directly invoke scene change to fake progress.

Observe the actual transition.

Arrival condition:

```text
active scene == canonical verified destination
```

after any needed scene/group settle period.

When zoning:

- assume the old `SimPlayer` reference may become invalid;
- verify whether that is actually true locally;
- reacquire the leader only by a stable identity that you have proved;
- if identity cannot be made safe, v1 may finish on arrival and defer multi-leg continuation.

Unexpected zone:

```text
-> Failed or Cancelled with explicit reason
```

Do not guess.

---

## 12. Return

Implement only the safe v1 form:

After arrival, Return is available if the original zone is currently a live verified adjacent destination.

Then use the same state machine with:

```text
ObjectiveMode = Return
```

Do not store/replay origin coordinates.

Do not implement a speculative global return route.

---

## 13. Commands

Preserve backward compatibility with `/elead`.

Recommended explicit commands:

```text
/expedition
/expedition status
/expedition pause
/expedition resume
/expedition cancel
/expedition return
```

`/expedition return` must report unavailable when no deterministic live route exists.

Do not add redundant leader-selection commands if `/elead` and the action menu already solve selection.

---

## 14. Natural party commands

Keep deterministic parsing in Erenshor Follow.

The public tree already parses phrases equivalent to:

```text
Phanty, lead us to Azure.
Phanty, take us to Azure.
```

Retain this.

If added, keep control phrases small and explicit:

```text
hold here
keep going
let's head back
cancel the expedition
```

These must map directly to coordinator operations.

Do not route them through Ollama.

---

## 15. UI

Extend the existing Sim action menu and travel overlay.

Do not create a new permanent UI system.

Suggested menu behavior:

```text
START EXPEDITION
<verified adjacent zones>
```

During an expedition:

```text
Pause / Resume
Return          // only if available
Cancel
```

Overlay should show:

```text
leader
destination
expedition state
```

Use read-only status snapshots.

No UI getter/draw path should mutate gameplay state.

---

## 16. Deep Sims integration

Do not hard-depend on Deep Sims.

First make Expeditions complete and correct without it.

Then add an optional bridge.

Prefer a structured primitive-only lifecycle call so Deep Sims receives roles without parsing prose.

Recommended payload:

```text
event type
leader name
origin zone
destination
current zone
objective mode
combat interruption count
reason code
```

Initial events:

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

Do not cause chat directly.

Deep Sims decides whether to speak.

For v1 social importance:

- arrival may be a meaningful candidate;
- start/depart/resume/pause/combat should usually remain context/silent;
- only verified completed arrival should be eligible for leader-specific long-term shared-history memory.

If the current local Deep Sims lacks a structured method, use Practice Duels' reflection bridge pattern only as a compatibility fallback.

Do not make a generic prose event prove "I led you there" memory.

---

## 17. Camp / Relax integration

No hard dependency.

Expose/emit verified arrival.

If a Camp/Relax capability exists, let it subscribe/detect and offer:

```text
Camp Here
Relax Here
```

Do not automatically enter Camp on every arrival.

---

## 18. Co-op

Re-run the current Follow ownership guard every time a leader is acquired or reacquired.

Never drive:

```text
ErenshorCoop.NetworkedPlayer
ErenshorCoop.Client.NetworkedPlayer
ErenshorCoop.NetworkedSim
```

if those are still the correct local types.

If COOP internals differ locally, update the compatibility check before shipping Expeditions.

Fail closed on unknown network ownership.

---

## 19. Live test matrix

Do not consider the feature complete after compilation.

### Baseline regression

- `/efollow` still works.
- manual input still stops plain Follow.
- existing `/elead` still works.
- existing action menu still opens/closes correctly.
- Travel overlay still works.
- optional Practice Duel action still works.

### Expedition happy path

1. group with a local Sim;
2. start to a listed adjacent zone;
3. observe Sim native movement;
4. observe player following Sim;
5. cross actual zone boundary;
6. verify correct new scene;
7. verify one arrival;
8. verify movement stops cleanly.

### Combat tests

Run separate cases where:

- player pulls combat;
- leader pulls combat;
- another party Sim pulls combat.

Verify:

```text
travel pauses
combat stays native
travel waits after clear
group catches up
travel resumes
```

### Manual movement

- press WASD during expedition;
- expedition pauses rather than disappears;
- leader waits;
- Resume works;
- plain Follow behavior remains unchanged.

### Regroup

- fall behind;
- leader waits;
- catch up;
- leader resumes;
- test timeout.

### Failure

- leader leaves party;
- leader dies;
- route stalls;
- destination disappears/invalidates;
- unexpected zone transition;
- cancel during combat;
- cancel during regroup;
- unload plugin.

### COOP

If available:

- networked human is never offered as leader;
- networked Sim is never offered as leader;
- local Sim still works;
- no local navigation fights remote synchronization.

### Deep Sims

With Deep Sims missing:
- zero errors.

With Deep Sims installed:
- verified arrival delivered once;
- no required Ollama;
- social layer may choose silence;
- no fabricated "I led" history without completed structured event.

---

## 20. Build and diff requirements

Build using the repository's current installed-game build path, not a stale SDK/reference set.

After implementation:

```text
build
install to the intended local BepInEx profile
launch game
run live tests
exit
inspect BepInEx log
git diff --check
git diff
git status
```

Before stopping, summarize:

```text
files changed
behavior added
local Assembly findings
tests run
tests passed
tests not run / still needing live game
known limitations
diff risks
```

Do not commit/push unless asked.

---

## 21. Stop conditions

If local DLL inspection disproves a core public assumption, do not force the design.

Examples:

- `DestinationZone` is not a reliable scene identity;
- leader object cannot be safely reacquired;
- current group state is not stable when expected;
- `HighPriorityNavUpdate` is unsafe in the current build;
- COOP ownership cannot be established.

In that case:

1. preserve the current working Follow behavior;
2. implement only the subset that remains safe;
3. document the contradiction;
4. leave the risky phase unimplemented.

Partial safe implementation is preferable to speculative automation.

---

## 22. Definition of done for the first implementation

The first implementation is done when all of the following are true:

```text
[ ] current Follow behavior is not regressed
[ ] current Lead behavior is not regressed
[ ] one active Expedition has explicit lifecycle state
[ ] destinations are live verified adjacent zones
[ ] real combat interrupts without replacement combat logic
[ ] regroup happens before resume
[ ] manual movement pauses an Expedition rather than silently destroying it
[ ] real zone transition produces exactly one verified arrival
[ ] cancel/failure cleanup leaves no stuck movement/guard state
[ ] remote/networked COOP entities are never driven
[ ] feature works with Deep Sims/Ollama absent
[ ] optional event bridge is exact-once and grounded if implemented
[ ] final diff has been reviewed
[ ] live-game gaps are documented
```
