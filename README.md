# Erenshor Follow 0.6.22

Part of the **Forgotten Roads for Erenshor** mod collection.

Erenshor Follow adds player movement assistance, Sim-led travel, and expedition coordination around existing Erenshor zone transitions. It is separate from Deep Sims; when the known embedded Deep Sims follow movement prefix is detected, standalone Follow disables that prefix by Harmony owner ID so only one movement owner remains. Other Deep Sims integration stays reflection-based and optional, not a loader-level dependency.

## Status: playable expedition workflow with experimental direct cross-zone Follow

The current local patch keeps Erenshor authoritative for movement, combat, NavMesh, party state, zonelines, actor spawning, and scene transitions while hardening Follow's coordination around those systems. Sim Actions and the travel-status overlay are retained uGUI (no production `OnGUI`/`GUILayout` path), normal access has no global F8-style hotkey, direct Follow accepts only living authoritative local-party Sims, and switching targets releases the previous movement ownership before rebinding. The optional Suite contract exposes `ui.state`/`closePanel`; Escape ownership follows the shared suite contract described under [Suite quick-close ownership](#suite-quick-close-ownership), so Follow only polls Escape itself when no Forgotten Roads Hub is loaded at all.

Direct Follow now has an **OFF-by-default** `Follow/ExperimentalCrossZoneFollow` setting. When enabled, it may preserve the exact `SimPlayerTracking` identity through a native player zone transition, wait for Erenshor to rebuild the party, and reacquire only that tracking object's new `MyAvatar`. It never initiates zoning, scene loading, Sim spawning, or teleportation. Expeditions use the same persistent identity rule for every native multi-zone transition and never report arrival if the tracked leader failed to reacquire.
If the followed Sim disappears a moment before the player starts native zoning, Follow uses only a bounded 2.5-second handoff grace: vanilla player control is restored while the mod waits for `GameData.Zoning` or the exact local avatar to return. If neither happens, Follow stops instead of guessing a crossing or destination.

The deterministic suites pass for the current source snapshot, and the current source builds and installs cleanly against the installed game/Lunaris assemblies with the built and installed DLL hashes verified equal. Release-default logging keeps detailed movement/writer and route-phase traces behind the existing `Diagnostics/Verbose` setting while retaining bounded failure, cancellation, arrival, and leader-reacquisition outcomes. 0.6.5 repaired the Expedition setup/status presentation layer (compact destination rows, an explicit Start result instead of silent success/failure, and a visible reason on a mid-expedition cancel/failure). 0.6.6 repaired a route-planning gap found live at Duskenlight Cove: a crossing whose trigger is a tall vertical volume could have every seed (transform position, collider center, party-closest point) sit many meters above real walkable ground, producing zero NavMesh samples and zero accepted candidates even though the crossing itself was live and correctly identified. The planner also seeds the collider bounds' floor face, only when the collider is tall enough for that to matter; every seed - old or new - still has to pass the same NavMesh sample, CalculatePath, and approach-distance acceptance policy as before, so this changes only *where* Follow looks, never *what counts* as a valid route. 0.6.7 repairs a separate, live-proven-different gap: with a valid leader route confirmed (PathComplete, 13 corners), the player following the leader could still get physically stuck running into a tree. The player has no NavMeshAgent (only a `CharacterController`, confirmed by inspecting the installed game assembly), so it steers by NavMesh corner-following already, not raw direct-to-leader steering - but when physically blocked, the only recovery was recomputing the identical path from the identical stuck spot, regenerating the identical corner. Player follow now classifies why it stalled (blocked by local geometry vs. a moving leader vs. leader too far vs. a genuine no-route condition), tries a bounded lateral side-step probe specifically for a physical block before an ordinary repath, and aims at a trailing point behind the leader's heading instead of its exact position. The existing bounded stuck-recovery timeout that decides genuine failure is unchanged. 0.6.8 tunes follow distance and closes a speed-parity gap: the leader's native NavMeshAgent speed and the player's `CharacterController` speed both come from the same `actualRunSpeed` stat with no multiplier on either side, so once the player lost ground to a corner/sidestep/repath it was often mathematically unable to close it on a straightaway. Ordinary trailing formation is now noticeably tighter (nav-target stop/resume hysteresis tightened from 3.0/4.5m to 1.2/2.2m), and a new bounded, hysteresis-aware catch-up band applies a modest temporary speed multiplier (1.15x moderate, 1.30x strong, both on top of the player's own native speed, never written back to any persistent stat) only while genuinely behind, returning to exactly 1.0x the moment formation is recovered. A temporary 8-10m terrain-caused gap now correctly engages strong catch-up rather than drifting toward the existing stuck-recovery timeout; that timeout itself, and every obstacle-recovery/crossing mechanism above, is unchanged. Keep experimental direct cross-zone Follow OFF until the exact installed binary passes the multi-zone live matrix in `docs/FOLLOW_PLAYABLE_STATE.md`.

## Commands

```text
/efollow <SimName>       follow a nearby local Sim
/efollow status          concise follow/identity/repath/zone diagnostics
/efollow ui              report the custom Follow Sim Actions surface/revision
/efollow off             stop following
/dsfollow <SimName>      compatibility alias
/dsfollow off            stop following
/elead <SimName>         open/use Sim-led travel controls
/expedition status       expedition state and route
/expedition diag         route-discovery + exact-identity + native movement telemetry
/expedition pause        pause the current expedition
/expedition resume       resume a paused expedition
/expedition cancel       cancel the expedition
/expedition return       request a verified return leg
```

Natural group phrasing such as asking a Sim to lead to a named destination is parsed only when the leader and route resolve unambiguously. Route planning uses the local zone graph, known crossings, scene transitions, and verified arrival events; it does not invent a route.

## UI

The normal expedition workflow is now entirely player-facing retained uGUI:

1. click a valid living local-party Sim;
2. choose **Create Expedition** from **SIM ACTIONS**;
3. choose any destination for which `ZoneAtlas` has a plausible route beginning through a currently verified live zoneline;
4. inspect the immediate route preview and transition count;
5. press **Start Expedition**;
6. use the compact persistent **EXPEDITION** status panel for Pause/Resume/Cancel while the Sim leads through normal native crossings.

The setup window groups one-hop choices under **Nearby** and longer atlas routes under **Other Reachable Zones**. It stores the exact selected `SimPlayerTracking`, not a display-name lookup. Pressing Start revalidates that identity, living/local/party/non-remote authority, recomputes the route from the current scene, and proves the first leg again through a live usable `Zoneline`. A stale preview can never authorize movement.
Atlas `NeighboringZones` links are normalized as adjacency even when the authored relationship appears on only one endpoint. That normalization is candidate discovery only: it cannot authorize the first or any later leg. `/expedition diag` reports the current atlas/link counts, live first hops, and reachable-route count so asymmetric atlas data can be verified in-game without hardcoding a destination list.

At leg start, Follow releases only the selected Sim's previous guard/follow posture with the verified `FreeFollow()` seam, assigns the route target, resolves that exact avatar's native NPC via `GetThisNPC()`, and requires `HighPriorityNavUpdate()` to accept the order before treating travel as started. A Complete preflight NavMesh path is therefore no longer mistaken for proof that the Sim is physically moving. Initial movement is observed for a bounded window; stopped/missing/non-executing movement ownership is reissued a small number of times and then fails once, while a real `PathInvalid` may still select another verified approach candidate. No sitting/task fields, group-wide Follow order, teleport, or scene API are mutated.

During a native scene transition the status panel remains visible as presentation-only UI and reports **Changing zones... Reacquiring <leader>...** while the coordinator waits for Erenshor to rebuild the exact tracked Sim. After every successful reacquisition the final-destination route is recalculated rather than blindly consuming a stale itinerary. At arrival the panel becomes **EXPEDITION COMPLETE** and shows **Return** only when the verified route/leader record supports it, and **Camp Here** only when the optional Campmaster capability is actually available.

Closing the setup cancels planning only. Closing or Escape-hiding the expedition status **never cancels the expedition**; runtime travel continues and status can be reopened from Sim Actions. There is no global Follow UI hotkey. A small shared retained fallback entry point is automatically visible when Forgotten Roads Hub is absent/unavailable and hides while a healthy Hub owns primary access. The three Follow-owned retained surfaces advertise one aggregate `ui.state`/`closePanel` contract, with the topmost surface chosen by sort order + activation. Local Escape is only used when no Hub is loaded at all — see [Suite quick-close ownership](#suite-quick-close-ownership). The travel overlay position remains persisted as normalized bottom-left `UI/OverlayPositionX/Y`; old pixel offset values remain load-compatible but are not reinterpreted as normalized coordinates. Verbose click/route diagnostics are controlled by `Diagnostics/Verbose`.


### Native party-command menu vs Follow Sim Actions

Erenshor's own stacked party-command menu (`Attack`, `Assist`, `Follow`, `Pull Target`, `Auto Pull`,
`Guard`, `Run Away`, `Invite Group`, `Manage Roles`, `Loot Distribution`, etc.) is **native game UI**.
Erenshor Follow does not own or restyle that control stack. The Follow-owned **SIM ACTIONS** surface is a
separate retained-uGUI context menu containing Follow/Stop Following, Create Expedition, optional Practice
Duel, Expedition Status, and Cancel. Use `/efollow ui` to verify whether that custom surface is actually
open and which UI revision is loaded.

Version 0.6.2 keeps the revision-2 custom surface at the same 236 px / 28 px / 3 px compact metrics and now explicitly sets each retained row's `flexibleHeight=0`, so a Unity layout cannot stretch a two-action context menu into unused viewport space.

The same release replaces the old "reach a sampled approach and wait" expedition boundary behavior with a bounded **native crossing phase**. Route proximity is measured against each trigger collider's true shape (`Collider.ClosestPoint`) rather than only its world-space AABB. Once the selected Sim reaches the verified approach, Follow builds a small set of NavMesh targets whose path actually intersects the real live trigger, sends only the selected Sim through that trigger using the existing `AssignGuardSpot` + native NPC navigation seam, and observes `Zoneline.OnTriggerEnter` without changing native behavior. If the Sim enters first and its avatar is destroyed, the already-active player Follow owner gets only a bounded final walk toward that same pre-verified through-trigger target; the player's actual `Zoneline.OnTriggerEnter`/`GameData.Zoning` still owns the transition. Only native zoning and the real scene change advance the expedition.

### 0.6.3 Expedition movement ownership repair

Installed-build evidence showed that Expedition and vanilla `SimPlayer.DoGuard()` were concurrently writing the exact leader's locomotion. Expedition deliberately keeps `GuardSpot` as the verified grouped-Sim movement mode, but while the exact leader is in ordinary pre-crossing Expedition travel, a narrow Harmony prefix suppresses **only that leader's `DoGuard()` call**. Suppression is released for combat, explicit hold, regrouping, user Group Guard/Follow/Run Away control, crossing handoff, native zoning, failure/cancel/arrival cleanup, and every non-leader Sim. `SimPlayer.Update`, `NPC`, `NavMeshAgent`, combat AI, and native recovery continue running normally.

While that narrow travel owner is active, Follow sets the existing `NavMeshAgent.speed` from the leader's verified native `actualRunSpeed` when usable, un-stops the agent, retains the exact Expedition order through `HighPriorityNavUpdate`, and synchronizes `Walking`/`Patrol` only from actual `velocity`, `desiredVelocity`, or measured position delta. The adapter is released before crossing/native zoning and is not applied to the through-trigger handoff order. It never moves the Sim transform, warps, teleports, loads a scene, or forces Walking while stationary. Speed/animation restoration is ownership-aware: Follow restores only a value that still matches the last value Follow wrote; a newer native/external change wins.

Repair-build movement telemetry is change-only plus a one-second heartbeat while genuinely stalled. `/expedition diag` remains the concise on-demand view; the structured `[Expedition movement]` lines identify phase, exact leader, order/owner generation, last writer, position/delta, agent speed/path/destination, guard/randomization state, locomotion booleans, combat/hold/regroup/crossing/zoning state. This keeps first acquisition, corner progression, approach, crossing, native zoning, reacquisition, and next-leg failures distinguishable.

Follow's retained drag guard is also normalized to left-button ownership from pointer-down through physical-button/focus/pause/lifecycle release. A standalone-safe monotonic postfix on the verified `CameraController.UsingUI()` method reports native UI usage only while Follow owns an actual drag gesture, fixing modern-camera leakage without requiring SuiteHub.

## Safety and scope

- Movement is owned by this mod's deterministic follow/lead controller, not by an LLM.
- Locality, target identity, scene, NavMesh/crossing, zoning, and participant validity are checked continuously.
- Ordinary direct Follow cancels on deliberate non-combat movement takeover. During real combat it yields native player control, waits a short post-combat safety window, then resumes only if the player has not continued taking over.
- Stalls use a bounded, spaced repath policy and then stop cleanly; there is no teleport, noclip, per-frame path hammering, or cross-scene NavMesh attempt.
- With experimental cross-zone continuation disabled, direct Follow stops at native zoning. With it enabled, only a verified native zoning lifecycle may suspend intent and only the exact persistent Sim identity may resume it.
- Expedition manual takeover/falling-behind states pause rather than silently replacing native movement; invalid identity, party loss, remote authority, unexpected scenes, timeout, errors, or cancellation still fail closed.
- The mod does not choose combat actions, loot, spells, targets, quests, equipment, or party composition.
- COOP/remote-human compatibility is treated conservatively; a remote human is not accepted as a local Sim target.
- Removing the plugin leaves Erenshor's normal movement and Sim behavior intact.

## Expedition behavior

An expedition is a bounded multi-leg session: it records origin/current zone, final destination, route progress, persistent leader identity, pause/resume/cancel state, verified arrival, and optional Campmaster handoff. `ZoneAtlas` is **global candidate route knowledge only**. The setup screen may therefore list destinations several zones away, but every current hop must still resolve back to a live, active, non-`RemoveParty` `Zoneline` in the loaded scene before the local travel leg can begin.

The game performs the real player transition. The coordinator never invokes scene loading or teleports. It waits for the original `SimPlayerTracking.MyAvatar` to be rebound, re-runs local-party/non-remote/alive checks, then recalculates the remaining atlas route from the actual current zone and proves the new first hop through live scene geometry. Failed routing, unexpected zoning, or leader reacquisition is reported and fails closed rather than substituting a same-name Sim. Real combat automatically takes precedence; it does not require a settings checkbox.

## Installation

This is a **native Lunaris plugin** — BepInEx is no longer required for this version. Requires
Lunaris installed in your Erenshor install. The compiled DLL is placed directly in
`<Erenshor>\plugins\ErenshorFollow.dll`; Lunaris manages enable/disable.

## Build

`BUILD_AND_INSTALL.ps1` builds the standalone native Lunaris plugin against installed Erenshor and Lunaris assemblies, including the Unity uGUI/TextMeshPro references required by the retained UI. `RUN_TESTS.ps1` runs the pure deterministic route/workflow/identity/UI regression suites plus release source-contract assertions. Install Deep Sims separately if social dialogue integration is desired.

## Credits and Inspiration

### Compatibility / related projects

- **[Erenshor COOP](https://github.com/MizukiBelhi/ErenshorCoop) by MizukiBelhi** — important technical reference and compatibility target for remote-human/networked-Sim detection. I have also tested against a locally updated copy for recent Erenshor and Deep Sims compatibility.

## Development note

The goal is to build features for Erenshor, with development guided through design, testing, playtesting, audits, and iteration against the game. Bug reports, code review, corrections, and contributions from experienced Erenshor modders are welcome.

This is an unofficial, community-made mod for Erenshor and is not affiliated with or endorsed by the game's developer.


## Optional Suite Hub integration

Forgotten Roads Hub is **optional**. When it is installed, this mod can expose its normal player-facing controls there through the versioned public `FollowControlApi` surface. The mod remains independently usable without the Hub and does not compile against Hub types or assume Hub load order.

Follow keeps its contextual Sim action menu and travel overlay rather than inventing a second gameplay panel. The shared Hub-aware fallback entry point provides mouse discoverability when Hub is absent/unavailable; `/efollow`, `/elead`, and `/expedition` remain compatibility controls.

Hub can show current Follow/Lead/Expedition state and expose Stop plus the existing expedition pause/resume/cancel/return actions. Its Developer settings tier may also toggle the existing `Diagnostics/Verbose` setting; overlay coordinates remain owned by Follow's contextual UI rather than becoming Hub controls. The optional Aura surface also provides `ui.state` and `closePanel` for the contextual Sim Actions menu so the shared quick-close owner can close it without a second Escape hook. Erenshor remains authoritative for movement, NavMesh, zonelines, scene changes, combat, and identity.

### Suite quick-close ownership

Escape ownership has three states, shared across Forgotten Roads modules so that two mods never
compete for the same key press:

| Hub state | Who owns Escape | Player closes Follow windows with |
| --- | --- | --- |
| No Hub loaded (IPC endpoint absent) | Follow's own local fallback | Escape, or the window's X control |
| Hub loaded, central quick-close not verified (`quickClose=0`, payload unusable, or Follow's provider did not register) | Nobody polls Escape | The window's explicit X control |
| Hub loaded, verified `quickCloseContract=1` + `quickClose=1`, and Follow's provider registered | Hub | Hub's central quick-close, or the window's X control |

The middle state is deliberate. Once a Hub is loaded, Follow stops polling Escape even if Hub has
not yet advertised a usable quick-close binding — a live Hub IPC endpoint is treated as proof that
Hub exists even when a given describe payload is malformed or unavailable that frame. Follow will
not reinterpret a transient bad payload as "standalone" and start a competing Escape poll.

In every state, closing a Follow window is presentation only. Escape and the X control never cancel
Follow, Lead, or an Expedition; runtime travel continues and the status panel can be reopened from
Sim Actions. Cancelling requires the explicit Cancel action.

The retained UI and shared quick-close integration still require an installed-reference compile and live Lunaris hot-reload/zone-transition pass before release.
