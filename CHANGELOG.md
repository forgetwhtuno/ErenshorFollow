# Changelog

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

- Added optional, versioned `FollowControlApi` discovery/control surface for Suite Hub without a hard Hub dependency.
- Kept standalone commands and core gameplay authority intact.
- Documented the retained panel/launcher policy and Lunaris live-test requirement.
- Gated contextual travel UI until stable in-world state and added a primitive-only Hub status/action surface. Follow/Lead/Expedition movement logic is unchanged.
