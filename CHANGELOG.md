# Changelog

## 0.6.4 - RC telemetry defaults

- Aligned Suite Escape ownership with the current three-state suite contract (`StandaloneFallback` / `ExplicitCloseControls` / `HubVerified`). Follow previously polled Escape whenever verified Hub quick-close was absent, including while a Hub was loaded with `quickClose=0`; it now stops polling as soon as a Hub is present, so Forgotten Roads modules cannot compete for the same key. A live Hub IPC endpoint counts as presence even when a describe payload is malformed that frame. No compile-time dependency on Hub or Party Tools was added, and window close remains presentation-only for Follow/Lead/Expedition runtime.
- Keeps movement writer/NavMesh/animation heartbeats and detailed route phases behind the existing explicit Verbose diagnostics setting.
- Retains bounded default lifecycle evidence for expedition failure, cancellation, arrival, and exact leader reacquisition.
- Adds deterministic telemetry policy tests; Expedition movement, ownership, crossing, and reacquisition behavior is otherwise unchanged.

## 0.6.3 — Expedition movement ownership and locomotion repair

- Added a narrow, exact-leader Harmony prefix for verified `SimPlayer.DoGuard()` contention. Vanilla Guard is suppressed only while an active Expedition owns ordinary pre-crossing travel; combat, holds, regroup, native party commands, crossing/zoning and every non-leader Sim remain native.
- Kept `AssignGuardSpot` as the selected Sim's grouped-travel mode while suppressing its competing periodic Guard writer; this prevents both native stop/idle resets and the double-`randomizeOffset` destination rewrite during owned travel.
- Added an ownership-aware NavMesh movement adapter that restores usable native run speed, un-stops the exact leader, and sets `Walking`/`Patrol` only from actual agent velocity/desired velocity/position delta. The adapter releases before crossing/native zoning and failed order acquisition; no transform movement, Warp, teleport, or manual scene loading was added.
- Added owner/order generations plus bounded change-only movement telemetry and a one-second heartbeat only while stalled, including Guard/native NavMesh/animation/combat/crossing state.
- Preserved native Group Guard/Follow/Run Away results instead of immediately overwriting them; Expedition pauses/yields and only explicit/current policy can reacquire travel.
- Added pure movement-ownership/speed/locomotion/generation tests and one-hop/multi-leg workflow advancement coverage.
- Normalized Follow retained drag ownership to left-button pointer-down acquisition, physical/focus/pause/lifecycle cleanup and prior-native-state restoration. Added the verified monotonic `CameraController.UsingUI()` postfix for standalone modern-camera containment.
- The DoGuard patch validates its exact zero-argument void shape. The camera patch performs the stronger current-runtime IL/member proof required by the Suite drag contract before applying. Either compatibility failure leaves native behavior unpatched.

## 0.6.2 — Native Zoneline crossing handoff and bounded route recovery

- Fixed the live final-boundary regression where a **Complete** approach route could stop roughly half a meter from a Zoneline and immediately fall into generic no-progress/candidate cycling. Complete routes now enter the same explicit crossing phase instead of being excluded from boundary handling.
- Replaced AABB-only crossing distance checks with true trigger-shape distance via `Collider.ClosestPoint` (bounds remains exception fallback), eliminating false-near approach decisions on rotated/non-box trigger volumes.
- Added bounded trigger traversal planning: after reaching the verified approach, the planner samples a few NavMesh targets that actually cross the selected live trigger and requires the path segments to intersect that trigger. The selected Sim receives the existing verified native movement order; no transform warp, `ZoneSim`, or scene-load call is used.
- Added a read-only Harmony observation of `Zoneline.OnTriggerEnter` so the expedition can distinguish exact leader trigger entry, player trigger entry, and later `GameData.Zoning`. If the Sim zones first and its scene avatar is destroyed, the exact `SimPlayerTracking` identity is preserved for a bounded player-trigger handoff rather than being misclassified as ordinary leader loss.
- During that leader-first gap, the existing player Follow movement owner may continue only toward the same already-proven through-trigger target for the bounded grace window. It drops immediately when the player's native trigger fires or `GameData.Zoning` begins; no scene transition or destination is synthesized.
- Added one bounded event-time route re-sample after all pre-built geometry candidates fail. Movement-ownership failures do not use this geometry retry.
- Added phase-boundary telemetry for command admission, leader validation, target/exit/candidate selection, approach reached, crossing attempt, trigger entered/not entered, native transition observation, destination scene, exact leader reacquisition, next-leg revalidation, arrival/failure/cancel. Telemetry is de-duplicated and does not log every frame.
- Preserved Sim Actions retained UI revision 2 and its 236 px / 28 px / 3 px compact model; rows now explicitly set `flexibleHeight=0` so Unity cannot stretch them into blank space.
- Added deterministic crossing-policy coverage plus release static assertions for true-shape crossing distance, trigger traversal, bounded retries, telemetry wiring, and forbidden teleport/manual-scene APIs.

## 0.6.1 — Sim Actions retained-layout identification and sizing repair

- Fixed the custom Follow **SIM ACTIONS** retained `VerticalLayoutGroup` so it actually controls child row heights. The previous `childControlHeight=false` setting allowed default RectTransform heights to override the 32 px model and could make the context surface look much larger than intended.
- Tightened the custom context surface to a 236 px width, 28 px actions, 18 px status rows, smaller spacing/padding, and a 320 px scrolling cap while preserving the existing screen clamp and exact-clicked-Sim identity.
- Added `/efollow ui` diagnostics and a visible startup revision marker so live tests can distinguish the Follow-owned custom context surface from Erenshor's separate native party-command stack.
- Bumped the plugin version to **0.6.1** so Hub/runtime output can prove that this UI repair is actually installed.
- No native party command, combat, grouping, route, zoning, or movement behavior changed.

## Unreleased - Expedition destination / movement execution hardening

- Made the retained **SIM ACTIONS** context surface content-driven again with action-count-driven height, bounded scrolling for genuinely long menus, visible X, and the existing screen clamp. The 0.6.1 sizing repair later tightened the final row/width metrics. Exact clicked `SimPlayer` identity remains the action target.
- Fixed multi-zone atlas traversal so `ZoneAtlasEntry.NeighboringZones` is treated as authored adjacency even when only one endpoint records the relationship. The current scene's live usable `Zoneline` set still authorizes hop 1, and every later hop is re-planned/re-resolved after native zoning. No hardcoded zone list or scene-load path was added.
- Hardened expedition departure so route geometry and movement ownership are separate proofs. The selected Sim now releases its prior guard/follow posture via `FreeFollow()`, receives the new `AssignGuardSpot`, resolves its native NPC through the previously verified `GetThisNPC()` accessor, and must accept `HighPriorityNavUpdate()` before the leg is considered ordered.
- Added bounded startup movement acquisition: visible transform/distance/velocity progress proves execution; stopped agents, missing destinations, unavailable NPC ownership, disabled/off-NavMesh agents, and zero progress are diagnosed separately. Only `PathInvalid` is treated as route-candidate geometry; ownership/state failure no longer burns through every Complete approach candidate.
- Added `/expedition diag` for concise atlas/live-first-hop/reachable-route, exact `SimPlayerTracking` rebind, and native NPC/NavMeshAgent movement telemetry. Diagnostics are command-triggered rather than emitted every frame.
- Added deterministic policies/tests for compact Sim Actions sizing, asymmetric multi-hop atlas enumeration, stale live-leg authority, movement acquisition/reissue/failure, stopped/sitting startup behavior, and path-invalid candidate fallback.
- No Sim task/sitting fields, `GroupFollow`, teleport, manual scene load, cross-scene object movement, or native combat movement ownership were added.

## 0.6.0 — Expedition player workflow

- Replaced the adjacent/command-oriented normal expedition entry flow with **Sim Actions → Create Expedition → dedicated retained-uGUI setup window**. Commands remain compatible/recovery surfaces.
- Added a scrollable destination planner backed by the authored `ZoneAtlas`, organized into Nearby and Other Reachable Zones, with immediate route preview and transition count. Destinations are advertised only when the current candidate route begins through a verified live usable zoneline.
- Start now revalidates the exact captured `SimPlayerTracking`, current avatar identity, living/local-party ownership, remote-COOP exclusion, current scene, atlas route, and live first leg before the coordinator owns travel.
- Added a compact persistent Expedition Status surface with Traveling/Paused/Combat/Regrouping/Changing-zones states plus Pause, Resume, Cancel, verified Return, and capability-gated Camp Here. Closing or Escape-hiding status is presentation-only and never cancels runtime travel.
- Kept Expedition Status visible across native zoning so it can report `Changing zones... Reacquiring <leader>...` while Erenshor owns the transition. Setup/context windows still close on lifecycle loss.
- Recalculate the remaining route after every verified zone entry; every newly current leg must again resolve to a live, active, non-`RemoveParty` zoneline. No teleport, scene load, or synthetic zoning path was added.
- Aggregated Sim Actions, Expedition Setup, and Expedition Status under the existing optional Suite `ui.state`/`closePanel` quick-close contract; the topmost Follow surface closes once per Escape while expedition runtime state remains intact.
- Added pure deterministic route-graph and workflow-policy suites plus release source assertions covering multi-hop reachability, unavailable routes, exact identity/remote rejection, status controls, arrival capability gating, UI-close semantics, route recalculation, and unload cleanup wiring.

## Unreleased - deep Follow / cross-zone playable-state hardening

- Put direct cross-zone Follow continuation behind a new `Follow/ExperimentalCrossZoneFollow` setting that defaults OFF until the current installed build is compiled and live-verified. The implementation still observes only native `GameData.Zoning`; it never initiates a scene change or teleports an actor.
- Hardened direct Follow's persistent identity lifecycle: only the originally captured `SimPlayerTracking` can resume after a native player zone transition, with the same real-group, living-avatar, exact-identity, and remote-COOP rejection guards on the far side.
- Added a 2.5-second pre-zone handoff grace for the native ordering where a followed Sim's old avatar disappears immediately before the player's own zoneline trigger fires. The grace yields vanilla player control, preserves only the exact tracking identity, and cancels unless real `GameData.Zoning` begins or the exact local avatar returns.
- Direct Follow now yields the `PlayerControl.LandMovement` patch during real combat and for a short post-combat safety window, allowing ordinary Erenshor combat movement to remain authoritative. Deliberate movement outside that combat handoff still cancels ordinary Follow.
- Replaced the abrupt direct-Follow five-second stall cutoff with a bounded recovery policy: spaced route recomputes, a maximum of three recovery attempts, and a clean stop after a nine-second no-progress bound. No teleport/noclip fallback was added.
- Avoided `CharacterController`/animation cleanup calls after native zoning begins; Follow now drops scene-bound movement references without touching player movement during game-owned transition teardown.
- Made player death/unavailability an explicit direct-Follow stop reason and added concise `/efollow status` diagnostics for state, persistent identity, current avatar, party/COOP authority, scene, last repath, recovery attempts, cross-zone flag, and last stop reason.
- Hardened expedition startup so a multi-zone trip cannot begin without a captured `SimPlayerTracking` that is actually present in `GameData.GroupMembers`.
- Hardened expedition transitions to wait for the exact persistent leader identity after each native zone load instead of treating a settled scene alone as success. Late `MyAvatar` rebinding waits within the existing bounded timeout; party loss, remote authority, identity mismatch, death, and timeout fail closed.
- Fixed final arrival so `Arrived` cannot be emitted if the tracked leader failed exact-identity reacquisition. Return-leader reacquisition uses the same identity guard.
- Changed the bounded "group could not catch up" expedition outcome from terminal route failure to an explicit paused state; the player can regroup and `/expedition resume` or cancel.
- Preserved explicit expedition pause state across an expected intermediate native zone transition; the next leg is rebuilt then immediately held until the player resumes, while final arrival clears stale pause state.
- Typed Sim lookup now treats duplicate exact display names as ambiguous instead of selecting the first actor; click-based Sim Actions remain bound to the selected object.
- Sim Actions now offers `Stop Following` for the exact current direct-Follow target, hides ordinary Follow while an expedition owns travel, and avoids a duplicate generic stop row.
- Expanded the retained travel overlay with combat-recovery and safe-repath states.
- Added deterministic pure-policy coverage for the experimental-zone gate, identity/rebind failures, bounded stuck recovery, and direct-Follow combat handoff.
- No player/Sim teleport, `SceneChange.ChangeScene`, `ZoneSim`, `TravelToZone`, cross-scene pathfinding, party mutation, or new combat automation was introduced.

## Unreleased - playable-state / retained Sim Actions pass

- Replaced the remaining production IMGUI Sim Actions menu and travel-status overlay with compact retained uGUI using the established dark/cyan suite visual language. No second `EventSystem` is created.
- Removed the legacy normal-access F8/middle-click UI path. Native local-party Sim clicks remain the contextual entry point, with commands preserved as recovery/compatibility access.
- Added the shared optional `ui.state` + `closePanel` contract. Local Escape remains available unless a usable Hub advertises verified quick-close and Follow's own Aura provider registered successfully.
- Restricted direct `/efollow <name>` lookup to verified living local-party Sims and continuously stop direct Follow if the actor dies, becomes remote-authority, or leaves the current party.
- Release previous movement ownership before switching directly from one follow target to another.
- Hide the retained travel overlay whenever gameplay readiness drops during zoning/character transitions instead of leaving a stale `DontDestroyOnLoad` surface visible.
- Migrated retained UI persistence to normalized bottom-left coordinates while treating old pixel values as unset rather than mis-scaling them.
- Added pure tests for Hub presence/quick-close gating, `ui.state`, local-party actor eligibility, legacy-position recovery, and descriptor action exposure.
- No NavMesh route policy, zoneline authority, teleport behavior, expedition state machine, combat ownership, or remote-human admission was widened.

## 0.5.0 — Native Lunaris migration

- Migrated off BepInEx 5 onto native Lunaris: `BaseUnityPlugin`/`[BepInPlugin]`/`[BepInProcess]`/
  `[BepInDependency]`/`Logger` replaced by `LunarisPlugin`/`[LunarisPlugin]`/
  `[LunarisPermission(Reflection | Harmony)]`/native `Logging`. `BepInEx.Configuration.ConfigEntry<T>`
  replaced by a new typed `FollowSettings` class (`[Config]` fields) plus a small
  `FollowConfigEntry<T>` compatibility shim. All 5 existing settings
  (`UI/OverlayOffsetX`, `UI/OverlayOffsetY`, `Diagnostics/Verbose`, `UI/OverlayPositionX`,
  `UI/OverlayPositionY`) are preserved verbatim (section/key/default/description).
- `TravelStatusOverlay`'s legacy offset-to-position first-run migration (a user's old
  `OverlayOffsetX`/`Y` seeding an initial screen-relative panel position) is preserved, adapted to
  detect "first run" by checking whether the position config is still at its compiled-in default
  rather than relying on a lazy `ConfigFile.Bind`, since Lunaris settings are registered upfront.
  Native Lunaris config does not auto-persist a `.Value` write to disk the way BepInEx's
  `ConfigEntry` did, so dragging the panel now explicitly calls `Config.Save()` afterward.
- The `[BepInDependency(..., SoftDependency)]` declaration on Deep Sims is not carried forward:
  Deep Sims compatibility here has always been reflection/Harmony-owner-ID based
  (`DisableEmbeddedDeepSimsFollow`, `CoopCompatibility`, `ExpeditionIntegrationBridge`), not
  dependent on a declared loader dependency, so nothing about that compatibility path changed.
- **Fixed a hot-reload event leak** in `CoopCompatibility.cs` and `ExpeditionIntegrationBridge.cs`:
  both previously subscribed `AppDomain.CurrentDomain.AssemblyLoad` to an anonymous delegate from
  a static constructor, with no corresponding unsubscribe. Converted to a named handler behind
  explicit `Initialize()`/`Reset()` methods, wired from the plugin's `Awake()`/`OnDestroy()`, so
  unloading the plugin actually unsubscribes instead of leaking a reference into the old assembly
  across a Lunaris hot reload. This is the same anti-pattern already found and fixed in three other
  mods during this migration series.
- This is a loader/config/logging/lifecycle migration only: no follow/lead/expedition/route
  logic, command grammar, or NavMesh/zoneline handling changed. Every Harmony patch target was
  re-verified against the currently installed `Assembly-CSharp.dll`.
- `BUILD_AND_INSTALL.ps1` rewritten for Lunaris: install target is now
  `<Erenshor>\plugins\ErenshorFollow.dll`; reference resolution now looks for a Lunaris developer
  folder (`Lunaris.dll`/`0Harmony.dll`) instead of a BepInEx profile root; all
  r2modman/Thunderstore BepInEx-profile auto-detection removed.
- Verified: real compile against the installed Erenshor + Lunaris assemblies, zero `BepInEx`
  references in the compiled output, the full existing deterministic test suite (`RUN_TESTS.ps1`
  — route candidate policy, natural-language lead grammar, cross-zone rebind policy, and UI/Camp
  handoff policy) still passes unchanged, and a static hot-unload audit (the
  `SceneManager.sceneLoaded` subscription is unsubscribed in `OnDestroy()`, `Harmony.UnpatchSelf()`
  is called, the static plugin instance is cleared, and both `AssemblyLoad` subscriptions described
  above are now properly torn down).
- Not yet done: live in-game verification under Lunaris, including movement-assist/expedition
  behavior around real zone transitions and repeated unload/reload while Follow or an Expedition is
  active.

## 0.4.2 — Zoneline trigger geometry correction

- Restrict Zoneline geometry sampling to enabled trigger colliders. Large solid child colliders can belong to nearby rocks or terrain; they no longer make a point beside an obstruction appear to be the transition itself.

## 0.4.1 — Multi-zone expedition continuation

- Added multi-zone Expeditions using Erenshor's authored `ZoneAtlas` only to choose a bounded itinerary. Every next hop must still resolve to a live, active, non-`RemoveParty` `Zoneline` after the new scene loads; missing or unexpected hops fail closed, and Follow never invokes a scene change or teleport.
- Continue an active Expedition after each verified zone load by reacquiring the exact `SimPlayerTracking.MyAvatar`, revalidating party/local authority, and starting the next live leg. `/expedition status` reports the final destination, next exit, and leg count; multi-zone Return uses the same safeguards.
- Preserve the selected complete NavMesh path's corner sequence and advance the leader through those bounded local waypoints. This prevents native steering from flattening a valid around-the-rock route into a direct push at the final Zoneline approach.

## Unreleased — Follow reliability, UI, and integration consolidation

- Expanded the deterministic party-chat lead grammar with `lead the way to`, `lead the group to`, `show us the way to`, and `guide us to`, while leaving conversational/question forms outside direct gameplay ownership.
- Reworked the travel overlay into a minimap-safe, draggable, persisted position with onscreen recovery after resolution changes.
- A verified Expedition arrival remains visible briefly and can expose atlas-routed **Return** plus optional **Camp Here** through a reflection-only Campmaster control bridge.
- Campmaster remains optional and Follow never assigns roles, toggles Auto Pull, chooses targets, attacks, heals, or changes saves through the handoff.
- Consolidated deterministic regression runners for command grammar, cross-zone rebind policy, route policy, and UI/Campmaster action policy.
- Resolve a canonical adjacent destination to **all** live usable Zonelines with that destination name; duplicate crossings to the same destination are alternatives rather than ambiguity.
- Derive a small bounded set of crossing approaches from the live Zoneline transform, collider bounds, current-side hints, and nearby NavMesh samples instead of treating `Zoneline.transform.position` as the one route target.
- Rank complete routes first, allow only meaningful boundary-near partial routes, and keep `RemoveParty` as a hard rejection.
- Give Erenshor's native Sim navigation one short bounded proof-of-progress window when startup NavMesh preflight is inconclusive but a verified crossing has a plausible sampled approach.
- Retry the next pre-ranked approach/crossing monotonically; report no route only after the finite verified candidate list is exhausted.
- Added read-only `/elead diag <zone>` route diagnostics with scene, canonical name, crossing/collider geometry, NavMesh/path status, endpoint distance, rejection reason, and selected candidate.
- Removed the first-three destination truncation from the Sim action menu and added a compact scroll viewport for larger real adjacent-exit lists.
- Added pure deterministic route-policy tests covering duplicate crossings, useful/useless partial paths, `RemoveParty`, ambiguity, all-invalid failure, and enumeration-order independence.
- Monster/NPC lead retains its existing stricter route preflight; atlas itineraries apply only to zone Expeditions and add no teleport or scene-change authority.
- Plain `/efollow` now separates persistent target identity (`SimPlayerTracking`) from the current scene-bound `SimPlayer` avatar.
- A verified `GameData.Zoning` transition temporarily suspends direct Follow instead of treating destruction of the old avatar as ordinary target loss.
- Follow releases stale scene/movement references during zoning, waits for the new scene and Erenshor group state to settle, then reacquires only `SimPlayerTracking.MyAvatar` for the originally captured tracking object.
- Rebind re-runs living/active, authoritative real-group, exact tracking-identity, and COOP-local-authority checks. It never substitutes a same-named Sim.
- Rebind uses a bounded 2.5-second settle window and 60-second overall timeout, matching the scale of the existing Expedition transition lifecycle; failures stop cleanly with a specific diagnostic.
- The travel overlay shows `Rebinding after zone change` rather than claiming active movement while no live avatar exists.
- Ordinary non-zoning target loss and manual-movement cancellation retain their existing behavior; a user Stop/cancel during rebind clears the retained tracking identity and cannot auto-resume later.
- No scene-change, Sim-zone, spawn, or teleport behavior was added.
- Added pure deterministic transition-policy tests and source-contract checks for zoning, identity, party/COOP rejection, cancellation, timeout, repeated transitions, and non-zoning target loss.
- Expedition arrival now also requires the tracked leader to be reacquired and regrouped after the verified native scene transition; leader loss no longer reports a successful arrival.
- Aligned Follow's deterministic natural travel grammar with Deep Sims' ownership mirror while keeping conversational/question forms out of movement handling.
- Gated high-frequency Sim action-menu click diagnostics behind `Diagnostics.Verbose` and let outside-menu clicks close the menu without swallowing the underlying Erenshor click.

## 0.4.0 — Sim-Led Expeditions (phase 1)

Zone travel is now an explicit, resumable, observable **Expedition** instead of a one-shot lead. Erenshor
still owns combat, Sim navigation, grouping, and zone transitions; this release only adds the lifecycle
around them.

- Added `ExpeditionCoordinator`, the single owner of expedition state: `Idle`, `Forming`, `Traveling`,
  `CombatInterrupted`, `Regrouping`, `Paused`, `Transitioning`, `Arrived`, `Cancelled`, `Failed`, with
  `Outbound` and `Return` objectives.
- Added `ExpeditionDestinationResolver`: destinations come only from live, active `Zoneline` objects in
  the loaded scene. No coordinates, no wiki routes, no generated place names, and `ZoneAtlas` is
  deliberately not used as a runtime route source.
- Rejected `Zoneline`s with `RemoveParty` set as expedition destinations. Crossing one force-dismisses the
  whole party, so the leader can never arrive with you; the refusal is now explained up front.
- Preserved the existing native-backed leader movement chain unchanged:
  `AssignGuardSpot` -> `NPC.HighPriorityNavUpdate` -> `FollowController.Start`.
- Preserved the existing combat detector and the five-second post-combat safety delay. Real combat moves
  the expedition to `CombatInterrupted`; no replacement combat AI, target manipulation, or automatic flee.
- Added an explicit `Regrouping` step: after combat clears, the leader holds position and following
  resumes so the group closes the gap, and travel is only reissued once the gap has been within resume
  range for a short settle window. This also removes guard/travel oscillation on the resume threshold.
- **Manual movement now pauses an expedition instead of destroying it.** Pressing WASD or jump during an
  expedition holds the leader and enters `Paused (PlayerManualMovement)`. Plain `/efollow` keeps its
  original stop-on-input behavior.
- Recognized real zone transitions and emitted exactly one verified arrival: the game must actually
  transition and the new active scene must equal the canonical destination. Reaching the border is not
  arrival, and no scene-change call is ever made to fake progress.
- Reacquired the leader after zoning through its `SimPlayerTracking`, re-running every usable, alive,
  party-membership, and COOP-ownership guard before trusting it.
- Added `/expedition status|pause|resume|cancel|return`, plus exact-match party phrases `hold here`,
  `keep going`, `let's head back`, and `cancel the expedition`.
- Added safe one-hop `Return`, available only while the origin zone is currently a live verified adjacent
  destination. Origin coordinates are never stored or replayed.
- Observed native party orders instead of fighting them: `GroupGuard` pauses, `GroupFollow` resumes only a
  guard-caused pause, and `RunAway` marks an external override so the resulting zone is never an arrival.
- Extended the existing Sim action menu and travel overlay rather than adding a second UI.
- Added an optional Deep Sims bridge that prefers a structured `NotifyExpeditionEvent` and falls back to
  `NotifyObservedGameEvent`. Terminal events are emitted exactly once, nothing is marked as durable memory
  through the prose fallback, and Follow never invokes an LLM.
- Routed `/elead <Sim> <zone>` and the equivalent party phrases into the coordinator, removing the second,
  divergent zone-resolution path so the menu, `/elead zones`, and the commands can no longer disagree.
- Expeditions are not persisted across save/load; plugin unload cancels cleanly.

Not implemented on purpose: multi-zone routing, teleport fallbacks, arbitrary coordinates, wiki-driven or
LLM-generated destinations, automatic leader replacement, and replacement combat AI. See
`docs/EXPEDITIONS_LOCAL_ASSEMBLY_FINDINGS.md` for what the installed assembly actually verified.

## 0.3.2 — Party-Sim Action Menu and Travel UI

- Added an Erenshor-style action menu for living local Sims in the current party.
- Opened the menu from native character target selection, including repeat clicks on an already-selected Sim.
- Added reliable Escape, Cancel, and outside-window dismissal without treating title-bar dragging or button clicks as outside input.
- Reset pending click, selection, suppression, and window state on every close path.
- Preserved F8 and middle-click fallbacks without opening from stale party targets after terrain, loot, UI, or non-party clicks.
- Added Follow, verified Lead destinations, optional Practice Duel integration, travel status, and a Stop control.
- Excluded remote COOP humans and retained runtime-only optional companion detection.

Future compatibility work may expose stable read-only travel lifecycle state and verified arrival notifications without giving Follow responsibility for Deep Sims memory.


## Unreleased - Suite UI/API coherence handoff

- Isolated Sim Actions Escape ownership behind `SuiteQuickCloseCompatibility`: standalone Escape remains unchanged while the shared `ui.state`/verified `quickClose` wire contract is absent. The seam can defer local Escape once that capability is explicitly supplied, without adding a second Escape hook.
- Exposed Sim Actions' local open/activation/close seam internally for future shared quick-close wiring. No launcher, navigation, NavMesh, zoneline, party/COOP eligibility, combat pause/resume, or teleport behavior changed.
- Added optional, versioned `FollowControlApi` discovery/control surface for Suite Hub without a hard Hub dependency.
- Kept standalone commands and core gameplay authority intact.
- Documented the retained panel/launcher policy and Lunaris live-test requirement.
- Gated contextual travel UI until stable in-world state and added a primitive-only Hub status/action surface. Follow/Lead/Expedition movement logic is unchanged.
