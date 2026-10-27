# Stage 6 — Prototype acceptance

**Date:** 2026-10-27 · **Final build:** `ValheimCompanion 0.4.1`

## Checklist (plan acceptance 8/8)

| # | Item | Evidence |
|---|---|---|
| 1 | Builds without errors | `dotnet build -c Release` 0 err / 0 warn (net48) |
| 2 | BepInEx loads without critical exceptions | `LogOutput.log`: `Chainloader startup complete (2 loaded, 0 skipped, 0 failed)` |
| 3 | NPC appears and is identifiable | `Skeleton_Friendly` at known coords, ZDOID `1150485139:718918`, name `$enemy_skeleton_summoned` |
| 4 | Logs show real coords and parameters | `vc state` 10-field block + `vc scan` |
| 5 | Mod initiates the chosen action | `[Move] SetFollowTarget(marker)` |
| 6 | State change confirms action | 14.76 m → **2.94 m** in 9.7 s (Succeeded) |
| 7 | Errors and timeouts handled | double-spawn refused; timeout path with diagnostics; cleanup on abort |
| 8 | Instructions allow repeating on Windows | `README.md` “Repeat the test” |

## Stage log excerpts

See per-stage reports:

- `Stage1_Environment.md` — deps + install
- `Stage2_Candidates.md` — API signatures + candidate table
- `Stage3_MinimalMod.md` — clean load
- `Stage4_NpcDiagnostics.md` — spawn/state live
- `Stage5_Move.md` — move proof + rebind fix

## Scope held (plan priorities)

1. Haldor/Hildir **rejected** with named blocker (`Trader.Interact`); Skeleton_Friendly is a documented compromise.
2. Creation method = Instantiate prefab + component verify + `MakeTame()` (signatures confirmed, not name-guesses).
3. **One** move method (`SetFollowTarget`); `MoveTo` driver not implemented.
4. MiMo adapter = design-only (`ICompanionBrain` + README section).
5. Build/diag commands documented (TFM net48, pack HarmonyX, `vc` via Jötunn).

## Result

**Reproducible prototype** with **confirmed** capabilities only (table above).  
Next iteration (out of scope): MoveTo driver, CustomCreature humanoid, MiMo brain wiring.
