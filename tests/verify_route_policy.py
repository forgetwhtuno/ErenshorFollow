#!/usr/bin/env python3
"""Portable mirror of the pure RouteCandidatePolicy acceptance vectors.

The Windows C# harness compiles the real C# policy. This script exists so the same deterministic
acceptance/ranking vectors can be run in source-only environments that do not have a .NET compiler.
"""
from dataclasses import dataclass
from pathlib import Path
import re

SOURCE = Path(__file__).parents[1] / "src" / "RouteCandidatePolicy.cs"
text = SOURCE.read_text(encoding="utf-8")

def const(name):
    m = re.search(rf"internal const float {name} = ([0-9.]+)f;", text)
    assert m, f"missing policy constant {name}"
    return float(m.group(1))

COMPLETE_NEAR = const("CompleteApproachNearCrossing")
PARTIAL_NEAR = const("PartialEndpointNearCrossing")
NATIVE_NEAR = const("NativeProbeApproachNearCrossing")
FLOOR = const("PartialMinimumProgressFloor")
CEILING = const("PartialMinimumProgressCeiling")
FRACTION = const("PartialMinimumProgressFraction")

@dataclass
class C:
    key: str
    sampled: bool = True
    path: str = "Invalid"
    corners: int = 0
    start: float = 0
    endpoint: float = 0
    approach: float = 0
    length: float = 999999
    active: bool = True
    remove_party: bool = False

def evaluate(c):
    if not c.active: return "Rejected"
    if c.remove_party: return "Rejected"
    if not c.sampled: return "Rejected"
    if c.path == "Complete" and c.corners >= 2:
        return "Complete" if c.approach <= COMPLETE_NEAR else "Rejected"
    if c.path == "Partial" and c.corners >= 2:
        required = max(FLOOR, min(CEILING, max(0, c.start) * FRACTION))
        progress = c.start - c.endpoint
        return "PartialNearCrossing" if c.endpoint <= PARTIAL_NEAR and progress >= required else "Rejected"
    return "NativeProof" if c.approach <= NATIVE_NEAR else "Rejected"

def rank(cs):
    tier = {"Complete": 0, "PartialNearCrossing": 1, "NativeProof": 2}
    accepted = [(evaluate(c), c) for c in cs if evaluate(c) != "Rejected"]
    accepted.sort(key=lambda ec: (tier[ec[0]], max(0, ec[1].length), max(0, ec[1].endpoint), max(0, ec[1].approach), ec[1].key))
    return [c.key for _, c in accepted]

def resolve(names, query):
    # names: (canonical, active, remove_party)
    q = query.strip().lower()
    exact = []
    exact_removing = False
    for name, active, removing in names:
        if not active or name.lower() != q: continue
        if removing: exact_removing = True
        elif name.lower() not in [x.lower() for x in exact]: exact.append(name)
    if exact: return exact[0], False, False
    if exact_removing: return None, False, True
    partial = []
    partial_removing = False
    for name, active, removing in names:
        if not active or q not in name.lower(): continue
        if removing: partial_removing = True
        elif name.lower() not in [x.lower() for x in partial]: partial.append(name)
    if len(partial) == 1: return partial[0], False, False
    if len(partial) > 1: return None, True, False
    return None, False, partial_removing

def main():
    checks = []
    invalid = C("crossing-1", sampled=False, start=20, endpoint=20, approach=12)
    complete = C("crossing-2", path="Complete", corners=4, start=18, endpoint=0, approach=1, length=22)
    checks.append((rank([invalid, complete]) == ["crossing-2"], "multiple crossings select complete candidate"))

    partial = C("partial-near", path="Partial", corners=5, start=20, endpoint=3.5, approach=1.5, length=18)
    checks.append((evaluate(partial) == "PartialNearCrossing", "partial near crossing accepted"))

    stalled = C("partial-stall", path="Partial", corners=3, start=6, endpoint=5.5, approach=2, length=1)
    checks.append((evaluate(stalled) == "Rejected", "partial with no meaningful progress rejected"))

    checks.append((rank([C("a", sampled=False), C("b", sampled=False)]) == [], "all invalid fails cleanly"))

    r = resolve([("Vitheo's Watch", True, False), ("Vitheo's Woods", True, False)], "Vitheo")
    checks.append((r == (None, True, False), "different partial destination names ambiguous"))

    r = resolve([("Vitheo's Watch", True, False), ("Vitheo's Watch", True, False)], "Watch")
    checks.append((r == ("Vitheo's Watch", False, False), "same destination duplicate not ambiguous"))

    rp = C("remove-party", path="Complete", corners=4, start=8, endpoint=0, approach=0, length=8, remove_party=True)
    checks.append((evaluate(rp) == "Rejected", "RemoveParty route rejected"))

    a = C("a", path="Complete", corners=4, start=20, endpoint=0, approach=1, length=12)
    b = C("b", path="Complete", corners=4, start=20, endpoint=0, approach=1, length=16)
    c = C("c", path="Partial", corners=4, start=20, endpoint=3, approach=2, length=10)
    checks.append((rank([a,b,c]) == rank([c,b,a]) == ["a","b","c"], "ranking deterministic regardless enumeration order"))

    for ok, name in checks:
        if not ok: raise AssertionError(name)
        print("PASS:", name)
    print(f"PASS: {len(checks)} route-candidate policy vectors")

if __name__ == "__main__":
    main()
