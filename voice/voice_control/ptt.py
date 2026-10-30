"""Push-to-talk keyboard hook (hold = record, release = finalize)."""

from __future__ import annotations

import logging
from typing import Callable

log = logging.getLogger("voice.ptt")


class PushToTalk:
    """Wraps the `keyboard` package. Does not suppress the key.

    Notes:
    - Some games swallow low-level hooks; run elevated or use borderless windowed.
    - If PTT never fires, try another key in config.toml (e.g. f13).
    """

    def __init__(self, key: str = "left ctrl") -> None:
        self.key = key
        self._pressed = False
        self._on_press: Callable[[], None] | None = None
        self._on_release: Callable[[], None] | None = None
        self._hooked = False

    def bind(self, on_press: Callable[[], None], on_release: Callable[[], None]) -> None:
        self._on_press = on_press
        self._on_release = on_release

    def start(self) -> None:
        try:
            import keyboard
        except Exception as exc:  # pragma: no cover - import env
            raise RuntimeError(
                f"keyboard package unavailable ({exc}). Install voice/requirements.txt"
            ) from exc

        def _handler(event):
            # keyboard calls this for down and up; use event.event_type
            try:
                if event.event_type == "down":
                    if not self._pressed:
                        self._pressed = True
                        log.debug("PTT down (%s)", self.key)
                        if self._on_press:
                            self._on_press()
                elif event.event_type == "up":
                    if self._pressed:
                        self._pressed = False
                        log.debug("PTT up (%s)", self.key)
                        if self._on_release:
                            self._on_release()
            except Exception:
                log.exception("PTT handler error")

        try:
            keyboard.hook_key(self.key, _handler)
            self._hooked = True
            log.info("PTT hooked on %r (hold to talk)", self.key)
        except Exception as exc:
            raise RuntimeError(
                f"cannot hook PTT key {self.key!r}: {exc}. "
                "Try running elevated or switch ptt_key in config.toml (e.g. f13)."
            ) from exc

    def stop(self) -> None:
        if not self._hooked:
            return
        try:
            import keyboard

            keyboard.unhook_all()
        except Exception:
            pass
        self._hooked = False

    @property
    def is_pressed(self) -> bool:
        return self._pressed
