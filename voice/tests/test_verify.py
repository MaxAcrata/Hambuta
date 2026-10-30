import pytest

from voice_control.commands import CommandKind
from voice_control.ipc import GameClient, IpcError
from voice_control.verify import (
    verify_come_here,
    verify_command,
    verify_follow_me,
    verify_status,
    verify_stop,
)


class FakeClient:
    """Scripted /health responses. Raises IpcError when script is empty if raise_when_empty."""

    def __init__(self, frames, raise_when_empty=True):
        self.frames = list(frames)
        self.raise_when_empty = raise_when_empty
        self.calls = 0

    def health(self):
        self.calls += 1
        if not self.frames:
            if self.raise_when_empty:
                raise IpcError("game gone", "connection")
            return {"in_world": True, "has_companion": True, "move_status": "none",
                    "follow_target": "", "move_distance": 0.0}
        return self.frames.pop(0)


def test_follow_me_success():
    frames = [
        {"in_world": True, "has_companion": True, "follow_target": "", "move_status": "none"},
        {"in_world": True, "has_companion": True, "follow_target": "Player", "move_status": "none"},
    ]
    r = verify_follow_me(FakeClient(frames), budget_s=2.0, poll_s=0.01)
    assert r.ok
    assert "Player" in r.message


def test_follow_me_timeout():
    frames = [
        {"in_world": True, "has_companion": True, "follow_target": "", "move_status": "none"}
        for _ in range(5)
    ]
    r = verify_follow_me(FakeClient(frames), budget_s=0.05, poll_s=0.01)
    assert r.outcome == "timeout"


def test_follow_me_no_companion():
    r = verify_follow_me(
        FakeClient([{"in_world": True, "has_companion": False}]),
        budget_s=1.0,
        poll_s=0.01,
    )
    assert r.outcome == "failed"


def test_come_here_success():
    frames = [
        {"in_world": True, "has_companion": True, "move_status": "running", "move_distance": 10.0},
        {"in_world": True, "has_companion": True, "move_status": "succeeded", "move_distance": 2.9},
    ]
    r = verify_come_here(FakeClient(frames), budget_s=2.0, poll_s=0.01)
    assert r.ok


def test_come_here_game_timeout():
    frames = [
        {"in_world": True, "has_companion": True, "move_status": "timeout", "move_distance": 8.0}
    ]
    r = verify_come_here(FakeClient(frames), budget_s=2.0, poll_s=0.01)
    assert r.outcome == "failed"
    assert "timeout" in r.message


def test_come_here_budget_timeout():
    frames = [
        {"in_world": True, "has_companion": True, "move_status": "running", "move_distance": 12.0}
        for _ in range(20)
    ]
    r = verify_come_here(FakeClient(frames), budget_s=0.05, poll_s=0.01)
    assert r.outcome == "timeout"


def test_stop_success_two_stable_polls():
    frames = [
        {"in_world": True, "has_companion": True, "follow_target": "Player", "move_status": "none"},
        {"in_world": True, "has_companion": True, "follow_target": "", "move_status": "none"},
        {"in_world": True, "has_companion": True, "follow_target": "", "move_status": "none"},
    ]
    r = verify_stop(FakeClient(frames), budget_s=2.0, poll_s=0.01)
    assert r.ok


def test_stop_still_active():
    frames = [
        {"in_world": True, "has_companion": True, "follow_target": "Player", "move_status": "running"}
        for _ in range(10)
    ]
    r = verify_stop(FakeClient(frames), budget_s=0.08, poll_s=0.01)
    assert r.outcome == "timeout"


def test_status_success():
    r = verify_status(
        FakeClient([{"in_world": True, "has_companion": True}]),
        budget_s=1.0,
        poll_s=0.01,
    )
    assert r.ok


def test_status_no_companion():
    r = verify_status(
        FakeClient([{"in_world": True, "has_companion": False}]),
        budget_s=1.0,
        poll_s=0.01,
    )
    assert r.outcome == "failed"
    assert "vc spawn" in r.message


def test_connection_loss_is_error_not_crash():
    r = verify_follow_me(FakeClient([]), budget_s=0.05, poll_s=0.01)
    assert r.outcome in ("timeout", "failed", "error")


def test_verify_command_dispatch():
    frames = [{"in_world": True, "has_companion": True, "follow_target": "", "move_status": "none"},
              {"in_world": True, "has_companion": True, "follow_target": "Player", "move_status": "none"}]
    r = verify_command(CommandKind.FOLLOW_ME, FakeClient(frames), budget_s=2.0, poll_s=0.01)
    assert r.ok
