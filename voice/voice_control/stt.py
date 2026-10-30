"""Vosk wrapper — local Russian recognition, offline after model download."""

from __future__ import annotations

import json
import logging
from pathlib import Path

log = logging.getLogger("voice.stt")


class SttError(RuntimeError):
    pass


class VoskRecognizer:
    def __init__(self, model_path: Path, sample_rate: int = 16000) -> None:
        self.model_path = Path(model_path)
        self.sample_rate = sample_rate
        self._model = None
        self._vosk = None

    def load(self) -> None:
        if not self.model_path.is_dir():
            raise SttError(
                f"Vosk model not found: {self.model_path}. "
                "Run: python scripts/download_model.py (needs internet once)."
            )
        try:
            import vosk
        except Exception as exc:
            raise SttError(f"vosk package unavailable ({exc}). pip install -r requirements.txt") from exc

        vosk.SetLogLevel(-1)
        self._vosk = vosk
        self._model = vosk.Model(str(self.model_path))
        log.info("Vosk model loaded from %s", self.model_path)

    def transcribe_pcm(self, pcm: bytes) -> str:
        """pcm: 16-bit mono little-endian at self.sample_rate."""
        if self._model is None:
            raise SttError("model not loaded")
        rec = self._vosk.KaldiRecognizer(self._model, self.sample_rate)
        rec.AcceptWaveform(pcm)
        result = json.loads(rec.FinalResult())
        return str(result.get("text", "")).strip()
