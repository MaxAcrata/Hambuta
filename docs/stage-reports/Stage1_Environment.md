# Stage 1 — Environment check

**Date:** 2026-10-27  
**Plan:** `Valheim_Companion_Plan_v4.md`  
**Working location:** `D:\Projects\Valheim bot\` (plan's `C:\Users\Фрол\ValheimCompanion\` overridden by user)

---

## Summary

| Item | Status | Detail |
|---|---|---|
| Valheim install | **FOUND** | `D:\Steam\steamapps\common\Valheim` |
| Valheim buildid | **FOUND** | `25909357` (Steam appid 892970) |
| BepInExPack_Valheim 5.4.2351 | **PARTIAL** | doorstop 4.4.0 present; `BepInEx\` core **missing** |
| Jötunn 2.30.2 | **MISSING** | no `Jotunn.dll` anywhere under install |
| .NET SDK 8.x | **MISSING** | `dotnet` not on PATH, not in standard install dirs |
| .NET Framework 4.6.2/4.7.2/4.8 targeting pack | **MISSING** | no Reference Assemblies tree |
| MSBuild / Visual Studio | **MISSING** | no MSBuild.exe found |
| `assembly_valheim.dll` | **FOUND** | `valheim_Data\Managed\assembly_valheim.dll` (2.5 MB, 2026-10-18) |
| `assembly_utils.dll` | **FOUND** | `valheim_Data\Managed\assembly_utils.dll` |
| UnityEngine modules | **FOUND** | CoreModule, InputLegacyModule, PhysicsModule, IMGUIModule, … |
| `BepInEx.dll` / `0Harmony.dll` | **MISSING** | ship with BepInExPack (not present) |
| `Jotunn.dll` | **MISSING** | ships with Jötunn mod |
| NuGet cache / ValheimGameLibs | **MISSING** | `~/.nuget/packages` empty / absent |
| `ilspycmd` / `ildasm` / Mono.Cecil tools | **MISSING** | — |
| Package managers | **winget available** | choco / scoop not installed |

---

## 1. Valheim

- **Path:** `D:\Steam\steamapps\common\Valheim`
- **Steam appid:** 892970
- **buildid:** `25909357` (from `D:\Steam\steamapps\appmanifest_892970.acf`)
- **LastUpdated:** 1792341149 (unix) ≈ 2026-10-18
- **SizeOnDisk:** ~4.5 GB
- **Mono:** `MonoBleedingEdge\` present (Unity Mono runtime)
- **Data:** `valheim_Data\Managed\` contains game + Unity assemblies

Key compile references present:

| Assembly | Path | Size |
|---|---|---|
| `assembly_valheim.dll` | `valheim_Data\Managed\` | 2 575 360 |
| `assembly_utils.dll` | `valheim_Data\Managed\` | 242 688 |
| `UnityEngine.dll` | `valheim_Data\Managed\` | 170 920 |
| `UnityEngine.CoreModule.dll` | `valheim_Data\Managed\` | 1 801 128 |
| `UnityEngine.InputLegacyModule.dll` | `valheim_Data\Managed\` | 42 416 |
| `UnityEngine.PhysicsModule.dll` | `valheim_Data\Managed\` | 182 696 |

---

## 2. BepInEx / Doorstop — PARTIAL install detected

The game root contains a **partial BepInExPack_Valheim 5.4.2351** drop:

| File | Present | Notes |
|---|---|---|
| `winhttp.dll` | yes | Doorstop loader |
| `doorstop_config.ini` | yes | `target_assembly=BepInEx\core\BepInEx.Preloader.dll` |
| `.doorstop_version` | yes | **4.4.0** (pack changelog mentions doorstop 4.5.0 for linux libs) |
| `doorstop_libs\` | yes (empty listing at root; `custom\libdoorstop_*` exist) | linux/mac dylibs in `custom\` |
| `changelog.txt` | yes | **BepInExPack_Valheim 5.4.2351** changelog (AzumattDev) |
| `start_game_bepinex.sh` / `start_server_bepinex.sh` | yes | linux start scripts |
| `BepInEx\core\BepInEx.Preloader.dll` | **NO** | target missing — doorstop currently points at nothing |
| `BepInEx\core\BepInEx.dll` | **NO** | — |
| `BepInEx\core\0Harmony.dll` (HarmonyX 2.9.0) | **NO** | — |
| `BepInEx\plugins\` | **NO** | — |

**Implication:** launching `valheim.exe` now runs Doorstop with a missing target (loader will no-op or error). Full BepInExPack install is required before Stage 3.

---

## 3. Jötunn — MISSING

- Target: **Jötunn 2.30.2**
- Expected path: `Valheim\BepInEx\plugins\Jotunn\Jotunn.dll`
- No `Jotunn.dll` anywhere under the install.

---

## 4. .NET / build toolchain — MISSING

| Tool | Status | Needed for |
|---|---|---|
| .NET SDK 8.x | missing | `dotnet build`, `dotnet tool install ilspycmd`, NuGet restore |
| .NET Framework 4.7.2 targeting pack | missing | TFM `net472` reference assemblies |
| MSBuild (VS Build Tools) | missing | alt build path |
| `ilspycmd` | missing | Stage 2 decompile |
| Mono.Cecil (NuGet) | missing | Stage 2 reflection metadata |

**Available:** `winget` (Windows Package Manager).

---

## Dependency checklist (install steps — awaiting approval)

> Plan rule: *no user-data changes without approval.* Nothing below has been installed.

### A. .NET SDK 8.x (required for build + Stage 2 tools)

```powershell
winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
# or: https://dotnet.microsoft.com/download/dotnet/8.0
```

Verify: `dotnet --list-sdks` shows `8.x`.

Optional but useful (TFM net472 reference assemblies without full VS):

```powershell
winget install Microsoft.DotNet.Framework.DeveloperPack_4 --accept-source-agreements --accept-package-agreements
# installs .NET Framework 4.x targeting pack
```

If targeting pack cannot be installed, fall back to TFM `net48` (plan allows `net48` if verified) or install **VS Build Tools** with `.NET Framework 4.7.2 targeting pack` / `4.8` workload.

### B. BepInExPack_Valheim 5.4.2351 (required for Stage 3+)

Source (Thunderstore, matches plan + local changelog):

- **Thunderstore:** `https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/` — version **5.4.2351**
- Extract so that `BepInEx\core\BepInEx.Preloader.dll` lands under `D:\Steam\steamapps\common\Valheim\BepInEx\`

Notes:
- Doorstop files are already present and configured; avoid overwriting user `doorstop_config.ini` if customized (current file is stock).
- Pack ships HarmonyX **2.9.0** as `0Harmony.dll` — use this one, do not NuGet a second Harmony.

### C. Jötunn 2.30.2 (required for Stage 3+)

- **Thunderstore:** `https://valheim.thunderstore.io/package/ValheimModding/Jotunn/` — version **2.30.2**
- Install to `Valheim\BepInEx\plugins\Jotunn\` (so `Jotunn.dll` is next to a thin plugin or as the package provides)

### D. Stage 2 analysis tools (after .NET SDK)

```powershell
dotnet tool install -g ilspycmd
dotnet new console -n RefDump   # or a small Cecil-based dumper project
dotnet add package Mono.Cecil
dotnet add package ValheimGameLibs   # optional, if publicized/unstripped refs needed offline
```

---

## Readiness verdict

| Gate | Ready? |
|---|---|
| Stage 1 exit (every missing item listed with version + source) | **DONE** (this report) |
| Stage 2 (static API / component analysis) | **BLOCKED** on tools (.NET SDK / Cecil / ilspycmd). Game DLLs are present. |
| Stage 3 (build + load) | **BLOCKED** on .NET SDK + BepInEx + Jötunn |
| Stage 4–5 (in-game) | **BLOCKED** on Stage 3 |

**Plan note updated:** the v4 plan claimed "Valheim **absent**, .NET SDK **absent**". On *this* machine (2026-10-27) **Valheim is present** at `D:\Steam\steamapps\common\Valheim` (buildid 25909357); .NET SDK remains absent.

---

## Proposed next actions (need user OK)

1. Install .NET SDK 8.x via winget.
2. Install .NET Framework 4.7.2 (or 4.8) targeting pack via winget / VS Build Tools.
3. Download + extract BepInExPack_Valheim 5.4.2351 into the game folder.
4. Download + extract Jötunn 2.30.2 into `BepInEx\plugins\Jotunn\`.
5. Then proceed to **Stage 2** (API signatures + candidate inventory) using `ilspycmd` / Mono.Cecil on the local `assembly_valheim.dll`.
