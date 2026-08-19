$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

function Read-RepoFile([string]$relative) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing release file: $relative" }
    return [IO.File]::ReadAllText($path)
}
function Assert-Contains([string]$text, [string]$needle, [string]$name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "FAILED: $name" }
    Write-Host "PASS: $name"
}
function Assert-NotContains([string]$text, [string]$needle, [string]$name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) { throw "FAILED: $name" }
    Write-Host "PASS: $name"
}

$simActions = Read-RepoFile "src\SimActionMenu.cs"
$setup = Read-RepoFile "src\ExpeditionSetupWindow.cs"
$status = Read-RepoFile "src\TravelStatusOverlay.cs"
$coordinator = Read-RepoFile "src\ExpeditionCoordinator.cs"
$plugin = Read-RepoFile "src\ErenshorFollowPlugin.cs"
$aura = Read-RepoFile "src\FollowSuiteAuraProvider.cs"
$control = Read-RepoFile "src\FollowControlApi.cs"
$route = Read-RepoFile "src\ZoneAtlasRoutePlanner.cs"
$routePolicy = Read-RepoFile "src\ZoneRouteGraphPolicy.cs"
$leader = Read-RepoFile "src\LeaderController.cs"
$follow = Read-RepoFile "src\FollowController.cs"
$layout = Read-RepoFile "src\SimActionMenuLayoutPolicy.cs"
$planner = Read-RepoFile "src\LocalZoneRoutePlanner.cs"
$crossingPolicy = Read-RepoFile "src\ExpeditionCrossingPolicy.cs"
$crossingObserver = Read-RepoFile "src\ZonelineTriggerObserver.cs"
$doGuardPatch = Read-RepoFile "src\ExpeditionDoGuardPatch.cs"
$ownershipPolicy = Read-RepoFile "src\ExpeditionMovementOwnershipPolicy.cs"
$dragGuard = Read-RepoFile "src\FollowUiDragGuard.cs"
$cameraUiPatch = Read-RepoFile "src\FollowCameraUiPatch.cs"
$cameraCompat = Read-RepoFile "src\FollowCameraCompatibility.cs"
$postZoneReadiness = Read-RepoFile "src\PostZoneRouteReadinessPolicy.cs"
$columnPolicy = Read-RepoFile "src\StandaloneLauncherColumnPolicy.cs"
$fallbackUi = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent (Split-Path -Parent $repoRoot)) "Erenshor-Mod-Suite\shared\ErenshorSuite.UI\StandaloneFallbackUi.cs"))

Assert-Contains $simActions 'AddAction("Create Expedition"' "Sim Actions exposes Create Expedition"
Assert-Contains $simActions 'ExpeditionSetupWindow.Open' "Create Expedition opens dedicated setup"
Assert-Contains $simActions 'GetProperty("IsAvailable"' "optional Duel integration checks live provider availability"
Assert-Contains $simActions 'apiVersion == 1 && isAvailable' "optional Duel integration requires supported public API version"
Assert-Contains $simActions 'GetMethod("TryChallenge"' "optional Duel integration invokes public TryChallenge"
Assert-NotContains $simActions 'GetMenuDestinations()' "Sim Actions no longer renders adjacent-only destination submenu"
Assert-Contains $setup 'ListReachableRoutes' "setup enumerates multi-zone reachable atlas destinations"
Assert-Contains $setup 'TryStartRouteExact' "Start revalidates exact tracking and current route"

# --- 0.6.5 UX/feedback repair: destination row sizing (test #1) ------------------------------------
Assert-Contains $setup 'layout.childControlHeight = true;' "Expedition destination list layout group owns row heights instead of default RectTransform heights"
Assert-Contains $setup 'e.flexibleHeight = 0f;' "Expedition destination rows cannot absorb unused viewport height into an oversized button"
Assert-Contains $setup 'ExpeditionSetupLayoutPolicy.DestinationRowHeight' "Expedition destination row height is driven by the shared compact layout policy, not a magic literal"

# --- 0.6.5 UX/feedback repair: explicit Start result contract (tests #2, #3, #5, #6, #7) -----------
Assert-Contains $coordinator 'internal static bool TryStartRouteExact(SimPlayerTracking tracking, string finalDestination,' "TryStartRouteExact returns an explicit success/failure result rather than a fire-and-forget command"
Assert-Contains $coordinator 'out ExpeditionStartOutcome outcome' "Start has a typed outcome contract (Accepted/AlreadyActive/InvalidLeader/NoRoute/NotReady/Rejected)"
Assert-Contains $setup 'if (!ExpeditionCoordinator.TryStartRouteExact(' "setup inspects the actual Start result instead of assuming success from the click"
Assert-Contains $setup 'SetMessage("Starting expedition to " + _selectedDestination + "...", false);' "Start gives immediate, truthful in-flight feedback before the result is known"
Assert-Contains $setup '_rejectionMessage = string.IsNullOrWhiteSpace(failure) ? DefaultRejectionText(outcome) : failure;' "a definitive Start rejection always carries a concrete reason"
Assert-Contains $setup 'window itself always stays open on a rejection' "a rejected Start keeps the setup window open rather than closing/flashing silently"
Assert-Contains $setup 'Close("expedition started");' "an accepted Start closes setup only after the real result is known"
Assert-Contains $setup 'TravelStatusOverlay.ShowExpeditionStatus();' "an accepted Start hands off to the persistent expedition status surface"
Assert-Contains $setup 'else if (_rejectionMessage != null)' "a sticky rejection reason survives the per-frame leader-admission refresh instead of being overwritten by the generic hint within one frame"

# --- 0.6.5 UX/feedback repair: pending post-zone route readiness is not a failure (test #4) --------
Assert-Contains $coordinator 'if (decision == PostZoneRouteReadinessDecision.Failed)' "only a definitive readiness failure ends the expedition; an unproven-so-far probe keeps waiting"
Assert-Contains $coordinator '_session.RouteReadinessPending = true;' "post-zone route probing is exposed to the UI as its own pending state"
Assert-Contains $coordinator '_session.RouteReadinessPending = false;' "route-readiness-pending clears once a result (start or terminal) is known"
Assert-Contains $status 'expedition.RouteReadinessPending' "status text distinguishes checking-route-readiness from stale leader reacquisition text"

# --- 0.6.5 UX/feedback repair: native route proof is unchanged (test #12) --------------------------
Assert-Contains $coordinator 'LeaderController.StartExpeditionLeg(leader, destination, out legFailure, out legOutcome)' "the outcome contract still routes through the real native-order leg-start call, not a bypass"
Assert-Contains $route 'allowedFirstHops' "atlas route is constrained by verified live first hops"
Assert-Contains $routePolicy 'BuildTraversalGraph' "atlas adjacency is normalized without hardcoded destinations"
Assert-Contains $layout 'ActionRowHeight = 28f' "Sim Actions uses compact MMO action rows"
Assert-Contains $simActions 'SimActionMenuLayoutPolicy.ResolvePanelHeight' "Sim Actions height is driven by visible action count"
Assert-Contains $simActions 'layout.childControlHeight = true;' "Sim Actions layout group owns row heights instead of default RectTransform heights"
Assert-Contains $simActions 'element.flexibleHeight = 0f;' "Sim Actions rows cannot absorb unused viewport height"
Assert-Contains $simActions 'DiagnosticStatus()' "Sim Actions exposes an explicit custom-vs-native UI diagnostic"
Assert-Contains $plugin 'PluginVersion = "0.6.15"' "Follow release version identifies the zero-sample inner-ring repair candidate"
Assert-Contains $leader 'if (releasePriorState) _leader.FreeFollow();' "expedition releases selected Sim guard/rest posture before travel"
Assert-Contains $leader '_leader.AssignGuardSpot(target);' "expedition assigns the selected Sim travel target"
Assert-Contains $leader 'sim.GetThisNPC()' "expedition resolves the selected Sim native NPC movement owner"
Assert-Contains $leader 'ExpeditionMovementPolicy.Evaluate' "expedition distinguishes movement ownership from route geometry"
Assert-Contains $leader 'ExpeditionMovementOwnershipPolicy.ShouldSuppressDoGuard' "expedition has an explicit narrow vanilla DoGuard ownership gate"
Assert-Contains $leader 'MyStats.actualRunSpeed' "movement adapter selects the leader native run speed"
Assert-Contains $leader 'animator.SetBool("Walking", walking)' "locomotion animation follows actual movement evidence"
Assert-Contains $leader 'animator.SetBool("Patrol", false)' "expedition locomotion never leaves patrol animation asserted"
Assert-Contains $doGuardPatch '[HarmonyPatch(typeof(SimPlayer), "DoGuard")]' "exact native SimPlayer.DoGuard method is patched"
Assert-Contains $doGuardPatch 'LeaderController.ShouldSuppressNativeDoGuard' "DoGuard patch delegates to exact expedition ownership policy"
Assert-Contains $doGuardPatch 'return true;' "uncertain DoGuard ownership fails open to vanilla"
Assert-Contains $ownershipPolicy '!input.Combat && !input.ExplicitHold && !input.Regrouping && !input.Paused' "DoGuard suppression excludes combat/hold/regroup/pause"
Assert-Contains $ownershipPolicy '!input.TerminalCleanup && !input.CrossingHandoff && !input.NativeZoning' "DoGuard suppression excludes cleanup/crossing/zoning"
Assert-NotContains $leader 'GameData.SimPlayerGrouping.GroupFollow' "expedition never issues a group-wide Follow order"
Assert-Contains $planner 'collider.ClosestPoint(point)' "crossing distance uses true collider shape before bounds fallback"
Assert-Contains $planner 'BuildCrossingTraversalTargets' "planner builds bounded native crossing traversal targets"
Assert-Contains $planner 'PathIntersectsTrigger' "crossing target must prove a path through the live trigger"
Assert-Contains $leader 'ExpeditionCrossingPolicy.Evaluate' "crossing attempts are governed by a bounded pure policy"
Assert-Contains $leader 'crossing_attempt_started' "crossing attempt phase is logged once at phase boundary"
Assert-Contains $leader 'crossing_trigger_entered' "native trigger entry is observed for exact expedition actors"
Assert-Contains $leader 'all pre-built route candidates failed; performing one bounded live re-sample' "all failed geometry candidates get one bounded live re-sample"
Assert-Contains $leader 'BeginExpeditionCrossingHandoff' "leader-first native zoning preserves a bounded player crossing handoff"
Assert-Contains $follow 'internal static bool BeginExpeditionCrossingHandoff' "Follow exposes only the bounded already-proven crossing handoff"
Assert-Contains $follow 'crossingHandoff ? 0.20f : StopDistance' "crossing handoff does not stop at ordinary three-meter follow distance"

# --- 0.6.7 player-follow local-obstacle repair (tests #4, #6, #8) -----------------------------------
# #4: the player still follows the actual NavMesh corner sequence toward the leader - the local-obstacle
# repair only changes WHICH point the sequence targets and adds a bounded lateral probe, it never
# replaces corner-following with direct steering.
Assert-Contains $follow 'Path.corners[1]' "player follow still advances through NavMesh path corners toward the leader"
Assert-Contains $follow '_hasNextCorner' "player follow still tracks the next corner to round it rather than stopping dead at each waypoint"
Assert-Contains $follow 'controller.SimpleMove(direction * appliedSpeed)' "player movement still goes through the native CharacterController, never a teleport/warp"
Assert-NotContains $follow 'player.transform.position =' "player follow never assigns the player's transform position directly (no teleport)"
# #6/#7: the outer bounded strike-counter/timeout that decides genuine failure is untouched by the new
# local-obstacle classification layer - it still owns exactly when to stop.
Assert-Contains $follow 'FollowStuckRecoveryPolicy.Evaluate(' "the existing bounded stuck-recovery timeout still gates genuine expedition failure"
Assert-Contains $follow 'FollowLocalObstaclePolicy.Classify(' "player follow classifies why it stalled before choosing a repath strategy"
Assert-Contains $follow 'FollowLocalObstaclePolicy.ChooseStrategy(' "player follow picks a bounded repath strategy from the stall classification"
Assert-Contains $follow 'TrySidestepWaypoint' "a physically blocked player gets a bounded local side-step probe before an ordinary repath"
# #3: trailing point, not the leader's exact transform.
Assert-Contains $follow 'FollowLocalObstaclePolicy.TrailingTarget(' "player follow targets a trailing point behind the leader, not its exact position"
# #8: crossing acceptance/geometry policy files are untouched by this pass - same proven markers as the
# prior Duskenlight repair remain present verbatim.
Assert-Contains $planner 'CrossingSeedGeometryPolicy' "crossing floor-seed geometry repair remains in place, unchanged by the player-follow repair"
Assert-Contains $crossingPolicy 'internal static class ExpeditionCrossingPolicy' "crossing traversal policy file is untouched by the player-follow repair"

# --- 0.6.8 follow-distance / catch-up tuning (tests #9, #10) -----------------------------------------
# #9: the catch-up speed multiplier is applied only to the LOCAL variable used for this frame's
# CharacterController.SimpleMove call - it must never be written back to the player's persistent stats,
# which would leave a permanent speed change behind after catch-up disengages.
Assert-NotContains $follow 'MyStats.actualRunSpeed =' "catch-up speed boost is never written back to the player's persistent run-speed stat"
Assert-NotContains $follow 'MyStats.RunSpeed =' "catch-up speed boost never mutates the player's base run-speed stat"
Assert-Contains $follow 'float appliedSpeed = speed * catchupMultiplier' "catch-up multiplies a local speed variable only, on top of the player's own native run speed"
Assert-Contains $follow 'FollowLocalObstaclePolicy.ClassifyFormation(' "player follow classifies the formation/catch-up band from the leader's real distance"
Assert-Contains $follow 'FollowLocalObstaclePolicy.FormationSpeedMultiplier(' "player follow derives the catch-up multiplier from the formation policy, not a hardcoded boost"
Assert-Contains $follow 'HorizontalDistance(from, leaderPosition)' "formation distance is measured against the leader's real position, not the trailing nav-target"
# #10: crossing routing/geometry files remain untouched by this distance/speed tuning pass too.
Assert-Contains $crossingObserver '[HarmonyPatch(typeof(Zoneline), "OnTriggerEnter")]' "crossing trigger observer is untouched by the follow-distance tuning pass"
Assert-Contains $coordinator 'ZoneAtlasRoutePlanner.TryBuild(_session.CurrentZone, _session.DestinationName' "route recalculation is untouched by the follow-distance tuning pass"
Assert-Contains $crossingObserver '[HarmonyPatch(typeof(Zoneline), "OnTriggerEnter")]' "read-only native Zoneline trigger observer is installed"
Assert-Contains $crossingObserver 'LeaderController.NoteNativeZonelineTrigger' "trigger observer forwards observations without zoning itself"
Assert-Contains $crossingPolicy 'MaximumAttempts = 2' "crossing retry count is bounded"
Assert-Contains $coordinator 'ExpeditionDestinationResolver.Resolve(route[1]' "current first leg is resolved through live Zoneline authority"
Assert-Contains $coordinator 'SimTrackingRebind.AvatarMatchesTracking' "exact persistent Sim identity is enforced"
Assert-Contains $coordinator 'ZoneAtlasRoutePlanner.TryBuild(_session.CurrentZone, _session.DestinationName' "route recalculates after each native zone"
Assert-Contains $coordinator 'zone_transition_observed' "coordinator records native transition observation"
Assert-Contains $coordinator 'destination_zone_entered' "coordinator records verified destination-zone entry"
Assert-Contains $coordinator 'leader_reacquired' "coordinator records exact leader reacquisition"
Assert-Contains $coordinator 'next_leg_revalidated' "next expedition leg is revalidated after native zoning"
Assert-Contains $coordinator 'TickPostZoneRouteReadiness' "post-zone next leg waits for fresh route readiness evidence"
Assert-Contains $coordinator 'fresh-zoneline-and-navmesh-probe' "post-zone readiness never uses a blind sleep"
Assert-Contains $postZoneReadiness 'TimeoutSeconds = 8.0f' "post-zone route readiness has a bounded timeout"
Assert-Contains $postZoneReadiness 'MaximumAttempts = 16' "post-zone route readiness has a bounded retry count"
Assert-Contains $status 'Changing zones...  Reacquiring' "status reports native zoning/reacquisition"
Assert-Contains $status 'ExpeditionWorkflowPolicy.ShouldShowExpeditionSurface' "the status surface's visibility is driven by the shared, tested terminal-visibility policy"
Assert-Contains $status 'case ExpeditionState.Cancelled:' "a cancelled expedition is shown with its reason instead of vanishing (test #9)"
Assert-Contains $status 'case ExpeditionState.Failed:' "a failed expedition is shown with its reason instead of vanishing (test #5)"
Assert-Contains $status 'ExpeditionCoordinator.Pause' "status Pause uses coordinator"
Assert-Contains $status 'ExpeditionCoordinator.Resume' "status Resume uses coordinator"
Assert-Contains $status 'ExpeditionCoordinator.Cancel' "status Cancel is explicit"
Assert-NotContains $status 'HideExpeditionStatus();\r\n                    ExpeditionCoordinator.Cancel' "window close is not coupled to cancel"
Assert-Contains $status 'CampmasterIntegrationBridge.TryDeclareHere' "Camp Here remains capability-backed"
Assert-Contains $status 'ExpeditionCoordinator.TryReturn' "verified arrival can initiate Return"
Assert-Contains $plugin 'TravelStatusOverlay.Tick();' "status continues rendering through native zoning lifecycle"
Assert-Contains $plugin 'ExpeditionSetupWindow.DisposeForLifecycle();' "plugin unload destroys setup UI"
Assert-Contains $plugin 'TravelStatusOverlay.ResetForLifecycle();' "plugin unload destroys status UI"
Assert-Contains $plugin 'ExpeditionCoordinator.Shutdown();' "plugin unload cancels/clears expedition runtime"
Assert-Contains $dragGuard 'eventData.button != PointerEventData.InputButton.Left' "Follow retained drag gestures are left-button only"
Assert-Contains $dragGuard 'AcquireOwnership();' "Follow drag ownership begins at pointer-down/begin-drag"
Assert-Contains $dragGuard 'Input.GetMouseButton(0)' "Follow drag ownership releases when the physical left button is gone"
Assert-Contains $dragGuard 'OnApplicationFocus' "Follow drag ownership releases on focus loss"
Assert-Contains $dragGuard 'OnApplicationPause' "Follow drag ownership releases on pause"
Assert-Contains $cameraUiPatch '[HarmonyPatch(typeof(CameraController), "UsingUI")]' "Follow owns a standalone-safe CameraController.UsingUI compatibility patch"
Assert-Contains $cameraUiPatch '!__result && FollowUiDragGuard.OwnsPointerGesture' "camera UI postfix is monotonic and only raises native UI state"
Assert-Contains $doGuardPatch '[HarmonyPrepare]' "DoGuard patch validates exact native method shape before applying"
Assert-Contains $doGuardPatch 'method.ReturnType == typeof(void) && method.GetParameters().Length == 0' "DoGuard patch requires verified zero-argument void shape"
Assert-Contains $cameraUiPatch '[HarmonyPrepare]' "camera UI patch validates the current native boundary before applying"
Assert-Contains $cameraUiPatch 'FollowCameraCompatibility.VerifyUsingUiBoundary' "camera patch delegates to the full runtime compatibility proof"
Assert-Contains $cameraCompat 'UIWindows' "camera compatibility verifies native UIWindows field"
Assert-Contains $cameraCompat 'typeof(List<GameObject>)' "camera compatibility requires UIWindows List<GameObject>"
Assert-Contains $cameraCompat 'activeSelf' "camera compatibility verifies UsingUI activeSelf scan"
Assert-Contains $cameraCompat 'ModernControls' "camera compatibility verifies modern camera path"
Assert-Contains $cameraCompat 'releaseMouse' "camera compatibility verifies modern releaseMouse gate"
Assert-Contains $cameraCompat 'GetAxis' "camera compatibility verifies native mouse-axis use"
Assert-Contains $cameraCompat 'DraggingUIElement' "camera compatibility verifies standard drag flag gate"
Assert-Contains $cameraCompat 'GetILAsByteArray' "camera compatibility inspects current runtime IL rather than trusting a stale token"
Assert-Contains $dragGuard 'End(true);' "pointer-up/end path completes a real Follow drag exactly once"
Assert-Contains $dragGuard 'forgetwhtuno.erenshor.ui.drag.owners.v1' "Follow drag ownership coordinates through the standalone BCL process registry"
Assert-Contains $dragGuard 'forgetwhtuno.erenshor.ui.drag.nativeBaseline.v1' "Follow drag ownership shares the process native-baseline key"
Assert-Contains $dragGuard 'ReleaseProcessOwnership' "Follow release cannot blindly clear another participating mod owner"
Assert-Contains $control 'FollowUiSurfaceRouter.CloseAllVisuals()' "Suite closePanel closes all Follow visual surfaces without cancelling gameplay"
Assert-Contains $aura 'FollowUiSurfaceRouter.Topmost()' "Suite ui.state advertises top Follow surface"

$productionSource = (Get-ChildItem (Join-Path $repoRoot "src") -Filter *.cs | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
Assert-NotContains $productionSource 'SceneChange.ChangeScene(' "Follow never manually loads expedition scenes"
Assert-NotContains $productionSource '.ZoneSim(' "Follow never directly zones a Sim"
Assert-NotContains $productionSource '.TravelToZone(' "Follow never invokes Sim tracking travel authority"
Assert-NotContains $productionSource 'BringSimToPlayer(' "Follow never teleports/reconstructs a Sim through helper authority"
Assert-NotContains $productionSource 'SpawnMeInPlayerZone(' "Follow never forces a Sim spawn into the player zone"
Assert-NotContains $productionSource '.Warp(' "Follow never warps the expedition leader"
Assert-NotContains $productionSource 'transform.position =' "Follow never manually moves the Sim leader transform"
Assert-NotContains $productionSource 'void OnGUI(' "production UI contains no OnGUI"

# --- optional-integration boundary (tests #13, #14): reflection only, never a compile-time reference ---
Assert-NotContains $productionSource 'using ErenshorDeepSims' "no compile-time dependency on Deep Sims"
Assert-NotContains $productionSource 'using ErenshorDuel' "no compile-time dependency on Practice Duel"
Assert-Contains $productionSource 'GetType("ErenshorDeepSims.DeepSimsPlugin", false)' "Deep Sims integration remains reflection-based and optional"
Assert-Contains $simActions 'GetType("ErenshorDuel.DuelControlApi", false)' "Practice Duel integration remains reflection-based and optional"

# --- shared standalone-launcher visual/placement pass -----------------------------------------------
Assert-Contains $plugin 'StandaloneLauncherColumnPolicy.DefaultX()' "Follow launcher default X comes from the shared right-side column policy"
Assert-Contains $plugin 'StandaloneLauncherColumnPolicy.DefaultY(StandaloneLauncherColumnPolicy.SlotIndex)' "Follow launcher default Y comes from Follow's own column slot"
Assert-Contains $columnPolicy 'internal const int SlotIndex = 2;' "Follow owns column slot 2 (Journal=0, Duel=1, Follow=2)"
Assert-Contains $fallbackUi 'LauncherWidth = 154f' "shared fallback launcher matches the canonical 154-wide launcher geometry"
Assert-Contains $fallbackUi 'LauncherHeight = 32f' "shared fallback launcher matches the canonical 32-tall launcher geometry"
Assert-Contains $fallbackUi 'LauncherGripWidth = 20f' "shared fallback launcher matches the canonical 20px grip width"
Assert-Contains $fallbackUi 'LauncherBorder = 1f' "shared fallback launcher has the canonical 1px outline border"
Assert-Contains $fallbackUi 'AddLauncherFrame(_launcher)' "shared fallback launcher draws the canonical outline frame"
Assert-Contains $fallbackUi '"GripDot"' "shared fallback launcher draws the canonical three-dot grip"
Assert-Contains $fallbackUi 'for (int i = -1; i <= 1; i++)' "shared fallback launcher grip renders exactly three dots centered on the grip"
Assert-Contains $fallbackUi 'float defaultLauncherX, float defaultLauncherY' "shared fallback launcher takes a normalized default position instead of a raw pixel Y"
Assert-Contains $fallbackUi 'if (target == _launcher)' "shared fallback launcher now actually persists a dragged launcher position (previously a no-op)"
Assert-Contains $fallbackUi 'ResolveLauncherPosition' "shared fallback launcher re-resolves its position on resolution change like the Journal launcher"
Assert-Contains $fallbackUi 'private static FallbackChevronGraphic EnsureChevron(RectTransform owner)' "shared-chevron regression guard: panel collapse chevron is still built as its own child, never added directly onto the button Graphic"
Assert-NotContains $fallbackUi 'collapse.gameObject.AddComponent<FallbackChevronGraphic>' "shared-chevron regression guard: never re-add FallbackChevronGraphic directly onto the Collapse button (zero recurrence of the historical Graphic-conflict crash)"
# --- 0.6.10 zoneline crossing reliability ------------------------------------------------------
# Failure A: the leader reached its verified approach (destination=0.0m-from-order, PathComplete,
# velocity=0.00) and was reissued 1/2 then 2/2 before failing as "the native movement owner made no
# useful progress". Root cause: LeaderController's tick short-circuits on
# `if (HandleMovementAcquisition()) return;`, which sits ABOVE the crossing handoff, and the movement
# policy carried no remaining-distance - so arrival was numerically identical to a stall, the proof
# never cleared, and HandleCrossingAttempt() was structurally unreachable.
$movementPolicy = Get-Content (Join-Path $repoRoot "src\ExpeditionMovementPolicy.cs") -Raw
$leader = Get-Content (Join-Path $repoRoot "src\LeaderController.cs") -Raw
$planner = Get-Content (Join-Path $repoRoot "src\LocalZoneRoutePlanner.cs") -Raw
$seedPolicy = Get-Content (Join-Path $repoRoot "src\CrossingSeedGeometryPolicy.cs") -Raw

Assert-Contains $movementPolicy 'internal float DistanceToTarget;' "movement observation carries remaining distance so arrival can be told apart from a stall"
Assert-Contains $movementPolicy 'ArrivedAtTarget' "the movement policy has an explicit arrival decision"
Assert-Contains $movementPolicy 'ApproachReachedTraversalPending' "arrival is classified as traversal-pending, not as a movement-ownership failure"
Assert-Contains $movementPolicy 'if (observation.DistanceToTarget <= ArrivalRadius)' "arrival is decided before any stall/reissue reasoning"
Assert-Contains $movementPolicy 'internal const float ArrivalRadius = 1.75f;' "arrival radius matches ExpeditionCrossingPolicy.ApproachReadyDistance"
Assert-Contains $leader 'observation.DistanceToTarget = float.MaxValue;' "an unmeasured distance can never be mistaken for arrival at the origin"
Assert-Contains $leader 'observation.DistanceToTarget = HorizontalDistance(now, target);' "remaining distance comes from the real leader position"
Assert-Contains $leader 'if (decision == ExpeditionMovementDecision.ArrivedAtTarget)' "arrival is handled distinctly from ordinary progress"
# The crossing handoff must remain reachable: arrival returns false so the tick CONTINUES past the
# movement-acquisition short circuit into AdvanceZoneWaypointIfReached()/HandleCrossingAttempt().
Assert-Contains $leader 'if (_monster == null && HandleCrossingAttempt()) return;' "the crossing handoff is still ticked"
Assert-Contains $leader 'EmitCrossingHandoffDiagnostic' "the crossing handoff emits its bounded diagnostic"
foreach ($field in @('crossing_handoff','phase=','approach=','leaderDistanceToApproach=','colliderBounds=','traversalCandidates=','selectedTraversalTarget=','pathStatus=','endpointDistance=','insideTrigger=','agentVelocity=','zoneChanged=')) {
    Assert-Contains $leader $field "crossing handoff diagnostic reports $field"
}

# Failure B: Hidden -> Duskenlight generated 14 seeds, only 2 sampled, both ~40m from the verified
# crossing. Oriented/vertical seeding searches the collider's real basis and height range; the
# proximity filter stops far axis-aligned bounds corners from consuming the seed budget.
Assert-Contains $seedPolicy 'internal static Point3[] OrientedFaceOffsets()' "oriented face seeds exist for rotated/scaled triggers"
Assert-Contains $seedPolicy 'internal static float[] VerticalProbeOffsets(int steps)' "a bounded vertical probe exists for triggers offset from walkable ground"
Assert-Contains $seedPolicy 'internal static bool SeedIsWorthSampling' "seed budget is spent only on seeds that could still be accepted"
Assert-Contains $planner 'AddOrientedCrossingSeeds' "the planner generates oriented seeds from the collider's own transform"
Assert-Contains $planner 't.TransformPoint(localPoint)' "oriented seeds are transformed by the real collider transform, not axis-aligned bounds arithmetic"
Assert-Contains $planner 'RouteCandidatePolicy.NativeProbeApproachNearCrossing' "the seed filter is derived from the existing acceptance distance"
Assert-Contains $planner 'AddCrossingProximitySeed' "far seeds are filtered before they consume sample budget"

# Acceptance must NOT be weakened: a 40m endpoint stays rejected and the existing proofs remain.
$routePolicy = Get-Content (Join-Path $repoRoot "src\RouteCandidatePolicy.cs") -Raw
Assert-Contains $routePolicy 'CompleteApproachNearCrossing = 8.0f' "crossing proximity acceptance is unchanged (8m), so a 40m endpoint stays rejected"
Assert-Contains $routePolicy 'NativeProbeApproachNearCrossing = 8.0f' "native-probe proximity acceptance is unchanged"
Assert-Contains $planner 'NavMesh.SamplePosition' "every seed still requires a real NavMesh sample"
Assert-Contains $planner 'NavMesh.CalculatePath' "every candidate still requires a real NavMesh path"

# Native transition remains the only authority; no teleport/warp/scene-load shortcut anywhere.
Assert-NotContains $leader 'SceneManager.LoadScene' "zone transition is never forced by loading a scene"
Assert-NotContains $leader 'nav.Warp(' "the leader is never warped across a zoneline"
Assert-NotContains $planner 'SceneManager.LoadScene' "the planner never loads a scene"

# Frozen 0.6.9 player-follow behavior: these live-proven constants must not be retuned here.
$followController = Get-Content (Join-Path $repoRoot "src\FollowController.cs") -Raw
$obstaclePolicy = Get-Content (Join-Path $repoRoot "src\FollowLocalObstaclePolicy.cs") -Raw
Assert-Contains $obstaclePolicy 'IsCatchUpActive' "0.6.9 catch-up policy is still the single owner of catch-up activation"
Assert-Contains $obstaclePolicy 'FormationSpeedMultiplier' "0.6.9 catch-up speed multiplier policy is unchanged"
Assert-Contains $followController 'FollowLocalObstaclePolicy.IsCatchUpActive(formationBand)' "player catch-up still routes through the frozen 0.6.9 policy"
Assert-Contains $followController 'FollowLocalObstaclePolicy.FormationSpeedMultiplier(formationBand)' "player catch-up multiplier still routes through the frozen 0.6.9 policy"

# 0.6.12 Duskenlight -> Hidden large-trigger seed regression. The 0.6.10 proximity filter measured
# distance to the crossing's RAW TRANSFORM POINT, which for the live 67.5 x 47.1 x 59.4 Hidden
# BoxCollider discarded every seed on the trigger's own lateral faces (33.75m / 29.68m from centre,
# vs a 12m threshold) and left only the column of seeds above its centre: generatedSeeds=16,
# samples=0. Relevance must be measured against the verified collider VOLUME - the same metric
# acceptance already uses - so a seed INSIDE the trigger is never discarded for being far from centre.
Assert-Contains $seedPolicy 'internal static bool SeedIsWorthSamplingNearVolume' "seed relevance is measured against the verified collider volume"
Assert-Contains $seedPolicy 'if (seedInsideCrossingVolume) return true;' "a seed inside the verified trigger volume is never discarded for being far from centre"
Assert-Contains $seedPolicy 'internal static float LocalBoxSurfaceDistance' "distance to an oriented trigger volume is exact, honouring rotation and scale"
Assert-Contains $seedPolicy 'internal static bool IsInsideLocalBox' "volume containment is an explicit, testable fact"
Assert-Contains $planner 'CrossingSeedGeometryPolicy.SeedIsWorthSamplingNearVolume' "the planner filters seeds by volume distance, not centre distance"
Assert-Contains $planner 'float volumeDistance = DistanceToCrossingVolume(position, crossing, colliders);' "the seed filter reuses the acceptance metric so both agree by construction"
Assert-Contains $planner 'IsInsideAnyCrossingCollider' "the planner resolves real collider containment from the live collider"
Assert-NotContains $planner 'HorizontalDistance(position, crossingPosition),' "the removed centre-based seed measurement must not come back"

# Per-seed diagnostics: proves whether a useful seed class was never generated, was generated and
# then filtered, or survived and genuinely failed NavMesh.SamplePosition. Route-build only, bounded
# by the seed budget - never per frame.
Assert-Contains $planner 'internal sealed class SeedDiagnostic' "per-seed route-build diagnostics exist"
foreach ($field in @('DistanceToRawCenter','DistanceToColliderVolume','InsideCollider','FilterReason','SampleHit')) {
    Assert-Contains $planner $field "seed diagnostic records $field"
}
foreach ($field in @('dRaw=','dVol=','inside=',' filtered',' sampled=','filteredSeeds=')) {
    Assert-Contains $planner $field "seed diagnostic emits $field"
}
Assert-Contains $planner 'diagnostics.Count >= MaxSeedsPerCrossing * 2' "seed diagnostics stay bounded"

# 0.6.13 live follow-up: 0.6.12 retained the large Hidden trigger seeds correctly, but every
# retained seed still reported sampled=False. The live working historical approach was at an
# intermediate Y (~50.06), while current candidates clustered at centre/floor/top. Also freeze the
# corrected OBB construction: collider.bounds is a world AABB and cannot be inverse-transformed as
# though it were BoxCollider local extents.
Assert-Contains $seedPolicy 'IntermediateVerticalLayersMeaningfullyDifferFromCenter' "tall triggers can prove that centre/floor layers leave a vertical blind band"
Assert-Contains $seedPolicy 'LowerIntermediateInteriorOffsets' "tall triggers get a bounded lower-mid interior cross rather than a grid scan"
Assert-Contains $seedPolicy 'LowerIntermediateApproachFaceOffsets' "tall triggers add a bounded route-facing lower-mid surface band"
Assert-Contains $planner 'BoxCollider box = collider as BoxCollider;' "oriented seeds prefer the BoxCollider authoritative local geometry"
Assert-Contains $planner 'localCenter = box.center;' "oriented seeds include an off-centre BoxCollider real local centre"
Assert-Contains $planner 'Vector3 size = box.size;' "oriented seeds use BoxCollider local size rather than world AABB extents"
Assert-Contains $planner 'LowerIntermediateInteriorOffsets()' "planner emits the bounded lower-mid interior layer for sufficiently tall triggers"
Assert-Contains $planner 'LowerIntermediateApproachFaceOffsets' "planner combines route-facing horizontal surface position with intermediate height"
Assert-Contains $planner 'MaxSeedsPerCrossing = 38' "seed budget reserves eight zero-sample fallback slots and remains fixed/bounded"
Assert-Contains $planner 'localCenter=' "failure diagnostics expose authoritative BoxCollider local centre"
Assert-Contains $planner 'localSize=' "failure diagnostics expose authoritative BoxCollider local size"
Assert-Contains $planner 'Build(start, crossings, ErenshorFollowPlugin.VerboseDiagnostics)' "ordinary planning only records per-seed forensics when verbose diagnostics are enabled"
Assert-Contains $planner 'AddZeroSampleInteriorRingSeeds' "zero-sample-only large-trigger interior ring fallback exists"
Assert-Contains $planner 'if (sampled.Count == 0)' "inner ring is only considered after every primary seed misses NavMesh"
Assert-Contains $planner 'midRing' "fallback ring seeds are individually diagnosable"
Assert-Contains $seedPolicy 'LowerIntermediateFallbackRingOffsets' "pure bounded inner-ring geometry policy exists"
$routeDiagnostics = Get-Content (Join-Path $repoRoot "src\RouteDiagnostics.cs") -Raw
Assert-Contains $routeDiagnostics 'LocalZoneRoutePlanner.Build(start, crossings, true)' "explicit route diagnostics still request forensic seed detail"
Assert-Contains $routeDiagnostics 'ZoneAtlasRoutePlanner.TryBuild' "route diagnostics follow the same multi-hop atlas decision tree as /elead"
Assert-Contains $routeDiagnostics 'Atlas itinerary:' "multi-hop diagnostics expose the candidate itinerary and first hop"
Assert-Contains $leader 'ZeroCandidateDetailRepeatSeconds = 10f' "giant zero-candidate forensic lines are rate-limited during repeated readiness retries"
Assert-Contains $leader 'ErenshorFollowPlugin.VerboseDiagnostics' "zero-candidate forensic formatting is skipped in normal play"
Assert-Contains $plugin 'if (!VerboseDiagnostics) return;' "Follow LogDebug centrally suppresses forensic I/O when Diagnostics/Verbose is off"

# The seed budget is retained (only resized) and sample radii are NOT globally widened.
Assert-Contains $planner 'private const int MaxSeedsPerCrossing' "the seed budget still exists"
Assert-Contains $planner 'private const float FloorSeedRadius = 4f;' "seed sample radius is unchanged - the fix is filtering, not wider sampling"
Assert-Contains $planner 'private const int MaxApproachesPerCrossing = 6;' "the sampled-approach budget is unchanged"

Write-Host "Erenshor Follow 0.6.15 zoneline crossing static guards: PASS" -ForegroundColor Green

# UI workspace normalization pass: compact status box + shared right-side default workspace,
# without changing Initialize's positional call site (see StandaloneFallbackUi.cs comment on why).
$followPlugin = Read-RepoFile "src\ErenshorFollowPlugin.cs"
$columnPolicy = Read-RepoFile "src\StandaloneLauncherColumnPolicy.cs"
Assert-Contains $followPlugin 'StandaloneFallbackUi.ConfigureWorkspaceDefaults(56f,' "Follow opts into the compact status box + shared default workspace"
Assert-Contains $followPlugin 'StandaloneLauncherColumnPolicy.DefaultPanelRightNormalized()' "Follow's panel default derives from the shared right-side workspace anchor"
Assert-Contains $followPlugin 'StandaloneLauncherColumnPolicy.DefaultPanelTopNormalized()' "Follow's panel default derives from the shared below-launcher-stack anchor"
Assert-Contains $columnPolicy 'internal const int SlotIndex = 2;' "Follow's column slot is 2 (Journal=0, Duel=1, Follow=2)"
Assert-NotContains $columnPolicy 'RightMarginNormalized = 0.006f' "the old right margin (fully swallowed by the launcher-width clamp) is gone"
$fallbackUiSource = Read-RepoFile "..\..\Erenshor-Mod-Suite\shared\ErenshorSuite.UI\StandaloneFallbackUi.cs"
Assert-Contains $fallbackUiSource 'internal static void ConfigureWorkspaceDefaults' "shared StandaloneFallbackUi exposes the opt-in workspace-defaults API"
Assert-Contains $fallbackUiSource '_launcherObject.SetActive(!_hubUsable)' "Hub-healthy suppression of the standalone launcher is unchanged"
Assert-Contains $fallbackUiSource 'if (_openAccent != null) _openAccent.SetActive(_open)' "shared launcher exposes a structural (non-color-only) open/active cue"
Write-Host "Erenshor Follow UI workspace normalization guard: PASS" -ForegroundColor Green

Write-Host "Erenshor Follow release static tests: ALL PASS" -ForegroundColor Green
