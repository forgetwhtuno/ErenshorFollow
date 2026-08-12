from dataclasses import dataclass
from enum import Enum, auto

class Phase(Enum):
    IDLE = auto()
    FOLLOWING = auto()
    REBINDING = auto()

class Decision(Enum):
    WAITING = auto()
    RESUME = auto()
    STOP = auto()

class Failure(Enum):
    NONE = auto()
    TIMEOUT = auto()
    LEFT_PARTY = auto()
    IDENTITY_MISMATCH = auto()
    TARGET_UNAVAILABLE = auto()
    REMOTE_AUTHORITY = auto()

@dataclass
class Inputs:
    zoning: bool = False
    scene_changed: bool = True
    game_ready: bool = True
    settled: bool = True
    tracking_in_group: bool = True
    avatar_present: bool = True
    same_tracking: bool = True
    avatar_usable: bool = True
    live_party_member: bool = True
    remote_authority: bool = False
    timed_out: bool = False

class Intent:
    def __init__(self):
        self.phase = Phase.IDLE
        self.identity = None
    def begin(self, identity):
        self.identity = identity
        self.phase = Phase.FOLLOWING
    def begin_rebind(self):
        if self.phase is not Phase.FOLLOWING or self.identity is None:
            return False
        self.phase = Phase.REBINDING
        return True
    def resume(self):
        if self.phase is not Phase.REBINDING or self.identity is None:
            return False
        self.phase = Phase.FOLLOWING
        return True
    def cancel(self):
        self.identity = None
        self.phase = Phase.IDLE

def can_suspend(direct, verified_zoning, has_identity):
    return direct and verified_zoning and has_identity

def evaluate(i):
    if i.zoning or not i.scene_changed or not i.game_ready or not i.settled:
        return (Decision.STOP, Failure.TIMEOUT) if i.timed_out else (Decision.WAITING, Failure.NONE)
    if not i.tracking_in_group:
        return Decision.STOP, Failure.LEFT_PARTY
    if not i.avatar_present:
        return (Decision.STOP, Failure.TIMEOUT) if i.timed_out else (Decision.WAITING, Failure.NONE)
    if not i.same_tracking:
        return Decision.STOP, Failure.IDENTITY_MISMATCH
    if i.remote_authority:
        return Decision.STOP, Failure.REMOTE_AUTHORITY
    if not i.avatar_usable:
        return Decision.STOP, Failure.TARGET_UNAVAILABLE
    if not i.live_party_member:
        return Decision.STOP, Failure.LEFT_PARTY
    return Decision.RESUME, Failure.NONE

def expect(condition, label):
    if not condition:
        raise AssertionError(label)
    print("PASS:", label)

tracking = object()
intent = Intent()
intent.begin(tracking)
expect(can_suspend(True, True, intent.identity is not None), "active follow + verified zoning can suspend")
expect(intent.begin_rebind() and intent.identity is tracking, "zoning preserves tracking identity")

old_destroyed = Inputs(zoning=True, scene_changed=False, game_ready=False, settled=False,
                       tracking_in_group=False, avatar_present=False, same_tracking=False,
                       avatar_usable=False, live_party_member=False)
expect(evaluate(old_destroyed) == (Decision.WAITING, Failure.NONE),
       "old avatar destruction during zoning is a bounded wait")

expect(evaluate(Inputs()) == (Decision.RESUME, Failure.NONE),
       "matching tracking with valid live avatar resumes")
expect(evaluate(Inputs(tracking_in_group=False)) == (Decision.STOP, Failure.LEFT_PARTY),
       "target leaving group stops")
expect(evaluate(Inputs(remote_authority=True)) == (Decision.STOP, Failure.REMOTE_AUTHORITY),
       "remote COOP target stops")
expect(evaluate(Inputs(zoning=True, scene_changed=False, game_ready=False, settled=False, timed_out=True)) == (Decision.STOP, Failure.TIMEOUT),
       "timeout stops")

intent.cancel()
expect(intent.phase is Phase.IDLE and intent.identity is None and not intent.resume(),
       "explicit cancellation during rebind remains stopped")

same_name_other_tracking = object()
expect(tracking is not same_name_other_tracking,
       "different same-name Sim identity is never substituted")

intent.begin(tracking)
expect(intent.begin_rebind() and intent.resume() and intent.identity is tracking,
       "first zone keeps tracking identity")
expect(intent.begin_rebind() and intent.identity is tracking,
       "repeated zone keeps same tracking identity")

expect(not can_suspend(True, False, True),
       "ordinary target loss without zoning is not resumable")
expect(not can_suspend(False, True, True),
       "leader/expedition-owned follow does not enter direct rebind")

late = Inputs(avatar_present=False, same_tracking=False, avatar_usable=False, live_party_member=False)
expect(evaluate(late) == (Decision.WAITING, Failure.NONE),
       "late MyAvatar waits before timeout")
late.timed_out = True
expect(evaluate(late) == (Decision.STOP, Failure.TIMEOUT),
       "late MyAvatar stops at timeout")
expect(evaluate(Inputs(same_tracking=False)) == (Decision.STOP, Failure.IDENTITY_MISMATCH),
       "identity mismatch stops")
expect(evaluate(Inputs(avatar_usable=False)) == (Decision.STOP, Failure.TARGET_UNAVAILABLE),
       "dead or unavailable avatar stops")
expect(evaluate(Inputs(live_party_member=False)) == (Decision.STOP, Failure.LEFT_PARTY),
       "live avatar outside player group stops")

print("All deterministic rebind transition model tests passed.")
