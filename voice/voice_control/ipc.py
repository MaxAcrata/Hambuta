"""HTTP client for the loopback game bridge. No arbitrary commands."""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any
from urllib.parse import urljoin

from .commands import CommandKind


class IpcError(Exception):
    """Transport or protocol failure talking to the game."""

    def __init__(self, message: str, kind: str = "error") -> None:
        super().__init__(message)
        self.kind = kind  # connection | timeout | protocol | http | rejected


@dataclass(frozen=True)
class CommandReply:
    accepted: bool
    status: str
    message: str
    job_id: int | None
    state: dict[str, Any] | None

    @property
    def ok(self) -> bool:
        return self.accepted and self.status in ("accepted", "succeeded", "running")


class GameClient:
    """Minimal client: POST /command (fixed enum) and GET /health."""

    def __init__(self, base_url: str, timeout: float = 5.0) -> None:
        self.base_url = base_url.rstrip("/") + "/"
        self.timeout = timeout

    def health(self) -> dict[str, Any]:
        return self._get("/health")

    def send_command(self, kind: CommandKind) -> CommandReply:
        """Only the four enum tokens are ever sent — no coordinates, no free text."""
        payload = json.dumps({"cmd": kind.value}).encode("utf-8")
        try:
            req = urllib.request.Request(
                urljoin(self.base_url, "command"),
                data=payload,
                method="POST",
                headers={"Content-Type": "application/json", "Accept": "application/json"},
            )
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                status_code = resp.status
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode("utf-8", errors="replace")
            try:
                data = json.loads(raw) if raw else {}
            except json.JSONDecodeError:
                raise IpcError(f"HTTP {exc.code}: {raw[:200]}", "http") from exc
            return CommandReply(
                accepted=bool(data.get("accepted", False)),
                status=str(data.get("status", "error")),
                message=str(data.get("message", f"HTTP {exc.code}")),
                job_id=data.get("job_id"),
                state=data.get("state"),
            )
        except urllib.error.URLError as exc:
            reason = getattr(exc, "reason", exc)
            kind = "timeout" if isinstance(reason, TimeoutError) else "connection"
            raise IpcError(f"cannot reach game bridge at {self.base_url}: {reason}", kind) from exc
        except TimeoutError as exc:
            raise IpcError(f"timeout talking to {self.base_url}", "timeout") from exc

        try:
            data = json.loads(raw) if raw else {}
        except json.JSONDecodeError as exc:
            raise IpcError(f"malformed JSON from game (HTTP {status_code})", "protocol") from exc

        return CommandReply(
            accepted=bool(data.get("accepted", False)),
            status=str(data.get("status", "error")),
            message=str(data.get("message", "")),
            job_id=data.get("job_id"),
            state=data.get("state"),
        )

    def _get(self, path: str) -> dict[str, Any]:
        try:
            req = urllib.request.Request(
                urljoin(self.base_url, path.lstrip("/")),
                method="GET",
                headers={"Accept": "application/json"},
            )
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
        except urllib.error.HTTPError as exc:
            raise IpcError(f"HTTP {exc.code} on {path}", "http") from exc
        except urllib.error.URLError as exc:
            reason = getattr(exc, "reason", exc)
            kind = "timeout" if isinstance(reason, TimeoutError) else "connection"
            raise IpcError(f"cannot reach game bridge at {self.base_url}: {reason}", kind) from exc
        except TimeoutError as exc:
            raise IpcError(f"timeout talking to {self.base_url}", "timeout") from exc

        try:
            data = json.loads(raw) if raw else {}
        except json.JSONDecodeError as exc:
            raise IpcError("malformed JSON from game", "protocol") from exc
        if not isinstance(data, dict):
            raise IpcError("unexpected JSON root from game", "protocol")
        return data
