# Stage 3 — Minimal loadable mod

**Date:** 2026-10-27

## Done (on this machine)

| Step | Result |
|---|---|
| Project `ValheimCompanion\` | `D:\Projects\Valheim bot\ValheimCompanion\` |
| TFM | `net48` + `Microsoft.NETFramework.ReferenceAssemblies` (local targeting pack is 4.8.1 only; plan allows `net48` when verified) |
| LangVersion | 10 |
| References | BepInEx, 0Harmony (pack HarmonyX), Jotunn, assembly_valheim, assembly_utils, UnityEngine + CoreModule/InputLegacyModule/PhysicsModule — all `Private=false` |
| `Plugin.cs` | `[BepInPlugin]`, `[BepInDependency(Jotunn.Main.ModGuid)]`, Awake logs versions + Jötunn prefab events |
| Build | `dotnet build -c Release` → **0 errors, 0 warnings** |
| Deploy | `ValheimCompanion.dll` → `Valheim\BepInEx\plugins\ValheimCompanion\` (auto after build) |

## Critical criterion — needs game launch

Exit criteria (log): `[Message: ValheimCompanion] Awake ...`; no unhandled exception from our assembly; Jötunn OK.

**Status: build + deploy complete. Live load NOT yet verified.**

### How to verify (user)

1. Launch Valheim via Steam (BepInEx doorstop is already configured).
2. If F5 does not open console, add Steam launch option `-console`.
3. After reaching main menu (or any world), open:
   `D:\Steam\steamapps\common\Valheim\BepInEx\LogOutput.log`
4. Expect lines like:
   ```
   [Message: ValheimCompanion] ValheimCompanion 0.1.0 Awake
   [Message: ValheimCompanion]   Jotunn: 2.30.x
   [Message: ValheimCompanion] ValheimCompanion: OnVanillaPrefabsAvailable
   [Message: ValheimCompanion] ValheimCompanion: Awake complete (Stage 3 load probe)
   ```
5. No `Error`/`Exception` from `ValheimCompanion`.

If the chainloader never starts: doorstop target `BepInEx\core\BepInEx.Preloader.dll` must exist (it does as of this stage).
