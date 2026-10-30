"""Config loading for the voice module (TOML, no secrets)."""

from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path

if sys.version_info >= (3, 11):
    import tomllib
else:
    try:
        import tomllib  # type: ignore
    except ImportError:
        tomllib = None  # type: ignore

VOICE_ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class VoiceConfig:
    mic_device: int = -1
    sample_rate: int = 16000
    ptt_key: str = "left ctrl"
    model_path: Path = VOICE_ROOT / "models" / "vosk-model-small-ru-0.22"
    min_confidence: float = 0.0
    fuzzy_min_ratio: float = 0.90
    base_url: str = "http://127.0.0.1:8765"
    http_timeout: float = 5.0
    follow_me_budget_s: float = 5.0
    come_here_budget_s: float = 35.0
    stop_budget_s: float = 3.0
    status_budget_s: float = 3.0
    poll_interval_s: float = 0.25
    highpass_hz: float = 100.0
    gate_db: float = -55.0
    trim_db: float = -50.0
    denoise: bool = True


def _read_toml(path: Path) -> dict:
    if tomllib is None:
        raise RuntimeError("tomllib unavailable — use Python 3.11+ or install tomli")
    with path.open("rb") as f:
        return tomllib.load(f)


def load_config(path: Path | None = None) -> VoiceConfig:
    """Load voice/config.toml if present, else defaults from VoiceConfig."""
    cfg_path = path or (VOICE_ROOT / "config.toml")
    if not cfg_path.is_file():
        return VoiceConfig()

    data = _read_toml(cfg_path)
    audio = data.get("audio", {})
    dsp = audio.get("dsp", {}) if isinstance(audio, dict) else {}
    ptt = data.get("ptt", {})
    stt = data.get("stt", {})
    match = data.get("match", {})
    ipc = data.get("ipc", {})
    verify = data.get("verify", {})

    model_raw = stt.get("model_path", str(VoiceConfig.model_path))
    model_path = Path(model_raw)
    if not model_path.is_absolute():
        model_path = VOICE_ROOT / model_path

    return VoiceConfig(
        mic_device=int(audio.get("mic_device", -1)),
        sample_rate=int(audio.get("sample_rate", 16000)),
        ptt_key=str(ptt.get("ptt_key", "left ctrl")),
        model_path=model_path,
        min_confidence=float(stt.get("min_confidence", 0.0)),
        fuzzy_min_ratio=float(match.get("fuzzy_min_ratio", 0.90)),
        base_url=str(ipc.get("base_url", "http://127.0.0.1:8765")).rstrip("/"),
        http_timeout=float(ipc.get("http_timeout", 5.0)),
        follow_me_budget_s=float(verify.get("follow_me_budget_s", 5.0)),
        come_here_budget_s=float(verify.get("come_here_budget_s", 35.0)),
        stop_budget_s=float(verify.get("stop_budget_s", 3.0)),
        status_budget_s=float(verify.get("status_budget_s", 3.0)),
        poll_interval_s=float(verify.get("poll_interval_s", 0.25)),
        highpass_hz=float(dsp.get("highpass_hz", 100.0)),
        gate_db=float(dsp.get("gate_db", -55.0)),
        trim_db=float(dsp.get("trim_db", -50.0)),
        denoise=bool(dsp.get("denoise", True)),
    )
