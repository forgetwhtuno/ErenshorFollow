# Erenshor Follow 0.5.0

Erenshor Follow adds player movement assistance, Sim-led travel, and expedition coordination around existing Erenshor zone transitions. It is separate from Deep Sims and coexists with its embedded follow movement patch through reflection-based compatibility, not a loader-level dependency.

## Status: native Lunaris migration candidate

This version has been migrated off BepInEx 5 onto native Lunaris. This is a
loader/config/logging/lifecycle migration only — no follow/lead/expedition/route logic, command
grammar, or NavMesh/zoneline handling changed; every Harmony patch target has been re-verified
against the currently installed `Assembly-CSharp.dll`, and the full existing deterministic test
suite (`RUN_TESTS.ps1`) still passes unchanged. This pass also fixed a pre-existing hot-reload
event leak in `CoopCompatibility`/`ExpeditionIntegrationBridge` (an unsubscribed
`AppDomain.AssemblyLoad` handler). **Live in-game verification under Lunaris — including movement
around real zone transitions and repeated unload/reload while Follow or an Expedition is active —
has not yet been done.** A legacy BepInEx release remains available in this repository's Git
history for anyone still on BepInEx.

## Commands

```text
/efollow <SimName>       follow a nearby local Sim
/efollow off             stop following
/dsfollow <SimName>      compatibility alias
/dsfollow off            stop following
/elead <SimName>         open/use Sim-led travel controls
/expedition status       expedition state and route
/expedition pause        pause the current expedition
/expedition resume       resume a paused expedition
/expedition cancel       cancel the expedition
/expedition return       request a verified return leg
```

Natural group phrasing such as asking a Sim to lead to a named destination is parsed only when the leader and route resolve unambiguously. Route planning uses the local zone graph, known crossings, scene transitions, and verified arrival events; it does not invent a route.

## UI

The mod provides a clickable Sim action menu and a travel status overlay. Clicking a local Sim can expose Follow/Lead actions and Stop. Overlay positions are persisted through `UI/OverlayOffsetX`, `UI/OverlayOffsetY`, and `UI/OverlayPositionX/Y`. Verbose click/route diagnostics are controlled by `Diagnostics/Verbose`.

## Safety and scope

- Movement is owned by this mod's deterministic follow/lead controller, not by an LLM.
- Locality, target identity, scene, NavMesh/crossing, zoning, and participant validity are checked continuously.
- Follow, leader travel, and expedition state stop on invalid targets, zone changes, errors, or cancellation.
- The mod does not choose combat actions, loot, spells, targets, quests, equipment, or party composition.
- COOP/remote-human compatibility is treated conservatively; a remote human is not accepted as a local Sim target.
- Removing the plugin leaves Erenshor's normal movement and Sim behavior intact.

## Expedition behavior

An expedition is a bounded multi-leg session: it records origin/current zone, requested destination, route progress, pause/resume/cancel state, verified arrival, and optional Campmaster handoff. The status overlay reports the current leg and exposes safe arrival actions. Failed or ambiguous route resolution is reported rather than silently teleporting.

## Installation

This is a **native Lunaris plugin** — BepInEx is no longer required for this version. Requires
Lunaris installed in your Erenshor install. The compiled DLL is placed directly in
`<Erenshor>\plugins\ErenshorFollow.dll`; Lunaris manages enable/disable.

## Build

`BUILD_AND_INSTALL.ps1` builds the standalone native Lunaris plugin against installed Erenshor
and Lunaris assemblies. Install Deep Sims separately if social dialogue integration is desired.

## Credits and Inspiration

### Compatibility / related projects

- **[Erenshor COOP](https://github.com/MizukiBelhi/ErenshorCoop) by MizukiBelhi** — important technical reference and compatibility target for remote-human/networked-Sim detection. I have also tested against a locally updated copy for recent Erenshor and Deep Sims compatibility.

## Development note

This project has been developed heavily with AI-assisted coding tools. The goal has been to build features I wanted to use in Erenshor, with development guided through design, testing, playtesting, audits, and iteration against the game. Bug reports, code review, corrections, and contributions from experienced Erenshor modders are welcome.

This is an unofficial, community-made mod for Erenshor and is not affiliated with or endorsed by the game's developer.
