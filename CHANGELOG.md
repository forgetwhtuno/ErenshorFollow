# Changelog

## 0.6.22 - world-scale intermediate crossing probes

- Separates authoritative local BoxCollider OBB construction from world-metre intermediate-height classification, restoring lower-mid discovery for heavily scaled tall triggers such as Hidden.
- Adds exact live Hidden geometry coverage for localSize=(1,1,1), rotationY=40.27, and lossyScale=(80,47.11,10), plus bounded `/elead diag` stage/geometry fields.
- Keeps the 30-primary/8-fallback budget, centerline ranking, route-facing probes, native zoning, and diagnostic-only egress behavior unchanged.

## 0.6.21 - Hidden discovery budget recovery and centered crossing selection

- Restores the historical 30-seed primary crossing discovery budget and reserves all eight
  zero-sample `midRing0..midRing7` fallback slots used by tall/large Hidden-style triggers.
- Removes the duplicate primary `centerFace` seed; centered crossing behavior remains a ranking
  preference through the oriented face centerline and bounded route-facing probes.
- Adds primary/fallback stage diagnostics while keeping authored egress POIs diagnostic-only and
  native Zoneline traversal authoritative.

## 0.6.19 - live-zoneline world-route reconciliation

- Reconciles current-scene eligible native Zonelines into the runtime route graph, so a live direct exit remains executable when the authored atlas omits that edge.
- Keeps EgressLocations and zoneline POIs optional authored navigation hints; they do not gate world adjacency.
- Adds concise route-reconciliation diagnostics while preserving native crossing, DoGuard ownership, and order-proof behavior.

## 0.6.18 - large-trigger route-facing sampling repair

- Live 0.6.17 proved the second-stage entrance probe could not help on a large trigger: it produced
  only three points, all at the collider's centre height and one inward depth, and recorded nothing
  about them. The bounded route-facing probe set is now derived from the live BoxCollider - same face
  the approach-quality reference uses, two inward depths, bounded tangent steps, and the
  lower-intermediate vertical level when the trigger is tall enough for it to differ from centre.
- The approach-quality reference and the entrance probes now share one face-selection helper, so the
  probes always cover the face quality is measured against.
- Adds bounded event-boundary forensics for every second-stage probe: label, unsampled world position,
  sample radius, local normalized offset, world Y, distance to the collider volume, quality reference,
  NavMesh.SamplePosition outcome, sampled hit, and CalculatePath status - plus the authoritative
  BoxCollider centre/size/lossyScale/rotation, oriented world half axes, and the route start in the
  collider's own local space. No per-frame diagnostics.
- The reported generated-seed count now includes the second-stage probes.
- Ranking policy, acceptance distances, sample radii, small/tall/rotated trigger seeding, and the
  0.6.16 bilateral sidestep behaviour are unchanged.

## 0.6.17 - large-zoneline entrance quality repair

- Preserves the 0.6.16 bilateral sidestep selection and recovery behavior unchanged.
- A lone accepted quality-poor edge on a large trigger now gets a bounded route-facing interior entrance probe before final ranking; existing NavMesh, path, and acceptance gates remain required.
- `/expedition diag` exposes the selected current-leg seed, approach, quality reference, quality score, route length, and reason with verbose diagnostics off.

## 0.6.16 - live navigation quality repair

- Evaluates both bounded lateral obstacle-recovery candidates before choosing the side with the
  stronger continuation evidence; Unity X/Z handedness labels now match the actual left/right sides.
- Adds bounded large-Zoneline approach-quality ranking so a natural route-facing/interior approach
  outranks an opposite extreme trigger edge when both are otherwise safe, while preserving the edge as
  a fallback when it is the only viable route.
- Adds bounded sidestep and crossing-quality diagnostics for sampled/path/continuation scores, seed
  labels, raw/candidate geometry, and ranking reason.
- No change to exact Sim identity, native zoning, expedition authority, or state-machine ownership.

## 0.6.15 - final release candidate identity

- Carries the live-proven 0.6.14 navigation behavior forward unchanged.
- Records the approved shared fallback collapse integration under a unique
  final candidate version.
- No crossing geometry, sampling, routing, movement, catch-up, or native
  traversal behavior changes are introduced in this bookkeeping pass.

## 0.6.14 - zero-sample large-trigger interior-ring fallback

- Live 0.6.13 `/elead diag Hidden` proved the player start is on NavMesh and the correct active,
  non-party-removing Hidden Zoneline is resolved, but every primary approach seed still returns
  `NavMesh.SamplePosition=false`. The authoritative collider is a rotated/scaled unit BoxCollider
  (`localSize=(1,1,1)`, large non-uniform lossy scale), so the earlier lower-mid axis cross remained
  too sparse inside the huge trigger volume.
- Adds an eight-point world-metre inner ring at the existing lower-intermediate height **only after**
  all primary seeds produce zero NavMesh samples. Working crossings pay no extra sampling cost; small
  triggers skip the ring entirely; acceptance/path/native traversal rules remain unchanged.
- `/elead diag <zone>` now follows `/elead` into the ZoneAtlas route planner when the requested zone
  is not directly adjacent, reports the candidate itinerary/first hop, then diagnoses that live first
  hop. This makes requests such as Azure useful instead of stopping at `Canonical destination: <none>`.
- No destination names or coordinates are special-cased in production behavior.

## 0.6.13 - tall-zoneline OBB + intermediate vertical seed repair

- Live 0.6.12 narrowed `Duskenlight -> Hidden` from a filtering ambiguity to a true sampling-coverage failure: the corrected volume filter retained the large trigger candidates, but every retained seed still reported `sampled=False`. The same diagnostic exposed that the supposed oriented face points were not actually on the BoxCollider oriented faces.
- Fixes the OBB reconstruction in `AddOrientedCrossingSeeds`: `Collider.bounds` is a world-axis-aligned AABB, so inverse-transforming its extents cannot recover BoxCollider local half-extents. Runtime now uses `BoxCollider.center` and `BoxCollider.size` directly and transforms those local points, including off-centre, rotated, and scaled triggers. Generic non-box colliders keep a bounded best-effort fallback.
- Adds bounded lower-intermediate coverage for genuinely tall triggers only. The live Hidden candidates clustered around centre Y=61.40, floor Y=37.84, and top Y=84.96, while an earlier live-successful approach was around Y=50.06. The repair adds a five-point interior cross plus a three-point band on the OBB face most strongly approached by the current route start (face centre and +/- quarter-width tangent offsets), all at half-way down the lower half. This covers the missing combination of horizontal surface position + intermediate height without increasing the 4m sample radius or creating a 3D grid. The old log exposed only world AABB bounds, not authoritative BoxCollider local size/rotation, so the patch deliberately does not claim an exact offline reconstruction of that old approach.
- Keeps traversal safety unchanged: every seed still requires `NavMesh.SamplePosition`, a real path, the existing 8m verified-crossing acceptance, and native zoneline traversal. No warp, teleport, or forced scene load is added.
- Reduces diagnostic pressure without removing forensic tools: `ErenshorFollowPlugin.LogDebug` now centrally fails closed unless `Diagnostics/Verbose` is enabled (some older `Verbose(...)` helpers relied on their name but did not consistently gate the logger). Ordinary route planning also avoids per-seed diagnostic objects unless verbose diagnostics are enabled; explicit route diagnostics still request them. Repeated giant zero-candidate detail lines are rate-limited to once per scene/destination every 10 seconds. When verbose detail is requested, BoxCollider diagnostics now include local center/size plus transform rotation/scale so another failure can be reconstructed correctly rather than inferred from world AABB bounds.

## 0.6.12 - large-trigger seed regression repair (Duskenlight -> Hidden)

- Fixes the one remaining live traversal failure: `Duskenlight -> Hidden` reported
  `generatedSeeds=16 samples=0 accepted=0` and "No NavMesh sample succeeded near any of the 16
  generated seed(s)", even though the same crossing had been traversed by an earlier candidate via an
  approach around `(232.14, 50.06, 116.71)`. That prior success ruled out "this scene has no usable
  NavMesh" and made it a candidate-generation/filtering regression, which is what it turned out to be.
- Root cause: the 0.6.10 seed-proximity filter measured "distance from the crossing" as the horizontal
  distance to the crossing's RAW TRANSFORM POINT, then compared it against
  `NativeProbeApproachNearCrossing` (8m) + `sampleRadius` (4m) = 12m. Acceptance, however, has always
  measured distance to the crossing's collider VOLUME (`DistanceToCrossing` -> `Collider.ClosestPoint`).
  Those two metrics agree for a small trigger but diverge badly for a large one, and the live Hidden
  trigger is a `BoxCollider` with `center=(223.37, 61.40, 117.58)` and `size=(67.50, 47.11, 59.35)`:
  - its four AABB floor corners sit `sqrt(33.75^2 + 29.68^2) ~= 44.9m` from the raw point,
  - its oriented +/-X face centres sit 33.75m out and its +/-Z face centres 29.68m out,
  - yet every one of those points lies ON the verified trigger volume, i.e. at acceptance distance 0.
  All of them were therefore discarded for being "too far from centre". What survived was only the
  vertical column of seeds through the collider centre (floor-face centre, bottom oriented face, three
  vertical probes - all at horizontal distance 0) plus the raw/cardinal seeds within a few metres of
  it. Sixteen seeds collapsed onto one narrow column inside a 67m x 59m volume, and the previously
  proven approach ~8.8m away laterally was outside every surviving seed's radius. The useful seed
  class was generated and then filtered out - it was never missing, and NavMesh was never absent.
- Repair: seed relevance is now measured against the verified collider VOLUME, the same metric
  acceptance already uses, via the new `CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume`. A
  seed INSIDE the trigger volume is never discarded for being far from its centre; a seed outside is
  judged by its distance to the volume's surface. Because the filter and acceptance now share one
  metric by construction, the original 0.6.10 argument - "a seed beyond acceptance + radius can only
  ever yield a rejected endpoint" - is sound again, so a genuinely remote AABB artefact beside a small
  or rotated trigger stays filtered exactly as intended. This generalizes to any large/tall zoneline
  BoxCollider; nothing is special-cased by zone name or coordinate.
- Added per-seed route-build diagnostics (`SeedDiagnostic`), emitted only on the zero-sample failure
  path and bounded by the seed budget - never per frame. Each record carries the seed index, label,
  world position, sample radius, `dRaw` (distance to the raw centre), `dVol` (distance to the collider
  volume), `inside`, kept/filtered plus filter reason, and the `NavMesh.SamplePosition` result with its
  hit position. This is what distinguishes the three possible causes of a zero-sample crossing -
  seed class never generated, generated then filtered, or survived and genuinely failed to sample -
  rather than inferring it from a bare count. `filteredSeeds=` is now also reported alongside
  `generatedSeeds=` on every crossing line.
- `MaxSeedsPerCrossing` raised 18 -> 26 so a large trigger's legitimately-retained oriented face and
  floor-corner seeds cannot starve the proven raw/cardinal seeds behind them. The budget itself is
  retained, and the collider set is now resolved once per route build and threaded through the seed
  path so volume-aware filtering does not turn one `GetComponentsInChildren` scan into one per seed.
- Deliberately NOT changed: sample radii are not widened (`FloorSeedRadius` is still 4m,
  `MaxApproachesPerCrossing` still 6); the 8m verified-approach acceptance, the Complete /
  PartialNearCrossing rules, the native-traversal requirement, and the no-teleport/no-warp/no-scene-load
  rules are all untouched. A 40m endpoint is still rejected downstream. The 0.6.11 live-proven
  systems - native Brake crossing traversal, Brake -> Azure, Azure arrival, Hidden -> Duskenlight,
  movement-arrival -> traversal handoff, player CatchUp, StrongCatchUp, BlockedByObstacle -> Sidestep,
  and leader reacquisition - are unmodified.

## 0.6.11 - UI workspace normalization

- UI/layout only; Follow navigation, expedition, and gameplay behavior are unchanged.
- The standalone FOLLOW fallback panel's status box is now 56px instead of a fixed 88px shared by
  every module regardless of content: Follow's guide+status text is normally 2-3 short lines, so the
  larger box left a large empty area beneath it. The reduction is opt-in per module via the new
  `StandaloneFallbackUi.ConfigureWorkspaceDefaults(...)`, so unrelated fallback-panel users
  (Campmaster, Deep Sims, Nemesis) are byte-for-behavior unchanged.
- New default panel position: opens into a shared right-side workspace below the launcher column
  (instead of dead screen center) when no position has ever been saved. An existing saved position
  is preserved exactly; this only changes the default and has no reset command to wire (Follow's
  fallback panel has no resize/reset control).
- Follow's launcher column slot changed from 1 to 2 (order is now Journal=0, Duel=1, Follow=2) to
  match the intended top-to-bottom workspace layout.
- Fixed the shared launcher-column right margin, which previously resolved flush to the screen edge
  with zero margin at any realistic resolution (see Journal 0.1.11 for the root cause).
- The standalone launcher now shows a small structural open/active indicator (a top-edge accent bar
  shared with Journal/Duel) instead of relying on panel visibility alone.

## 0.6.9 - shared standalone-launcher visual/placement pass

- The standalone FOLLOW launcher (`Erenshor-Mod-Suite/shared/ErenshorSuite.UI/StandaloneFallbackUi.cs`,
  used whenever Suite Hub is absent/unhealthy) now matches Journal's canonical launcher chrome exactly:
  154x32 launcher, 20px grip, 1px outline frame, and a centered three-dot grip accent - previously a
  plain colored rect with a slightly different ad-hoc dot layout.
- New default standalone position: a vertical right-side column beneath the native minimap area
  (Journal/Follow/Duel occupy fixed, non-overlapping slots), replacing the old lower-left default. No
  stable minimap RectTransform exists in the installed assembly to derive an exact lower edge from, so
  the column uses a resolution-independent top-right anchor with a conservative fixed inset.
- Fixed a pre-existing defect: dragging the launcher never actually persisted its position (the shared
  save path only recognized the larger fallback panel). The launcher now saves/restores its own
  position the same way the panel already does, so an existing install with no real saved launcher
  position adopts the new right-side default automatically; any future saved position is preserved.
- Added `src/StandaloneLauncherColumnPolicy.cs`, a small per-module copied policy (same convention as
  `StandaloneLauncherVisual.cs`) giving Follow its column slot (1 of 3, after Journal).

## 0.6.8 - follow-distance / catch-up tuning

- Live feedback on 0.6.7: local-obstacle recovery (`pathStatus=Sidestep`/`repathReason=BlockedByObstacle`
  followed by continued `PathComplete` following) confirmed working and left completely untouched. During
  ordinary travel, though, the player visibly fell farther behind the leader than felt good.
- Audited every distance/speed/timing constant in `FollowController`/`FollowLocalObstaclePolicy`/
  `FollowStuckRecoveryPolicy` before changing anything (see the session's numbered audit). Key finding:
  there was no catch-up mechanism at all, and `StopDistance`/`ResumeDistance` (3.0m/4.5m around the
  trailing nav-target, i.e. ~5m/~6.5m from the leader) were themselves already near the edge of "normal"
  formation, so the player settled and resumed movement farther out than felt tight.
- Confirmed the speed-parity hypothesis by inspecting `Stats.actualRunSpeed` in the installed game
  assembly: the leader's native `NavMeshAgent.speed` and the player's `CharacterController.SimpleMove`
  speed are both set directly from that same stat with no multiplier anywhere. With equal native max
  speed, any ground lost to a corner, side-step, repath, or brief collision cannot mathematically be
  regained on a straightaway without a temporary boost.
- Separated three previously-conflated concepts, as requested: (1) the trailing nav-target hysteresis
  (`StopDistance`/`ResumeDistance`, now 1.2m/2.2m - tightened for closer ordinary formation, settling the
  player roughly 0.8-3.2m behind the leader); (2) a new formation/catch-up classification
  (`FollowLocalObstaclePolicy.ClassifyFormation`) measured against the leader's REAL position (not the
  trailing target): Close (0-2m) / Normal (~2.5-5m, `DesiredFollowDistance=3.5m`) / CatchUp (6m+,
  1.15x native speed) / StrongCatchUp (9m+, 1.30x native speed), with hysteresis so catch-up disengages
  only once solidly back under 4.5m, never flickering at one boundary; (3) the existing bounded
  `FollowStuckRecoveryPolicy` timeout/attempt-count, completely unchanged - it remains the only thing that
  can fail an expedition, so a temporary 8-10m terrain gap now correctly engages strong catch-up instead
  of drifting toward that timeout, while a genuinely persistent no-route condition still fails exactly as
  before.
- The catch-up multiplier is applied only to a local speed variable used for that frame's
  `CharacterController.SimpleMove` call - never written back to `MyStats`/`RunSpeed`, so it can never
  leave a lingering speed change once catch-up disengages. No teleport, no warp, no collision bypass, no
  relaxed path acceptance; obstacle recovery (`NavMesh.SamplePosition`/`CalculatePath`/corner-following/
  `BlockedByObstacle`-triggered side-step) is unchanged and covered by the same guards as 0.6.7. The
  0.6.7 trailing-target design (aim behind the leader's heading, never its exact transform) is preserved
  unchanged - only the nav-target stop/resume distances around it were tightened.
- Extended the existing bounded `player_follow_path` diagnostic (still only logged on a real repath/state
  change, never per frame) with `desiredDistance=`, `catchupBand=`, `leashDistance=`, `normalSpeed=`,
  `appliedSpeed=`, `catchupActive=`.
- Added 5 new deterministic tests for the formation/catch-up bands (close/normal/moderate/strong/
  hysteresis-disengage/temporary-large-gap) and 2 new source guards proving the catch-up boost is never
  written back to a persistent stat. Crossing routing/geometry files were not touched by this pass; a
  separately-observed native crossing trigger-traversal failure from the same session is NOT addressed
  here and should be investigated on its own.

## 0.6.7 - player-follow local-obstacle navigation repair

- Investigated the live report: on the leg toward Azure, the leader had a verified PathComplete route
  (13 corners) and native movement toward the crossing was working, but the PLAYER visibly got stuck
  running into a tree while following, and Follow eventually reported "player follow could not keep a
  route to the leader" and failed the expedition. Confirmed this is entirely separate from the 0.6.6
  crossing-geometry repair: the leader's own route/crossing proof was never in question.
- Traced the exact mechanism: `FollowController.TryDrive` (the code that moves the local PLAYER while
  following an expedition leader) already used `NavMesh.SamplePosition` + `NavMesh.CalculatePath` +
  corner-following + `CharacterController.SimpleMove`, not raw direct-to-leader steering. Inspected the
  installed game assembly to confirm there was no richer native alternative available: `PlayerControl`
  has only a `CharacterController` (`myControl`), no `NavMeshAgent` - unlike `SimPlayer` (`MyNav`), which
  is why the leader Sim can glide around obstacles via Unity's native agent steering while the player,
  driven by this mod's own layer, could not.
- Root cause of the tree-running failure: the existing bounded stuck-recovery (`FollowStuckRecoveryPolicy`,
  unchanged) already retried up to 3 times over 9 seconds before failing - but every retry recomputed
  `NavMesh.CalculatePath` from the player's current (physically blocked) position to the leader's exact
  transform, which regenerated the identical corner sequence into the identical obstacle every time.
  There was no local, lateral recovery step, and the player was always aimed at the leader's exact spot
  rather than a reachable trailing point, so both actors could need the same tight gap simultaneously.
- Added `FollowLocalObstaclePolicy` (pure, Unity-free, deterministically tested): classifies a stall as
  `MovingTargetRepathPending`, `BlockedByObstacle`, `LeaderTooFar`, or `NoRoute` before choosing HOW the
  next bounded repath attempt behaves, and computes a trailing point behind the leader's recent heading
  plus two lateral side-step candidate points. This governs only the STRATEGY of each already-bounded
  repath attempt; `FollowStuckRecoveryPolicy`'s strike-count/timeout bound - the thing that decides when
  following genuinely gives up - is completely unchanged, so a persistent no-route condition still fails
  cleanly and a temporary miss still never fails immediately.
- `FollowController.TryDrive` now: (1) aims at a point ~2m behind the leader's recent movement heading
  instead of its exact transform - the player does not need to stand on the leader's exact spot; (2) when
  classified as physically blocked (actively steering, near-zero actual displacement over a repath
  cycle), tries two lateral NavMesh-sampled/CalculatePath-verified probe points before falling back to
  the ordinary repath, so a repath from a stuck spot can actually resolve instead of repeating; (3) keeps
  the ordinary ~0.35s-cadence bounded repath, corner-rounding, partial-path-retry, and ordinary
  NavMesh/CalculatePath acceptance exactly as before for every other case. No teleporting, no phasing
  through geometry, no speed increase, no relaxed path acceptance - every candidate point still has to
  pass the same NavMesh sample + CalculatePath pipeline as any other approach point in this mod.
- Added a bounded `player_follow_path` diagnostic (leaderDistance/sampledTarget/pathStatus/corners/
  currentCorner/repathReason/stuckSeconds/movementDelta), logged only when the dedupe signature actually
  changes (a real repath/state change/failure) - never per frame.
- Crossing routing/candidate acceptance/proof, zoneline traversal, and post-zone reacquisition were not
  touched. Added source guards confirming the proven crossing-geometry markers (`CrossingSeedGeometryPolicy`,
  `ExpeditionCrossingPolicy`) and player-follow corner-following/no-teleport invariants remain intact.

## 0.6.6 - Duskenlight Cove tall-trigger crossing seeding repair

- Investigated the live report "from Duskenlight Cove, `Route: built 0 accepted approach candidate(s)
  across 1 crossing(s) for Hidden` repeats and no expedition can start," while `Brake` succeeded from
  the same session/zone moments earlier (`selected route candidate ... => Complete ... corners=19`).
  Confirmed from the live `lunaris.log` this was real, reproducible, and destination-specific - not a
  general routing regression, since Brake proved the planner, NavMesh proof, and native movement
  ownership all worked correctly in the same zone.
- First pass added diagnostics only (`RouteCandidatePolicy.DescribeCandidate`, an enriched
  `LocalZoneRoutePlanner.DescribeReadiness`, and one bounded failure-branch log line in
  `LeaderController.RebuildZoneOptions`) because `LocalZoneRoutePlanner.Build` already computed a full
  per-crossing inspection that the route-start call site was discarding before logging only a terse
  summary. That diagnostic then produced the field data needed to find the actual defect: destination
  `Hidden`'s crossing had exactly one live trigger collider, was active, was not party-removing, and its
  transform sat at `rawPos=(223.37, 61.40, 117.58)` - yet 0 of the seeds generated near it ever produced
  a successful `NavMesh.SamplePosition`.
- Root cause: every existing seed for a crossing (its transform position, its collider's `bounds.center`,
  and the collider point closest to the party) clusters around the collider's vertical MIDPOINT. A
  zoneline whose trigger is a tall vertical volume - an archway, doorway, or cliff-face trigger spanning
  from real ground level up to well above head height - can have every one of those seeds sit many meters
  above the walkable floor. `NavMesh.SamplePosition` performs a true 3D sphere search, so a seed that far
  above the floor fails to find NavMesh within any reasonably bounded radius, even though the crossing
  itself is perfectly live and correctly identified. This requires no `Hidden`-specific coordinate: any
  crossing with a tall trigger volume would exhibit the identical zero-sample failure.
- Fix: added `CrossingSeedGeometryPolicy` (pure, Unity-free, deterministically tested) and wired it into
  `LocalZoneRoutePlanner.SampleApproaches`. When a collider is tall enough that its vertical extent
  exceeds the existing seed radius, the planner now also seeds the horizontal center and four corners of
  the collider bounds' FLOOR face (`boundsCenter.Y - boundsExtents.Y`) instead of relying only on its
  midpoint. `MaxSeedsPerCrossing` raised 14 -> 18 to make room without starving the pre-existing seeds;
  still a fixed, small, one-time cost per route-build call, never a per-frame or unbounded scan.
- This changes only WHERE Follow looks for an approach. Every geometry-derived seed still has to pass
  the exact same pipeline as before: `NavMesh.SamplePosition` success, `NavMesh.CalculatePath`,
  `PathComplete`/`PathPartial` evidence, and the unchanged `RouteCandidatePolicy` approach/endpoint
  distance acceptance policy. It cannot weaken what counts as a valid route - it can only find a
  genuinely valid one that the old center-only seeding missed. Brake's 5-candidate/Complete/19-corner
  result and Windwashed's 1-candidate/Complete/28-corner result are both unaffected: neither crossing's
  collider is tall enough to trigger floor seeding (`CrossingSeedGeometryPolicy.
  FloorSeedsMeaningfullyDifferFromCenter` is false for them), so their seed sets are identical to before.
- `LocalZoneRoutePlanner.DescribeReadiness`'s zero-sample branch now also reports `generatedSeeds=N` (how
  many seed points were actually tried, not just how many succeeded) and the full per-collider
  type/enabled/trigger/bounds detail, so a *future* crossing that still produces zero samples shows why
  without needing another diagnostics-only round trip.
- Added 5 deterministic tests for `CrossingSeedGeometryPolicy`, including a fixture that reproduces the
  live failure's shape (tall collider, elevated transform Y, ground far below) and proves transform-only
  seeding misses it while the floor seed reaches it - without hardcoding the real Hidden coordinate.
- The separately reported `LeaderUnavailable: the leader is no longer available` outcome on a later,
  successful Windwashed run (28-corner Complete route, encountered normal enemies deep into the route)
  is NOT addressed here. No evidence was found that it is a Follow defect rather than the leader
  legitimately dying/despawning/becoming unavailable during combat; left untouched pending independent
  evidence.

## 0.6.5 - Expedition setup/status UX and feedback repair

- Fixed oversized Expedition destination rows: the destination list's `VerticalLayoutGroup` had `childControlHeight = false`, which left each row's actual rect at Unity's default 100px size regardless of the intended ~30px row metric (the `LayoutElement` only affected row *positioning* under that setting, not the row's own size). Rows are now explicitly sized and the layout group is set to `childControlHeight = true`, matching the already-correct Sim Actions convention. Destination rows are ~30px tall with 3px spacing, and the selected destination now gets a distinct fill/text color instead of only a bullet glyph.
- Fixed the root cause of "Start Expedition gives no visible result": a rejected Start's reason was being overwritten by the generic hint text on the very next frame, because `Tick()` calls the leader-admission refresh unconditionally every frame and that refresh always fell through to the generic hint. A Start rejection is now sticky - it stays visible until the player picks a different destination, retries Start, or closes the window - and the setup window never silently closes on rejection.
- Start now gives immediate synchronous feedback ("Starting expedition to X...") and disables the Start button while the (synchronous) attempt resolves, then shows either the persistent Traveling status or a concrete, non-generic rejection reason. Added an explicit `ExpeditionStartOutcome` (Accepted/AlreadyActive/InvalidLeader/NoRoute/NotReady/Rejected) result contract, wired through `LeaderController.StartExpeditionLeg` and `ExpeditionCoordinator`, so the UI reacts to a typed result instead of inferring success from the click. Reused the existing, previously-unwired `ExpeditionWorkflowPolicy.EvaluateStart` identity/route admission policy rather than duplicating its logic.
- Fixed a cancelled or failed expedition disappearing instantly instead of showing why: `Active` is false for a terminal session, and the status overlay only special-cased `Arrived` to stay visible through the existing 4-second terminal window. Cancelled/Failed now get the same brief visible window with the actual reason, using the same `FailureDetail` text already used in chat.
- Added `RouteReadinessPending` so the status text can tell "reacquiring the leader after a zone change" apart from "leader reacquired, now waiting on the bounded post-zone NavMesh/route probe" - previously both showed the same "Reacquiring..." text even after the leader was already back, for as long as several seconds of probing.
- No changes to route discovery, NavMesh proof, crossing validation, or movement ownership; this is presentation/feedback only.

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
