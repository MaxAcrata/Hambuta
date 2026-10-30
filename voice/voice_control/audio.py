"""Microphone capture via sounddevice (16 kHz mono int16)."""

from __future__ import annotations

import logging
import queue
from typing import Any

log = logging.getLogger("voice.audio")


def list_input_devices() -> list[dict[str, Any]]:
    try:
        import sounddevice as sd
    except Exception as exc:  # pragma: no cover
        raise RuntimeError(f"sounddevice unavailable ({exc})") from exc
    devices = sd.query_devices()
    out = []
    for i, d in enumerate(devices):
        if d.get("max_input_channels", 0) > 0:
            out.append(
                {
                    "index": i,
                    "name": d.get("name", "?"),
                    "max_input_channels": d.get("max_input_channels"),
                    "default_samplerate": d.get("default_samplerate"),
                }
            )
    return out


class Microphone:
    """Start/stop capture of raw PCM into a queue of bytes blocks."""

    def __init__(self, device: int = -1, samplerate: int = 16000, blocksize: int = 4000) -> None:
        self.device = device
        self.samplerate = samplerate
        self.blocksize = blocksize
        self._q: "queue.Queue[bytes]" = queue.Queue()
        self._stream = None

    def start(self) -> None:
        if self._stream is not None:
            return
        import sounddevice as sd
        import numpy as np

        def _callback(indata, frames, time_info, status):  # noqa: ANN001
            if status:
                log.warning("mic status: %s", status)
            # indata is float32; convert to int16 bytes for Vosk
            pcm = (np.asarray(indata) * 32767.0).astype(np.int16).tobytes()
            self._q.put(pcm)

        self._stream = sd.InputStream(
            device=self.device if self.device >= 0 else None,
            channels=1,
            samplerate=self.samplerate,
            blocksize=self.blocksize,
            dtype="float32",
            callback=_callback,
        )
        self._stream.start()
        log.info("mic open device=%s rate=%s", self.device, self.samplerate)

    def stop(self) -> None:
        if self._stream is None:
            return
        stream = self._stream
        self._stream = None
        try:
            stream.stop()
            stream.close()
        except Exception:
            log.exception("mic close")
        # drain
        while not self._q.empty():
            try:
                self._q.get_nowait()
            except queue.Empty:
                break

    def read_block(self, timeout: float = 0.2) -> bytes | None:
        try:
            return self._q.get(timeout=timeout)
        except queue.Empty:
            return None

    @property
    def is_open(self) -> bool:
        return self._stream is not None
