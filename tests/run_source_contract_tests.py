from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
follow = (ROOT / "src" / "FollowController.cs").read_text(encoding="utf-8")
policy = (ROOT / "src" / "FollowRebindPolicy.cs").read_text(encoding="utf-8")
tracking = (ROOT / "src" / "SimTrackingRebind.cs").read_text(encoding="utf-8")
overlay = (ROOT / "src" / "TravelStatusOverlay.cs").read_text(encoding="utf-8")

checks = []
def check(condition, name):
    if not condition:
        raise AssertionError(name)
    checks.append(name)
    print(f"PASS: {name}")

# Safety/authority contract.
for forbidden in (
    "SceneChange.ChangeScene",
    "SceneChange.ChangeSceneSafe",
    ".ZoneSim(",
    ".TravelToZone(",
    "BringSimToPlayer",
    "SpawnMeInPlayerZone",
):
    check(forbidden not in follow + policy + tracking, f"no forbidden zoning authority call: {forbidden}")

check("FindSim(" not in follow and "FindObjectsOfType<SimPlayer>" not in follow,
      "rebind path never fuzzy-scans Sims by name")
check("SimPlayerTracking" in follow and "DirectIntent" in follow,
      "direct Follow carries persistent SimPlayerTracking identity")
check("object.ReferenceEquals" in tracking,
      "tracking identity uses reference equality")
check("GameData.GroupMembers" in tracking,
      "persistent tracking is revalidated against authoritative group roster")
check("GameData.Zoning" in follow and "BeginZoneRebind" in follow,
      "verified real zoning gates resumable Follow")
check("GameData.SimMngr" in follow and "GameData.SimPlayerGrouping" in follow,
      "rebind waits for Erenshor group/sim managers")
check("RebindSettleSeconds = 2.5f" in follow and "RebindTimeoutSeconds = 60f" in follow,
      "rebind settle/timeout matches proven Expedition scale")
check("CoopCompatibility.IsRemoteHuman" in follow,
      "rebound avatar revalidates COOP authority")
check("LeaderController.IsPlayerPartySim" in follow,
      "rebound avatar revalidates live player-party membership")
check("Rebinding after zone change" in overlay and "Follow target: " in overlay,
      "UI reports rebinding without claiming active movement")
check("FollowIntentPhase.Rebinding" in policy and "FollowRebindDecision" in policy,
      "transition-state policy is separated from Unity code")

# Rough syntax sanity checks that catch accidental truncation/unbalanced edits in this environment.
for path in (ROOT / "src").glob("*.cs"):
    text = path.read_text(encoding="utf-8")
    # Strip strings/comments enough for brace counting; this is not a C# parser.
    stripped = re.sub(r'//.*', '', text)
    stripped = re.sub(r'"(?:\\.|[^"\\])*"', '""', stripped)
    check(stripped.count("{") == stripped.count("}"), f"balanced braces: {path.name}")

print(f"All source-contract checks passed ({len(checks)} checks).")
