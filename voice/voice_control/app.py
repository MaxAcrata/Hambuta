"""Main entry: PTT -> Vosk -> whitelist match -> IPC -> verify -> report.

Never claims success because speech was recognized; only after game state confirms.
"""

from __future__ import annotations

import argparse
import logging
import sys
import time
from pathlib import Path

from .commands import MatchOutcome, match_command
from .config import load_config
from .ipc import GameClient, IpcError
from .verify import verify_command

log = logging.getLogger("voice.app")

# Human (Russian) replies. No TTS — printed to console/log.
REPLY_OK = {
    "FOLLOW_ME": "Понял: иду за вами.",
    "COME_HERE": "Понял: иду к вам.",
    "STOP": "Понял: стою.",
    "STATUS": "Состояние компаньона:",
}
REPLY_UNKNOWN = "Команда не распознана. Повторите, пожалуйста."
REPLY_AMBIGUOUS = "Команда неоднозначна. Повторите, пожалуйста."
REPLY_EMPTY = "Пустая команда. Повторите, пожалуйста."
REPLY_BUSY = "Предыдущая команда ещё выполняется."


def _setup_logging(verbose: bool) -> None:
    level = logging.DEBUG if verbose else logging.INFO
    logging.basicConfig(
        level=level,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )


def run_once_text(text: str, client: GameClient, cfg) -> int:
    """Process one already-transcribed phrase (used by --text and by tests)."""
    m = match_command(text, fuzzy_min_ratio=cfg.fuzzy_min_ratio)
    if m.outcome == MatchOutcome.EMPTY:
        print(REPLY_EMPTY)
        return 2
    if m.outcome == MatchOutcome.AMBIGUOUS:
        print(f"{REPLY_AMBIGUOUS} ({m.detail})")
        return 3
    if m.outcome == MatchOutcome.UNKNOWN:
        print(f"{REPLY_UNKNOWN} ({m.detail})")
        return 4

    assert m.command is not None
    kind = m.command
    print(f"{REPLY_OK[kind.value]} [{kind.value}]")

    try:
        reply = client.send_command(kind)
    except IpcError as exc:
        print(f"Связь с игрой недоступна: {exc}")
        return 5

    if not reply.accepted:
        print(f"Игра отклонила команду: {reply.message}")
        return 6

    budget = {
        "FOLLOW_ME": cfg.follow_me_budget_s,
        "COME_HERE": cfg.come_here_budget_s,
        "STOP": cfg.stop_budget_s,
        "STATUS": cfg.status_budget_s,
    }[kind.value]

    result = verify_command(kind, client, budget_s=budget, poll_s=cfg.poll_interval_s)

    if kind.value == "STATUS" and result.ok and result.health:
        h = result.health
        print(
            "  companion={0} follow={1} move={2} dist={3}".format(
                h.get("has_companion"),
                h.get("follow_target") or "(none)",
                h.get("move_status"),
                h.get("distance_to_player"),
            )
        )

    if result.ok:
        print(f"Выполнено: {result.message}")
        return 0

    print(f"Не выполнено ({result.outcome}): {result.message}")
    return 7


def run_ptt(cfg, verbose: bool) -> int:
    from .audio import Microphone, list_input_devices
    from .ptt import PushToTalk
    from .stt import VoskRecognizer

    client = GameClient(cfg.base_url, timeout=cfg.http_timeout)

    print("Проверка связи с игрой...")
    try:
        h = client.health()
        print(
            f"  ok={h.get('ok')} in_world={h.get('in_world')} "
            f"has_companion={h.get('has_companion')} version={h.get('version')}"
        )
        if not h.get("has_companion"):
            print("  Внимание: компаньон не найден. В игре: `vc spawn`.")
    except IpcError as exc:
        print(f"  Игра недоступна ({exc}). Запустите Valheim с модом 0.5.0+.")

    try:
        devices = list_input_devices()
        print(f"Микрофоны ({len(devices)}):")
        for d in devices:
            mark = " <- selected" if d["index"] == cfg.mic_device else ""
            print(f"  [{d['index']}] {d['name']}{mark}")
        if cfg.mic_device < 0:
            print("  (используется устройство по умолчанию)")
    except Exception as exc:
        print(f"Не удалось получить список микрофонов: {exc}")
        return 10

    stt = VoskRecognizer(cfg.model_path, sample_rate=cfg.sample_rate)
    try:
        stt.load()
    except Exception as exc:
        print(f"Ошибка модели Vosk: {exc}")
        return 11

    mic = Microphone(device=cfg.mic_device, samplerate=cfg.sample_rate)

    ptt = PushToTalk(cfg.ptt_key)
    state = {"recording": False, "pcm": bytearray()}

    def on_press():
        # Open the mic ONLY while the key is held (true push-to-talk).
        if state["recording"]:
            return
        try:
            mic.start()
        except Exception as exc:
            print(f"Ошибка микрофона: {exc}")
            return
        state["recording"] = True
        state["pcm"] = bytearray()
        print(f"[{time.strftime('%H:%M:%S')}] Микрофон ВКЛ — говорите...")

    def on_release():
        if not state["recording"]:
            return
        state["recording"] = False
        # Close the mic immediately so it is not "on" while idle.
        try:
            mic.stop()
        except Exception:
            log.exception("mic stop")
        pcm = bytes(state["pcm"])
        print(f"[{time.strftime('%H:%M:%S')}] Микрофон ВЫКЛ ({len(pcm)} байт)")
        if len(pcm) < max(cfg.sample_rate // 4, 2000):  # < ~0.25 s is noise
            print(REPLY_EMPTY)
            return
        try:
            from .dsp import dbfs, pcm16_to_float, process_for_stt, rms

            raw_rms = rms(pcm16_to_float(pcm))
            cleaned = process_for_stt(
                pcm,
                cfg.sample_rate,
                highpass_hz=cfg.highpass_hz,
                gate_db=cfg.gate_db,
                trim_db=cfg.trim_db,
                denoise=cfg.denoise,
            )
            clean_rms = rms(pcm16_to_float(cleaned)) if cleaned else 0.0
            print(
                f"DSP: {dbfs(raw_rms):.1f} dBFS -> {dbfs(clean_rms):.1f} dBFS "
                f"(hp={cfg.highpass_hz:.0f}Hz gate={cfg.gate_db:.0f}dB denoise={cfg.denoise})"
            )
        except Exception:
            log.exception("dsp")
            cleaned = pcm
        try:
            text = stt.transcribe_pcm(cleaned)
        except Exception as exc:
            print(f"Ошибка распознавания: {exc}")
            return
        print(f"Распознано: {text!r}")
        try:
            run_once_text(text, client, cfg)
        except Exception:
            log.exception("command pipeline")
            print("Внутренняя ошибка обработки команды.")

    ptt.bind(on_press, on_release)
    try:
        ptt.start()
    except Exception as exc:
        print(f"Ошибка клавиши PTT: {exc}")
        return 13

    print(f"PTT: зажмите [{cfg.ptt_key}] и говорите (микрофон включается только на время нажатия).")
    print("Ctrl+C — выход.")
    try:
        while True:
            if state["recording"]:
                block = mic.read_block(timeout=0.05)
                if block:
                    state["pcm"].extend(block)
            else:
                time.sleep(0.05)
    except KeyboardInterrupt:
        print("\nВыход.")
    finally:
        ptt.stop()
        try:
            mic.stop()
        except Exception:
            pass
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Russian PTT voice control for Valheim Companion")
    parser.add_argument("--config", type=Path, default=None, help="path to config.toml")
    parser.add_argument("--text", type=str, default=None, help="skip audio; process this phrase")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)

    _setup_logging(args.verbose)
    cfg = load_config(args.config)

    if args.text is not None:
        client = GameClient(cfg.base_url, timeout=cfg.http_timeout)
        return run_once_text(args.text, client, cfg)

    return run_ptt(cfg, args.verbose)


if __name__ == "__main__":
    sys.exit(main())
