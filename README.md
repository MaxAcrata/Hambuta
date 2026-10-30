# Valheim Companion

Minimal Windows BepInEx (Jötunn) prototype: spawn an NPC companion, read real state, issue **one** move action and verify a real state change. Game-agnostic seam for a future MiMo brain — **no AI in this prototype**.

- **Plan:** [`Valheim_Companion_Plan_v4.md`](Valheim_Companion_Plan_v4.md)
- **Commands (console + voice):** [`COMMANDS.md`](COMMANDS.md)
- **Working tree:** `D:\Projects\Valheim bot\`
- **Game:** `D:\Steam\steamapps\common\Valheim` (Steam 892970, buildid 25909357)
- **Versions:** BepInExPack_Valheim **5.4.2351**, Jötunn **2.30.2**, HarmonyX **2.9.0**, Unity **6000.0.75f1**
- **Changelog:** [`MODLOG.md`](MODLOG.md)

## Build & deploy

```powershell
cd "D:\Projects\Valheim bot\ValheimCompanion"
dotnet build -c Release
# auto-copies ValheimCompanion.dll -> Valheim\BepInEx\plugins\ValheimCompanion\
```

Requirements: .NET SDK 8.x, .NET Framework 4.8 targeting pack (via `Microsoft.NETFramework.ReferenceAssemblies`), game + BepInEx + Jötunn installed.

## Console

1. Steam launch option **`-console`** (otherwise F5 does not open).
2. `vc *` commands work after the plugin loads (`IsCheat = false` — no `devcommands` needed).
3. Vanilla cross-checks (need `devcommands` first): `pos`, `printcreatures`, `find`.

| Command | Effect |
|---|---|
| `vc spawn` | spawn primary `Skeleton_Friendly` (or re-bind existing) |
| `vc spawn fallback` | spawn `Skeleton` |
| `vc state` | 10-field state block |
| `vc status` | move-job + bound + tagged count |
| `vc move here` | move to player (the **one** method) |
| `vc move x y z` | move to coordinates |
| `vc inspect [prefab]` | component inventory |
| `vc scan` | dump all Characters + match flags |
| `vc adopt` | re-bind to nearest companion |
| `vc cleanup [keep]` | destroy companion-like NPCs |
| `vc despawn` | destroy bound companion |
| `vc help` | usage |

## Confirmed vs needs Valheim

| Confirmed (this machine, 2026-10-27) | Not claimed |
|---|---|
| Build 0 errors (TFM net48) | Multiplayer / dedicated server |
| BepInEx clean load (2 plugins, 0 failed) | Persistence across client restart (clean start 0.5.1: leftovers purged on world load) |
| `Skeleton_Friendly` spawn + `MakeTame` → `IsTamed=True`, `Faction=Players` | Haldor/Hildir as companions (rejected: `Trader.Interact`) |
| Real ZDOID, health 400/400, coords match player distance | `MoveTo` driver (protected; intentionally unused) |
| `SetFollowTarget` move: 14.76 m → 2.94 m in 9.7 s | CustomCreature path (reserve, unused) |
| Double-spawn refused; despawn works | |
| World-reload rebind + cleanup of legacy orphans | |

## Companion candidate (Stage 2/4)

| Role | Prefab | Evidence |
|---|---|---|
| **Primary** | `Skeleton_Friendly` | Humanoid + MonsterAI + Tameable (native); spawn+tame+follow live OK |
| Fallback | `Skeleton` | Humanoid + MonsterAI, no Tameable |
| Rejected | `Haldor`, `Hildir` | `Trader` Interactable conflict |
| Note | | Skeleton look is a **compromise**, not a human companion |

## Move method (Stage 5)

Only `MonsterAI.SetFollowTarget(markerGO)`:

- Vanilla `BaseAI.Follow` stops at **&lt; 3 m** → success **3.5 m** × 2 polls (0.5 s)
- Timeout **30 s** with BEFORE/AFTER diagnostics
- Cleanup: `StopMoving()`, `SetFollowTarget(null)`, clear ZDO `s_follow`, destroy marker
- `SetPatrolPoint(dest)` is **not** public API (no-arg only) — not used
- `MoveTo` is **protected** — not used in this prototype

## Layout

```
ValheimCompanion/
  Plugin.cs
  Core/                    # game-agnostic seam (no Valheim types)
    ICompanionEntity.cs    # CompanionEntity DTO
    IGameAdapter.cs        # Spawn / GetState / RequestMove / Tick
    ICompanionBrain.cs     # BrainDecision DTO + interface only
    NullBrain.cs
    CompanionController.cs
  Adapters/Valheim/        # ONLY place with Valheim types
    NpcBootstrap.cs
    ValheimGameAdapter.cs
  Commands/
    VcCommand.cs
docs/stage-reports/        # Stage 1–5 evidence
```

## Voice control (optional, Russian PTT)

In game console (F5): **`mic`** → hold **CTRL** and speak. Full guide: [`COMMANDS.md`](COMMANDS.md).

| Say | Action |
|---|---|
| «Иди за мной» | follow the player |
| «Иди сюда» | move to player position |
| «Стой» | stop and clear follow |
| «Статус» | print real companion state |

Details / DSP: [`voice/README_VOICE.md`](voice/README_VOICE.md), [`COMMANDS.md`](COMMANDS.md) §4.

## How to attach MiMo later (design only — not implemented)

1. Implement `ICompanionBrain.DecideAsync(CompanionEntity state)` → return `BrainDecision`.
2. Map `BrainDecision.Action` onto `IGameAdapter` (`RequestMove`, `SpawnCompanion`, …).
3. Provider: `xiaomi/mimo-v2.6-flash` via OpenAI-compatible `POST {base}/v1/chat/completions`.
4. Config keys reserved (unused): `AiBaseUrl`, `AiModel`, `AiApiKeyEnv`.
5. **Never hardcode API keys** — read from `auth.json` / env.

No HTTP, prompts, personality, or memory in this prototype.

## Acceptance checklist (plan)

- [x] 1. The mod builds without errors.
- [x] 2. BepInEx loads the mod without critical exceptions.
- [x] 3. NPC appears in the world and is identifiable.
- [x] 4. Logs show real NPC coordinates and parameters.
- [x] 5. The mod initiates the chosen action.
- [x] 6. State change confirms the action completed.
- [x] 7. Errors and timeouts are handled.
- [x] 8. The instructions allow repeating the test on Windows.

## Repeat the test

1. Install Valheim (Steam 892970) + BepInExPack_Valheim 5.4.2351 + Jötunn 2.30.2.
2. `dotnet build -c Release` in `ValheimCompanion\`.
3. Launch Valheim with `-console`, load a **local** world (F5).
4. `vc spawn` → `vc state` → walk 15 m → `vc move here` → `vc status`.
5. Expect `move-job : Succeeded` and AFTER dist ≤ 3.5 m in `LogOutput.log`.
