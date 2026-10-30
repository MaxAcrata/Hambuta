"""List input microphones (sounddevice)."""

from __future__ import annotations

import sys

from voice_control.audio import list_input_devices


def main() -> int:
    try:
        devices = list_input_devices()
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1
    if not devices:
        print("No input devices found.")
        return 2
    for d in devices:
        print(f"[{d['index']}] {d['name']}  (channels={d['max_input_channels']})")
    print("\nSet mic_device in config.toml to one of the indexes above (-1 = default).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
