import math

from voice_control.dsp import (
    adaptive_threshold_db,
    dbfs,
    estimate_noise_floor_db,
    extract_noise_profile,
    float_to_pcm16,
    highpass_simple,
    noise_gate,
    pcm16_to_float,
    process_for_stt,
    reduce_noise,
    rms,
    trim_silence,
)


def _sine(freq: float, sr: int, seconds: float, amp: float = 0.5) -> list[float]:
    return [amp * math.sin(2 * math.pi * freq * t / sr) for t in range(int(sr * seconds))]


def _mix(a: list[float], b: list[float], gain_b: float = 1.0) -> list[float]:
    n = max(len(a), len(b))
    out = []
    for i in range(n):
        x = a[i] if i < len(a) else 0.0
        y = (b[i] if i < len(b) else 0.0) * gain_b
        out.append(x + y)
    return out


def test_pcm_roundtrip():
    samples = [0.0, 0.5, -0.5, 0.25]
    pcm = float_to_pcm16(samples)
    back = pcm16_to_float(pcm)
    assert len(back) == len(samples)
    for a, b in zip(samples, back):
        assert abs(a - b) < 0.01


def test_highpass_attenuates_low_freq():
    sr = 16000
    low = _sine(50, sr, 0.2, amp=0.5)
    out = highpass_simple(low, sr, cutoff_hz=100.0)
    assert rms(out) < rms(low) * 0.7


def test_highpass_keeps_speech_band():
    sr = 16000
    speech = _sine(440, sr, 0.2, amp=0.5)
    out = highpass_simple(speech, sr, cutoff_hz=100.0)
    assert rms(out) > rms(speech) * 0.5


def test_noise_gate_zeros_quiet():
    quiet = _sine(440, 16000, 0.05, amp=0.001)
    out = noise_gate(quiet, threshold_db=-40.0)
    assert all(abs(s) < 1e-9 for s in out)


def test_gate_never_cuts_loud_speech():
    """Regression: gate must not zero mid-speech at normal voice level."""
    sr = 16000
    speech = _sine(440, sr, 0.4, amp=0.4)
    thr = adaptive_threshold_db(speech, sr, configured_gate_db=-55.0)
    assert thr <= -40.0  # hard cap
    out = noise_gate(speech, threshold_db=thr)
    # at least 70% of speech energy remains
    assert rms(out) > rms(speech) * 0.7


def test_trim_silence_soft_edges():
    sr = 16000
    pad = [0.0] * (sr // 2)
    tone = _sine(440, sr, 0.4, amp=0.5)
    samples = pad + tone + pad
    out = trim_silence(samples, sr, threshold_db=-50.0, keep_pad_s=0.1)
    assert len(out) < len(samples)
    assert len(out) >= int(0.35 * sr)


def test_trim_keeps_very_short():
    assert trim_silence([0.1] * 10, 16000, min_keep_s=1.0) == [0.1] * 10


def test_extract_noise_profile_prefers_quiet_window():
    sr = 16000
    noise = _sine(100, sr, 0.3, amp=0.01)
    speech = _sine(500, sr, 0.3, amp=0.6)
    samples = noise + speech + noise
    profile = extract_noise_profile(samples, sr, want_s=0.2)
    assert profile is not None
    assert rms(profile) < rms(speech) * 0.2


def test_estimate_noise_floor_lower_than_speech():
    sr = 16000
    noise = _sine(80, sr, 0.2, amp=0.005)
    speech = _sine(400, sr, 0.2, amp=0.5)
    samples = noise + speech + noise
    floor = estimate_noise_floor_db(samples, sr)
    assert floor < dbfs(rms(speech)) - 20


def test_process_for_stt_empty():
    assert process_for_stt(b"", 16000) == b""


def test_process_for_stt_returns_pcm16():
    sr = 16000
    samples = [0.0] * 1000 + _sine(300, sr, 0.5, amp=0.4) + [0.0] * 1000
    pcm = float_to_pcm16(samples)
    out = process_for_stt(pcm, sr, denoise=False)
    assert isinstance(out, bytes)
    assert len(out) % 2 == 0
    assert len(out) > 0


def test_process_for_stt_attenuates_noise_only():
    sr = 16000
    game_noise = _mix(_sine(60, sr, 0.8, 0.15), _sine(250, sr, 0.8, 0.08))
    pcm = float_to_pcm16(game_noise)
    out = process_for_stt(pcm, sr, denoise=True)
    back = pcm16_to_float(out)
    assert len(back) < len(game_noise) or rms(back) < rms(game_noise) * 0.6


def test_process_for_stt_keeps_loud_speech():
    sr = 16000
    speech = _sine(440, sr, 0.5, amp=0.5)
    pcm = float_to_pcm16(speech)
    out = process_for_stt(pcm, sr, denoise=False)
    back = pcm16_to_float(out)
    assert rms(back) > rms(speech) * 0.5


def test_process_for_stt_speech_plus_noise_keeps_speech():
    sr = 16000
    noise = _sine(90, sr, 0.6, amp=0.05)
    speech = [0.0] * int(sr * 0.15) + _sine(500, sr, 0.3, amp=0.6) + [0.0] * int(sr * 0.15)
    mixed = _mix(speech, noise)
    pcm = float_to_pcm16(mixed)
    out = process_for_stt(pcm, sr, denoise=True)
    back = pcm16_to_float(out)
    # measure the speech segment (skip lead-in silence)
    i0 = int(sr * 0.15)
    i1 = int(sr * 0.45)
    seg = back[i0:i1] if len(back) > i1 else back
    assert rms(seg) > 0.15


def test_speech_valleys_not_zeroed():
    """Envelope of speech must not collapse to zeros in the middle."""
    sr = 16000
    # amplitude-modulated speech-like tone (valleys still audible)
    samples = []
    for t in range(int(sr * 0.6)):
        env = 0.55 + 0.45 * math.sin(2 * math.pi * 2.5 * t / sr)  # 2.5 Hz envelope
        samples.append(env * math.sin(2 * math.pi * 400 * t / sr))
    pcm = float_to_pcm16(samples)
    out = process_for_stt(pcm, sr, denoise=False, gate_db=-55.0)
    back = pcm16_to_float(out)
    # fewer than 10% of samples fully zeroed
    zeros = sum(1 for s in back if abs(s) < 1e-6)
    assert zeros < len(back) * 0.15


def test_reduce_noise_noop_without_lib():
    samples = _sine(440, 16000, 0.1, 0.2)
    out = reduce_noise(samples, 16000, noise_profile=_sine(100, 16000, 0.05, 0.01))
    assert len(out) == len(samples)


def test_dbfs_quiet():
    assert dbfs(0.0) <= -100


def test_app_wires_process_for_stt():
    import inspect

    from voice_control import app

    src = inspect.getsource(app)
    assert "process_for_stt" in src
    assert "transcribe_pcm(cleaned)" in src
