# Voice control (Russian, push-to-talk)

Optional **local** voice module for Valheim Companion. Separate from the C# mod — deleting this folder does not affect the game mod.

- Speech: **Vosk** `vosk-model-small-ru-0.22` (offline after one-time download)
- Push-to-talk: hold a key (default **Left Ctrl**), speak, release
- Commands: **only 4**, matched by explicit rules (no LLM)
- Transport: `http://127.0.0.1:8765` (loopback only, fixed enum)

## 1. Install (Windows)

```powershell
cd "D:\Projects\Valheim bot\voice"
C:\Users\Admin\AppData\Local\Programs\Python\Python312\python.exe -m venv .venv
.venv\Scripts\python.exe -m pip install -r requirements.txt
.venv\Scripts\python.exe -m pip install pytest        # for unit tests only
```

Python 3.11+ (3.12 recommended). **Do not install globally.**

## 2. Download the model (one-time, needs internet)

```powershell
.venv\Scripts\python.exe scripts\download_model.py
```

Unpacks to `voice/models/vosk-model-small-ru-0.22` (~45 MB). After that everything is offline.

## 3. Configure

```powershell
copy config.example.toml config.toml
.venv\Scripts\python.exe scripts\list_mics.py
```

Edit `config.toml`:

| Key | Meaning | Default |
|---|---|---|
| `audio.mic_device` | index from `list_mics` | `-1` (default) |
| `ptt.ptt_key` | push-to-talk key | `left ctrl` |
| `stt.model_path` | Vosk model dir | `models/vosk-model-small-ru-0.22` |
| `ipc.base_url` | game bridge | `http://127.0.0.1:8765` |

If the game eats **Left Ctrl** (sneak), switch `ptt_key` to `"f13"` or `"scroll lock"`.

## 4. Run

1. Start **Valheim** (mod 0.5.0+) and load a **local** world.
2. In game console (F5): `vc spawn` — voice never creates NPCs.
3. Start the voice module:

```powershell
cd "D:\Projects\Valheim bot\voice"
.venv\Scripts\python.exe run.py
```

4. Hold **Left Ctrl**, say a command, release.

### Commands (only these four)

| Say (ru) | Action | Verified by |
|---|---|---|
| «Иди за мной» | player becomes follow target | `/health.follow_target` ≠ empty (not marker) |
| «Иди сюда» | move to **player position** (resolved in game) | move job `succeeded` (dist ≤ 3.5 m) |
| «Стой» | clear follow + stop | no follow target, no move job (2 polls) |
| «Статус» | print real companion state | state block from game object |

Anything else → «Повторите, пожалуйста» and **no game action**.

## 5. Diagnostics

| What | How |
|---|---|
| Game bridge | `curl http://127.0.0.1:8765/health` |
| Companion in game | `vc status`, `vc state`, `vc scan` |
| Voice CLI without mic | `.venv\Scripts\python.exe run.py --text "стой"` |
| Unit tests | `.venv\Scripts\python.exe -m pytest -q` |
| Mic list | `.venv\Scripts\python.exe scripts\list_mics.py` |

Error replies are plain text (no TTS). Exit codes of `run.py --text`: `0` ok, `2` empty, `3` ambiguous, `4` unknown, `5` game unreachable, `6` rejected, `7` verify failed.

## 6. Unit tests vs game tests

| Kind | Command | Runs where |
|---|---|---|
| **Automated** | `pytest -q` | no game, no mic (normalize, whitelist, IPC mocks, verify timeouts) |
| **Game (manual)** | checklist below | real Valheim + mic |

```powershell
.venv\Scripts\python.exe -m pytest -q -m "not game"
```

### Manual in-game checklist

- [ ] Mod loads (`LogOutput.log` shows `ValheimCompanion 0.5.0` and `[IPC] listening on http://127.0.0.1:8765/`).
- [ ] `vc spawn` then `vc status` shows a companion.
- [ ] Hold PTT, say «Статус» → state printed; game did not move.
- [ ] Say «Иди сюда» from ~15 m → companion walks to you; log shows `succeeded`.
- [ ] Say «Иди за мной» → follow target set; companion trails you.
- [ ] Say «Стой» → companion stops and drops follow.
- [ ] Say «открой инвентарь» / random noise → «Повторите…», **no** movement.
- [ ] Two fast phrases → second rejected as busy or queued safely.
- [ ] Close Valheim mid-session → voice prints connection error and keeps running.
- [ ] No companion (`vc despawn`) → «no companion — use vc spawn», no crash.

## 7. Remove the voice module (keep the game mod)

```powershell
# stop run.py, then optionally:
Remove-Item -Recurse -Force "D:\Projects\Valheim bot\voice\.venv"
Remove-Item -Recurse -Force "D:\Projects\Valheim bot\voice\models"
Remove-Item -Force "D:\Projects\Valheim bot\voice\config.toml"
```

Or delete the whole `voice\` folder. The C# mod keeps working; you can also set `Voice.IpcEnabled = false` in `BepInEx\config\com.valheimcompanion.prototype.cfg` to turn the bridge off without uninstalling anything.

## 8. Security notes

- IPC binds **127.0.0.1 only**; not reachable from the network.
- Protocol accepts only `FOLLOW_ME | COME_HERE | STOP | STATUS` — no coordinates, no console passthrough.
- No API keys, no cloud, no unknown executables.

## 9. Game noise (how to hear the player, not Valheim)

### Hear game noise in headphones when the mic opens?

That is Windows **microphone monitoring**, not DSP:

1. Sound → **Recording** → your mic → **Properties** → **Listen**
2. Uncheck **“Listen to this device”** → Apply

Also avoid Bluetooth **Hands-Free (HFP)** (noise + low quality); use stereo/A2DP or a wired mic.
Disable sidetone/monitor in the headset app.

### Software (already in the pipeline, tune in `config.toml` → `[audio.dsp]`):

| Stage | Effect |
|---|---|
| `highpass_hz = 100` | cuts bass/rumble without killing voice lows |
| `trim_db = -38` | drops silence/SFX at the edges so Vosk invents fewer words |
| `gate_db = -45` | zeros very quiet frames |
| `denoise = true` | spectral gating via **`noisereduce`** (install it: `pip install noisereduce`) |

Hardware / OS (usually more important than DSP):

1. **Use headphones** — speakers + mic is the main source of game audio bleed.
2. Prefer the **headset mic** (MOTIF / Bluetooth HFP) over a desk/Realtek mic near speakers.
3. Windows → Sound → your mic → **disable** “Listen to this device”.
4. Windows 11 → Sound → Mic → **Voice focus / noise suppression** (if available).
5. Lower in-game music/effects; voice command audio is easier to isolate.
6. Optional system-level: NVIDIA Broadcast / RTX Voice (GPU denoise) before the mic reaches Python.

If it still mishears: raise `trim_db` (e.g. `-30`), say commands closer to the mic, or use shorter phrases («Стой», «Статус»).

## 10. Limitations

- Low-level keyboard hooks may require elevation or **borderless windowed** mode in Valheim.
- Vosk small-ru can mishear accents; those cases are treated as unknown/ambiguous (never a wrong action).
- Left Ctrl may conflict with sneak — rebind in `config.toml`.
- Voice path is **not** marked verified in Valheim until the manual checklist above is done.
