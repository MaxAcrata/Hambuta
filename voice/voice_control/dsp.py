"""DSP for PTT audio before STT — cut game noise, keep speech.

Pipeline: high-pass -> trim edges -> spectral denoise (noise profile) -> adaptive gate.
Works with pure-Python fallbacks; uses numpy/noisereduce when installed.
"""

from __future__ import annotations

import logging
import math
import struct
from array import array

log = logging.getLogger("voice.dsp")


def pcm16_to_float(pcm: bytes) -> list[float]:
    n = len(pcm) // 2
    if n <= 0:
        return []
    ints = struct.unpack("<" + "h" * n, pcm[: n * 2])
    return [i / 32768.0 for i in ints]


def float_to_pcm16(samples: list[float]) -> bytes:
    out = array("h")
    for s in samples:
        v = int(max(-1.0, min(1.0, s)) * 32767.0)
        out.append(v)
    return out.tobytes()


def highpass_simple(samples: list[float], sample_rate: int, cutoff_hz: float = 200.0) -> list[float]:
    """One-pole high-pass: removes rumble / low-frequency game bass."""
    if not samples or sample_rate <= 0 or cutoff_hz <= 0:
        return samples
    rc = 1.0 / (2.0 * math.pi * cutoff_hz)
    dt = 1.0 / sample_rate
    alpha = rc / (rc + dt)
    out: list[float] = []
    prev_x = 0.0
    prev_y = 0.0
    for x in samples:
        y = alpha * (prev_y + x - prev_x)
        out.append(y)
        prev_x = x
        prev_y = y
    return out


def rms(samples: list[float]) -> float:
    if not samples:
        return 0.0
    acc = 0.0
    for s in samples:
        acc += s * s
    return math.sqrt(acc / len(samples))


def dbfs(rms_value: float) -> float:
    if rms_value <= 1e-12:
        return -120.0
    return 20.0 * math.log10(rms_value)


def estimate_noise_floor_db(samples: list[float], sample_rate: int, frame_s: float = 0.02) -> float:
    """Noise floor from the quietest 10% of frames (game SFX / room tone)."""
    if not samples:
        return -60.0
    frame = max(1, int(frame_s * sample_rate))
    frames = [samples[i : i + frame] for i in range(0, len(samples), frame) if len(samples[i : i + frame]) >= frame // 2]
    if not frames:
        return dbfs(rms(samples))
    levels = sorted(dbfs(rms(f)) for f in frames)
    k = max(1, len(levels) // 10)
    return sum(levels[:k]) / k


def adaptive_threshold_db(
    samples: list[float],
    sample_rate: int,
    configured_gate_db: float = -55.0,
    margin_db: float = 6.0,
    hard_max_db: float = -40.0,
    min_snr_db: float = 15.0,
) -> float:
    """Safe gate threshold — prefer NOT gating speech.

    Uses noise floor + margin only when a real quiet section exists and only up to
    hard_max_db (very quiet). Pure speech falls back to configured_gate_db.
    """
    if not samples:
        return configured_gate_db
    peak = max(abs(s) for s in samples)
    peak_db = dbfs(peak) if peak > 0 else -120.0
    floor_db = estimate_noise_floor_db(samples, sample_rate)
    if peak_db - floor_db < min_snr_db:
        return configured_gate_db
    t = floor_db + margin_db
    return max(configured_gate_db, min(t, hard_max_db))


def noise_gate(
    samples: list[float],
    threshold_db: float = -55.0,
    noise_floor_db: float | None = None,
    margin_db: float = 6.0,
    hard_max_db: float = -40.0,
) -> list[float]:
    """Zero samples below threshold. Adaptive mode never gates above hard_max_db.

    hard_max_db is intentionally very quiet so speech valleys are preserved.
    """
    if noise_floor_db is not None:
        threshold_db = max(threshold_db, min(noise_floor_db + margin_db, hard_max_db))
    thresh = 10.0 ** (threshold_db / 20.0)
    return [s if abs(s) >= thresh else 0.0 for s in samples]


def trim_silence(
    samples: list[float],
    sample_rate: int,
    threshold_db: float = -50.0,
    keep_pad_s: float = 0.12,
    min_keep_s: float = 0.25,
) -> list[float]:
    """Drop only near-silence / loud SFX edges. Conservative — do not clip words."""
    if not samples:
        return samples
    thresh = 10.0 ** (threshold_db / 20.0)
    pad = max(1, int(keep_pad_s * sample_rate))
    min_keep = max(1, int(min_keep_s * sample_rate))

    first = 0
    last = len(samples) - 1
    while first < last and abs(samples[first]) < thresh:
        first += 1
    while last > first and abs(samples[last]) < thresh:
        last -= 1

    start = max(0, first - pad)
    end = min(len(samples), last + 1 + pad)
    out = samples[start:end]
    if len(out) < min_keep:
        return samples
    return out


def extract_noise_profile(samples: list[float], sample_rate: int, want_s: float = 0.2) -> list[float] | None:
    """Pick the quietest contiguous window as a game-noise profile (no speech)."""
    if len(samples) < sample_rate // 10:
        return None
    win = max(1, int(want_s * sample_rate))
    if len(samples) <= win:
        return list(samples)

    best_i = 0
    best_db = 1e9
    step = max(1, win // 2)
    for i in range(0, len(samples) - win + 1, step):
        d = dbfs(rms(samples[i : i + win]))
        if d < best_db:
            best_db = d
            best_i = i
    return samples[best_i : best_i + win]


def reduce_noise(
    samples: list[float],
    sample_rate: int,
    noise_profile: list[float] | None = None,
    prop_decrease: float = 0.92,
    passes: int = 1,
) -> list[float]:
    """Spectral denoise. Uses a noise profile when available (much better vs game audio)."""
    if not samples:
        return samples
    try:
        import numpy as np
        import noisereduce as nr
    except Exception:
        log.debug("noisereduce/numpy not installed — skip spectral denoise")
        return samples

    y = np.asarray(samples, dtype=np.float32)
    try:
        use_profile = noise_profile is not None and len(noise_profile) >= 256
        y_noise = np.asarray(noise_profile, dtype=np.float32) if use_profile else None
        for i in range(max(1, passes)):
            if use_profile:
                y = nr.reduce_noise(
                    y=y,
                    sr=sample_rate,
                    y_noise=y_noise,
                    stationary=True,
                    prop_decrease=prop_decrease,
                    freq_mask_smooth_hz=800,
                    time_mask_smooth_ms=80,
                )
                # After first pass the profile is the residual noise.
                y_noise = np.asarray(
                    extract_noise_profile([float(x) for x in y], sample_rate) or noise_profile,
                    dtype=np.float32,
                )
            else:
                y = nr.reduce_noise(
                    y=y,
                    sr=sample_rate,
                    stationary=False,
                    prop_decrease=prop_decrease,
                )
        return [float(x) for x in y]
    except Exception:
        log.exception("reduce_noise failed — using raw audio")
        return samples


def process_for_stt(
    pcm: bytes,
    sample_rate: int,
    highpass_hz: float = 100.0,
    gate_db: float = -55.0,
    trim_db: float = -50.0,
    denoise: bool = True,
    gate_margin_db: float = 6.0,
    denoise_passes: int = 1,
) -> bytes:
    """PCM16 in -> cleaned PCM16 out.

    Defaults favour KEEPING SPEECH: soft gate/trim, stronger multi-pass denoise.
    """
    samples = pcm16_to_float(pcm)
    if not samples:
        return b""

    before_rms = rms(samples)

    samples = highpass_simple(samples, sample_rate, highpass_hz)
    samples = trim_silence(samples, sample_rate, threshold_db=trim_db)

    noise_profile = extract_noise_profile(samples, sample_rate) if denoise else None
    if denoise and noise_profile is not None:
        speech_peak = max((abs(s) for s in samples), default=0.0)
        noise_peak = max((abs(s) for s in noise_profile), default=0.0)
        if speech_peak > 0 and noise_peak > speech_peak * 0.5:
            noise_profile = None
    if denoise:
        samples = reduce_noise(
            samples,
            sample_rate,
            noise_profile=noise_profile,
            passes=denoise_passes,
        )

    gate_thr = adaptive_threshold_db(
        samples,
        sample_rate,
        configured_gate_db=gate_db,
        margin_db=gate_margin_db,
    )
    samples = noise_gate(samples, threshold_db=gate_thr)

    after_rms = rms(samples)
    log.debug(
        "dsp: before=%.1f dBFS after=%.1f dBFS gate_thr=%.1f dBFS profile=%s",
        dbfs(before_rms),
        dbfs(after_rms),
        gate_thr,
        "yes" if noise_profile else "no",
    )
    return float_to_pcm16(samples)
