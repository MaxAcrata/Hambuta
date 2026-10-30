"""Download vosk-model-small-ru-0.22 (one-time internet)."""

from __future__ import annotations

import argparse
import sys
import urllib.request
import zipfile
from pathlib import Path

# Official Alphacephei Vosk model index: https://alphacephei.com/vosk/models
MODEL_URL = "https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip"
VOICE_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DEST = VOICE_ROOT / "models"


def download(dest: Path, url: str = MODEL_URL) -> Path:
    dest.mkdir(parents=True, exist_ok=True)
    zip_path = dest / "vosk-model-small-ru-0.22.zip"
    model_dir = dest / "vosk-model-small-ru-0.22"
    if model_dir.is_dir() and any(model_dir.iterdir()):
        print(f"Model already present: {model_dir}")
        return model_dir

    print(f"Downloading {url}")
    print(f"  -> {zip_path} (about 45 MB, one-time)")
    urllib.request.urlretrieve(url, zip_path)

    print("Unzipping...")
    with zipfile.ZipFile(zip_path, "r") as zf:
        zf.extractall(dest)
    print(f"Done. Model dir: {model_dir}")
    print("You can now run the voice module offline.")
    return model_dir


def main() -> int:
    p = argparse.ArgumentParser(description="Download Vosk small Russian model")
    p.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    p.add_argument("--url", type=str, default=MODEL_URL)
    args = p.parse_args()
    try:
        download(args.dest, args.url)
    except Exception as exc:
        print(f"Download failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
