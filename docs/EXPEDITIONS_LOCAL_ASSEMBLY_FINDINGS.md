# Sim-Led Expeditions — Local Assembly Findings

Inspection date: 2026-08-10
Source of truth: installed `<SteamLibrary>\steamapps\common\Erenshor\Erenshor_Data\Managed\Assembly-CSharp.dll`
Method: reflection member dump + `MethodBody.GetILAsByteArray()` disassembly.

Every symbol below was observed in the installed build. "Public assumption matched?" compares against
`EXPEDITIONS_DESIGN.md` / `EXPEDITIONS_GAME_HOOKS.md`.

---

## 1. Symbols confirmed as already used by Erenshor Follow

| Symbol | Installed signature | Public assumption matched? |
|---|---|---|
| `SimPlayer.AssignGuardSpot` | `public void AssignGuardSpot(Vector3 pos)` | yes |
| `SimPlayer.FreeFollow` | `public void FreeFollow()` | yes |
| `SimPlayer.GetGuardPos` | `public Vector3 GetGuardPos()` | yes |
| `SimPlayer.GuardSpot` | `public bool GuardSpot` (field) | yes |
| `SimPlayer.InGroup` | `public bool InGroup` (field) | yes |
| `SimPlayer.IsSimGroupInCombat` | `public bool IsSimGroupInCombat()` | yes |
| `NPC.HighPriorityNavUpdate` | `public void HighPriorityNavUpdate(Vector3 _newDest)` | yes |
| `NPC.CurrentAggroTarget` | `public Character CurrentAggroTarget` (field) | yes |
| `Zoneline.DestinationZone` | `public string DestinationZone` (field) | yes |
| `SimPlayerGrouping.IsSimInPlayerGroup` | `public bool IsSimInPlayerGroup(SimPlayer _sim)` | yes |
| `GameData.InCombat` | `public static bool InCombat` | yes |
| `PlayerControl.LandMovement` / `LeftClick` | present, patchable | yes |
| `Character.TargetMe` | present, patchable | yes |
| `TypeText.CheckCommands` | present, patchable | yes |

No existing Follow behavior depends on a symbol that has moved or changed shape. The current movement
chain (`AssignGuardSpot` -> `HighPriorityNavUpdate` -> `FollowController.Start`) is intact and was preserved.

---

## 2. New findings that changed the implementation

### 2.1 `Zoneline.DestinationZone` IS the Unity scene name — CONFIRMED

`Zoneline.FixedUpdate` (after the screen fade completes) ends with:

```text
SceneChange::ChangeScene(this.DestinationZone, this.LandingPosition, this.UseSunInNewZone, this.yRot)
```

and `SimPlayer.ZoneSim(Zoneline zl)` passes `zl.DestinationZone` straight into
`SimPlayerMngr.SimChangeScene(tracking, sceneName, landingSpot, false)`.

`DestinationZone` is therefore used verbatim as the scene identity by the game itself. Comparing it to
`SceneManager.GetActiveScene().name` for arrival is sound. This answers design Q3 / hooks Q9: **yes**.

### 2.2 `Zoneline.RemoveParty` dismisses the entire party at the boundary — NOT IN PUBLIC RESEARCH

`Zoneline.CallZoning()` begins:

```text
if (this.RemoveParty)
    for each GameData.GroupMembers[0..3]:
        SimPlayerGrouping.ForceDismissFromGroup(member.MyAvatar.GetComponent<Character>())
        UpdateSocialLog.LogAdd("Your party member has decided not to follow you to this place...", yellow)
```

An expedition through such a Zoneline is **impossible by construction**: the leader is dismissed from the
party at the exact moment of transition, so a verified arrival with an intact leader can never occur.

**Implementation consequence:** `ExpeditionDestinationResolver` excludes `RemoveParty` Zonelines from the
expedition destination set and reports the reason explicitly instead of failing later. This is a deliberate,
documented behavior difference from public 0.3.2, which did not check the flag.

### 2.3 The player's own collider is the only thing that starts the scene change — CONFIRMED

`Zoneline.OnTriggerEnter(Collider other)`:

- `other.transform.name == "Player"` -> `CallZoning()` -> fade -> `SceneChange.ChangeScene(...)`.
- Otherwise, if `other` has a `SimPlayer` (and `!RemoveParty`, `!InRaid`), the Sim is saved and
  `SimPlayer.ZoneSim(this)` is called for its independent group members.

This answers design Q5 / hooks Q-order: **only the player's crossing loads the new scene**. The leader
walking to the border is genuinely not arrival; the design's v1 arrival rule is correct.

Note the practical ordering: `CallZoning()` sets `GameData.Zoning = true` immediately, but the actual
`ChangeScene` only fires after the screen fade reaches alpha 255 in `FixedUpdate`. `GameData.Zoning` is
therefore a usable early "transition has begun" signal, several frames ahead of the scene swap.

### 2.4 Grouped Sims are destroyed and respawned across zoning; `SimPlayerTracking` is the stable identity — CONFIRMED

`SimPlayer.ZoneSim` ends with `Object.Destroy(this.gameObject)`.

`SimPlayerMngr.BringPlayerGroupToZone()` iterates `GameData.GroupMembers` (a `SimPlayerTracking[]`) and for
each non-null slot calls:

```text
AddActiveSim(member.SpawnMeInGame(PlayerControl.position + jitter, member))
member.MyAvatar.InGroup = true
member.isPuller = false
member.Caution   = false
member.CurScene  = SceneManager.GetActiveScene().name
```

So:

- the old `SimPlayer` component reference **is** invalidated by zoning (design assumption correct);
- `SimPlayerTracking` is a plain `System.Object`, not a `MonoBehaviour`, so it survives the scene load;
- `SimPlayerTracking.MyAvatar` is re-pointed at the freshly spawned `SimPlayer`;
- `SimPlayer.MySimTracking` and `SimPlayerTracking.simIndex` give a stable key.

This answers design Q1/Q2 and hooks Q1/Q5/Q6: **the leader can be safely reacquired after zoning via its
`SimPlayerTracking`**, followed by a full re-run of the usable/alive/party/COOP guards.

The implementation uses this only to verify the leader at arrival and to restore its state. Multi-leg
routing remains unimplemented (see §4).

### 2.5 `GameData.GroupMembers` is the authoritative roster — CONFIRMED

`public static SimPlayerTracking[] GameData.GroupMembers` (4 slots, null-padded), alongside
`GameData.PlayerGroup` (`List<SimPlayerTracking>`). The game's own zoning and group-order code reads
`GroupMembers`. `SimPlayerGrouping.IsSimInPlayerGroup(SimPlayer)` remains the cheapest live membership check
and is what Follow already uses; it was kept.

### 2.6 Native group orders are directly observable — NOT IN PUBLIC RESEARCH (only inferred)

`SimPlayerGrouping` exposes public parameterless methods that ARE the native party commands:

```text
public void GroupFollow()
public void GroupGuard()
public void RunAway()
public void GroupAttack() / GroupAttack(Character)
public void GroupPull(bool) / IndivPull()
public void GroupCaution() / GroupAggro()
```

`GroupGuard()` clears `freeRoamAzure` and assigns guard state per member; `GroupFollow()` clears each
member's `NPC.CurrentAggroTarget` and restores following.

This answers design Q12 / hooks Q22-Q25: these are safe, deterministic Harmony observation points, so
explicit player orders can pause or cancel an expedition instead of being fought. Implemented as postfix
observers only — Follow never calls them.

### 2.7 A game-owned global zone graph exists — CONTRADICTS the public research

```text
static class ZoneAtlas
    public static ZoneAtlasEntry[] Atlas
    public static ZoneAtlasEntry FindZoneInfo(string _zoneName)
    public static string FindNeighboringZone(string _curZone, int _lvl)

class ZoneAtlasEntry : BaseScriptableObject
    public string ZoneName
    public List<string> NeighboringZones
    public bool Dungeon
    public int LevelRangeLow / LevelRangeHigh
```

The design documents state that no global zone-connection graph was verified and that multi-zone routing
must wait for one. **A graph does exist.** It is recorded here for a future phase but is deliberately
**not** used in this implementation, because:

- the brief scopes v1 to live verified adjacent-zone destinations only;
- `NeighboringZones` is authored scriptable-object data, not proof that a walkable `Zoneline` to that
  neighbor is currently loaded and active — it is a candidate source, not a route validator;
- using it would require per-leg live revalidation and leader rebind that has not been live-tested.

Any future multi-leg work should treat `ZoneAtlas` as a *candidate generator* and keep the live `Zoneline`
scan as the *authority*.

### 2.8 Sim activity/task fields that an expedition temporarily overrides

`SimPlayer` carries independent-activity state: `MyTask` (`POIType`), `MyPOI` (`PointOfInterest`),
`TimeOnTask`, `SeekPlayer`, `pursuing`, `RunningAway`, `suspendGuard`, plus `GuardSpot`/`GuardPos`.

Follow already saves and restores `GuardSpot` + `GetGuardPos()` around a trip, which is the reversible pair
that `AssignGuardSpot`/`FreeFollow` act on. The task fields are driven by the Sim's own update loop and
recover on their own once the guard order is released, so this implementation continues to restore only the
guard pair. `SimPlayer.RunningAway` is additionally read as a native-retreat signal.

Answers design Q13 / hooks Q26-Q28.

### 2.9 `SimPlayer.FollowPlayer()` is private — CONFIRMS the public caveat

`private void FollowPlayer()` in the installed build. It is not used. `FreeFollow()` (public) remains the
correct way to release a guard order, which is what Follow already does.

Also noted: `public NPC SimPlayer.GetThisNPC()` exists and is a cleaner accessor than
`MyStats.Myself.MyNPC`. Not adopted, to avoid churn in a path that currently works.

---

## 3. Symbols checked and deliberately not used

| Symbol | Why not used |
|---|---|
| `SceneChange.ChangeScene` / `ChangeSceneSafe` | Would fake progress. v1 must observe a real transition. |
| `SimPlayer.ZoneSim(Zoneline)` | Destroys the Sim and force-zones it; not a travel primitive. |
| `SimPlayerTracking.TravelToZone(string)` | Teleport-class operation, out of scope. |
| `SimPlayerMngr.BringSimToPlayer` / `SpawnMeInPlayerZone` | Teleport fallback, explicitly excluded. |
| `SimPlayerGrouping.ForceDismissFromGroup` | Expeditions never change group membership. |
| `NPC.UpdateNav` (private) / `NeedsNavUpdate` | `HighPriorityNavUpdate` already works; no reason to widen the patch surface. |
| `ZoneAtlas.FindNeighboringZone` | Multi-zone routing deferred (§2.7). |

---

## 4. What remains unverified and therefore unimplemented

1. **Multi-leg routing.** Leader reacquisition across zoning is now understood (§2.4) and a zone graph
   exists (§2.7), but neither has been live-tested. v1 ends at the first verified arrival.
2. **Post-zone settle timing.** `BringPlayerGroupToZone` is called by the game during zone setup; the exact
   frame at which `MyAvatar` becomes non-null after `sceneLoaded` was not measured. The coordinator uses a
   bounded settle window with an explicit timeout instead of assuming a frame count.
3. **Death / revival interaction with `IsSimGroupInCombat`.** Not statically determinable; requires live
   testing (see the test matrix in the summary).
4. **Whether special portals/dungeons use ordinary `Zoneline` instances.** `ZoneAtlasEntry.Dungeon` exists,
   but the correspondence to Zoneline objects was not confirmed.
