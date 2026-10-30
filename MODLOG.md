# Valheim Companion — MODLOG

All versions are BepInEx plugins under `Valheim\BepInEx\plugins\ValheimCompanion\`.

## 0.5.2 — 2026-10-31

- Companion XP: kills credit XP (scaled by victim max health, 10–100), persisted in ZDO (`vc_xp`/`vc_level`).
- Levels: +40 max health per level (full heal on level up); shown in `vc state` (11/12) and `vc xp`.
- Debug: `vc xp add N`.

## 0.5.1 — 2026-10-30

- **Clean start:** auto-purge of companion-like NPCs ~5 s after world load (once per load; skipped if a companion is already bound). No orphan bots at game launch — spawn only via `vc spawn`.
- `mic`: voice console window is closed automatically when the game exits.

## 0.5.0 — 2026-10-27 (voice prototype)

- **Optional** loopback IPC for the Python voice module (`Voice.IpcEnabled`, `Voice.IpcPort`, default 8765, `127.0.0.1` only).
- Adapter verbs (reuse existing `SetFollowTarget`/`StopMoving`/`RequestMove` — no second movement path):
  `RequestFollowPlayer`, `RequestMoveToPlayer`, `RequestStop`, `IsFollowingPlayer`, `IsStopped`.
- HTTP: `GET /health`, `POST /command` with fixed enum `FOLLOW_ME|COME_HERE|STOP|STATUS` (no coordinates).
- HTTP thread enqueues; game API runs on the Unity main thread (`Plugin.Update`).
- New `voice/` tree: Vosk PTT + whitelist matching + verify polling + DSP (high-pass, trim, gate, optional noisereduce).
- Console `mic` / `mic start|stop|status` and `mic.cmd` launchers.
- Unit tests: `voice/tests` (58 tests). Game tests are manual.

## 0.4.1 — 2026-10-27

- Fix: pre-tag companions (spawned before 0.4.0) were invisible to rebind/cleanup.
- Legacy match: tamed + `Faction.Players` + `Skeleton_Friendly`/`Skeleton` name + `MonsterAI`.
- Fallback scan via `FindObjectsByType` when `Character.GetAllCharacters()` is empty.
- New: `vc scan` — dump every character (name, faction, tamed, tag, match).
- `vc cleanup` (no args) destroys all companion-like NPCs; `vc cleanup keep` keeps bound.

## 0.4.0 — 2026-10-27

- Fix: world reload left persistent NPCs while adapter lost the GameObject ref.
- ZDO tag `vc_companion` = plugin GUID on spawn.
- Auto-rebind on `vc spawn` / `vc state` / `vc status` / `vc move`.
- New: `vc adopt`, `vc cleanup [keep]`.

## 0.3.0 — 2026-10-27 (Stage 5)

- **One** move method: `MonsterAI.SetFollowTarget(markerGO)`.
- Move-job state machine: BEFORE snapshot, 0.5 s polls, success 3.5 m × 2 polls, timeout 30 s, AFTER snapshot, cleanup (`StopMoving`, clear follow, destroy marker).
- InvariantCulture number formatting in logs.

## 0.2.0 — 2026-10-27 (Stage 4)

- `NpcBootstrap` spawn path (prefab → ZNetView → Character → MonsterAI → BaseAI → faction → Tameable → `MakeTame` → verify).
- Commands: `vc spawn [fallback]`, `vc state`, `vc status`, `vc move`, `vc inspect`, `vc despawn`.
- Core seam: `ICompanionEntity`, `IGameAdapter`, `ICompanionBrain`, `NullBrain`, `CompanionController`.

## 0.1.0 — 2026-10-27 (Stage 3)

- Minimal loadable plugin: `[BepInPlugin]`, `[BepInDependency(Jotunn)]`, Awake version log.
- Build: TFM `net48`, LangVersion 10, pack-shipped HarmonyX, non-publicized game DLLs.
