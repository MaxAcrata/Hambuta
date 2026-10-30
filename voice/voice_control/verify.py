"""Result confirmation against real game state (polls /health)."""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, Callable

from .commands import CommandKind
from .ipc import GameClient, IpcError


class VerifyOutcome(str):
    SUCCEEDED = "succeeded"
    FAILED = "failed"
    TIMEOUT = "timeout"
    ERROR = "error"


@dataclass(frozen=True)
class VerifyResult:
    outcome: str
    message: str
    health: dict[str, Any] | None = None

    @property
    def ok(self) -> bool:
        return self.outcome == VerifyOutcome.SUCCEEDED


def _now() -> float:
    return time.monotonic()


def verify_follow_me(client: GameClient, budget_s: float = 5.0, poll_s: float = 0.25) -> VerifyResult:
    """Success when the game reports a follow target (player) is set."""
    deadline = _now() + budget_s
    last: dict[str, Any] | None = None
    last_err = ""
    while _now() < deadline:
        try:
            last = client.health()
        except IpcError as exc:
            last_err = str(exc)
            time.sleep(poll_s)
            continue
        if not last.get("in_world", False):
            return VerifyResult(VerifyOutcome.FAILED, "not in world", last)
        if not last.get("has_companion", False):
            return VerifyResult(VerifyOutcome.FAILED, "no companion", last)
        follow = str(last.get("follow_target") or "")
        # Empty follow_target means not following; any non-empty target after FOLLOW_ME
        # is the player (the game only sets player or move-marker).
        if follow and "VC_MoveMarker" not in follow:
            return VerifyResult(VerifyOutcome.SUCCEEDED, f"following '{follow}'", last)
        time.sleep(poll_s)
    return VerifyResult(
        VerifyOutcome.TIMEOUT,
        f"follow target not set within {budget_s:.1f}s ({last_err or 'still empty'})",
        last,
    )


def verify_come_here(
    client: GameClient,
    budget_s: float = 35.0,
    poll_s: float = 0.25,
    success_distance: float = 3.5,
) -> VerifyResult:
    """Success when the in-game move job reports succeeded (dist <= 3.5 m)."""
    deadline = _now() + budget_s
    last: dict[str, Any] | None = None
    while _now() < deadline:
        try:
            last = client.health()
        except IpcError:
            time.sleep(poll_s)
            continue
        if not last.get("in_world", False):
            return VerifyResult(VerifyOutcome.FAILED, "not in world", last)
        if not last.get("has_companion", False):
            return VerifyResult(VerifyOutcome.FAILED, "no companion", last)
        move_status = str(last.get("move_status") or "none")
        dist = last.get("move_distance")
        if move_status == "succeeded":
            return VerifyResult(
                VerifyOutcome.SUCCEEDED,
                f"move succeeded (dist={dist}, threshold={success_distance})",
                last,
            )
        if move_status == "timeout":
            return VerifyResult(VerifyOutcome.FAILED, f"game move timeout (dist={dist})", last)
        if move_status == "aborted":
            return VerifyResult(VerifyOutcome.FAILED, f"game move aborted (dist={dist})", last)
        time.sleep(poll_s)
    return VerifyResult(
        VerifyOutcome.TIMEOUT,
        f"move did not finish within {budget_s:.1f}s",
        last,
    )


def verify_stop(client: GameClient, budget_s: float = 3.0, poll_s: float = 0.25) -> VerifyResult:
    """Success when no follow target and no active move job (stable across 2 polls)."""
    deadline = _now() + budget_s
    last: dict[str, Any] | None = None
    stable = 0
    while _now() < deadline:
        try:
            last = client.health()
        except IpcError:
            time.sleep(poll_s)
            continue
        if not last.get("has_companion", False):
            return VerifyResult(VerifyOutcome.FAILED, "no companion", last)
        follow = str(last.get("follow_target") or "")
        move_status = str(last.get("move_status") or "none")
        if not follow and move_status in ("none", "succeeded", "aborted", "timeout"):
            stable += 1
            if stable >= 2:
                return VerifyResult(VerifyOutcome.SUCCEEDED, "follow cleared, not moving", last)
        else:
            stable = 0
        time.sleep(poll_s)
    detail = ""
    if last:
        detail = f" follow={last.get('follow_target')!r} move={last.get('move_status')!r}"
    return VerifyResult(VerifyOutcome.TIMEOUT, f"still active after {budget_s:.1f}s.{detail}", last)


def verify_status(client: GameClient, budget_s: float = 3.0, poll_s: float = 0.25) -> VerifyResult:
    """Success when real companion state can be read."""
    deadline = _now() + budget_s
    last_err = ""
    while _now() < deadline:
        try:
            health = client.health()
        except IpcError as exc:
            last_err = str(exc)
            time.sleep(poll_s)
            continue
        if not health.get("in_world", False):
            return VerifyResult(VerifyOutcome.FAILED, "not in world", health)
        if not health.get("has_companion", False):
            return VerifyResult(VerifyOutcome.FAILED, "no companion — use vc spawn", health)
        return VerifyResult(VerifyOutcome.SUCCEEDED, "state available", health)
    return VerifyResult(VerifyOutcome.TIMEOUT, last_err or "no state", None)


_VERIFIERS: dict[CommandKind, Callable[..., VerifyResult]] = {
    CommandKind.FOLLOW_ME: verify_follow_me,
    CommandKind.COME_HERE: verify_come_here,
    CommandKind.STOP: verify_stop,
    CommandKind.STATUS: verify_status,
}

_BUDGET_ATTR = {
    CommandKind.FOLLOW_ME: "follow_me_budget_s",
    CommandKind.COME_HERE: "come_here_budget_s",
    CommandKind.STOP: "stop_budget_s",
    CommandKind.STATUS: "status_budget_s",
}


def verify_command(
    kind: CommandKind,
    client: GameClient,
    budget_s: float,
    poll_s: float = 0.25,
) -> VerifyResult:
    fn = _VERIFIERS[kind]
    return fn(client, budget_s=budget_s, poll_s=poll_s)
