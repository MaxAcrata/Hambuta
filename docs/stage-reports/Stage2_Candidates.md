# Stage 2 — NPC candidates & creation method (static analysis)

**Date:** 2026-10-27  
**Source of truth:** `ilspycmd` 9.0 decompile of local  
`D:\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll` (buildid 25909357)  
and `BepInEx\plugins\Jotunn\Jotunn.dll` (2.30.2).  
Decompiled C# dumps: `docs/decompiled/`.

**Scope note (plan rule):** static analysis narrows choices; it does **not** prove live spawn, networking, pathfinding, or controllability. Runtime checks are named explicitly. The word *works* is **not** used for any candidate.

---

## 2.1 API signatures (confirmed in this DLL)

| API | Actual signature | Visibility | Plan assumption | Verdict |
|---|---|---|---|---|
| MoveTo | `BaseAI.MoveTo(float dt, Vector3 point, float dist, bool run) → bool` | **protected** | same | **Confirmed** (not public — AccessTools if ever needed) |
| SetPatrolPoint | `BaseAI.SetPatrolPoint()` | public | `SetPatrolPoint(dest)` | **Mismatch:** public overload is **no-arg** only (pins *current* position). Private `SetPatrolPoint(Vector3)` exists — not callable without reflection |
| ResetPatrolPoint | `BaseAI.ResetPatrolPoint()` | public | — | Confirmed |
| StopMoving | `BaseAI.StopMoving()` | public | same | Confirmed (`m_character.SetMoveDir(Vector3.zero)`) |
| SetFollowTarget | `MonsterAI.SetFollowTarget(GameObject go)` | public | same | **Confirmed — primary move method** |
| GetFollowTarget | `MonsterAI.GetFollowTarget() → GameObject` | public | same | Confirmed |
| MakeTame | `MonsterAI.MakeTame()` | public | same | Confirmed → `Character.SetTamed(true)` + clear targets |
| SetTamed | `Character.SetTamed(bool tamed)` | public | — | Confirmed (RPC `RPC_SetTamed` → ZDO `s_tamed`) |
| Command | `Tameable.Command(Humanoid user, bool message = true)` | public | `Command(Humanoid, bool)` | **Confirmed** (not GameObject) |
| GetZDOID | `Character.GetZDOID() → ZDOID` | public | same | Confirmed |
| IsTamed | `Character.IsTamed() → bool` and `Tameable.IsTamed() → bool` | public | same | Confirmed |
| GetHealth / GetMaxHealth | `Character.GetHealth() / GetMaxHealth() → float` | public | same | Confirmed |
| GetBaseAI | `Character.GetBaseAI() → BaseAI` | public | `m_baseAI` field | Field `m_baseAI` is **protected**; use `GetBaseAI()` |
| GetFaction / m_faction | `Character.GetFaction()`, `public Faction m_faction` | public | same | Confirmed |
| Follow stop radius | `BaseAI.Follow`: `if (dist < 3f) StopMoving()` | protected | vanilla stop 3 m | **Confirmed** → success threshold **3.5 m** |
| Run threshold in Follow | `run = dist > 10f` | — | — | Confirmed |
| IsOnGround | `Character.IsOnGround()` | public | same | Confirmed |
| Swimming | `Character.IsSwimming()` | public | plan wrote `IsSwiming` | **Name is `IsSwimming`** |
| IsAlerted | `BaseAI.IsAlerted()` | public | same | Confirmed |
| ZNetView | `IsValid()`, `IsOwner()`, `GetZDO()`, `ClaimOwnership()`, `m_persistent` | public | same | Confirmed |
| ZDO tags | `ZDOVars.s_tamed`, `s_follow`, `s_patrol`, `s_patrolPoint`, `s_health`, `s_level` | static hashes | `s_tamed`, `s_follow` | Confirmed |
| ZDO Set/Get | `Set(string|int, bool/float/…)`, `GetBool(string|int, …)` | public | same | Confirmed |
| Trader interact | `Trader.Interact(Humanoid, bool hold, bool alt)` | public (Interactable) | merchant blocker | Confirmed — competing Interactable with `Tameable` |
| Faction enum | `Players, AnimalsVeg, ForestMonsters, Undead, Demon, MountainMonsters, SeaMonsters, PlainsMonsters, Boss, MistlandsMonsters, Dverger, PlayerSpawned, TrainingDummy, DeepNorth` | public | `Faction.Players` | Confirmed |

### Jötunn 2.30.2 (local `Jotunn.dll`)

| API | Signature | Notes |
|---|---|---|
| ConsoleCommand | abstract `Name`, `Help`; virtual `IsCheat/IsNetwork/OnlyServer/IsSecret`; **`Run(string[] args)`** and **`Run(string[] args, Terminal context)`** | **Both overloads exist** — plan's caution resolved: `Terminal` overload is present |
| CommandManager | `CommandManager.Instance.AddConsoleCommand(ConsoleCommand cmd)` | Register in `Plugin.Awake` |
| PrefabManager | `PrefabManager.Instance.GetPrefab(string name)`, static `PrefabManager.GetPrefab<T>(string)`, `CreateClonedPrefab`, `AddPrefab`, `RegisterToZNetScene` | Creation path candidate |
| CustomCreature | ctors `(GameObject, bool fixRef)`, `(string name, string basePrefabName, CreatureConfig)`, etc. | Full-control path; higher scope |
| Events | `PrefabManager.OnVanillaPrefabsAvailable`, `OnPrefabsRegistered` | Gate spawn until ready |

---

## 2.2 Per-candidate component inventory

**Runtime probe (`vc inspect`) — filled 2026-10-27 Stage 4:**

| Prefab | Components (live GetComponents) | Humanoid | MonsterAI | Tameable | Merchant |
|---|---|---|---|---|---|
| `Skeleton_Friendly` | Transform, CapsuleCollider, Rigidbody, **Humanoid**, ZNetView, ZSyncTransform, ZSyncAnimation, **MonsterAI**, VisEquipment, FootStep, **Tameable** | yes | yes | **yes (native)** | no |
| `Skeleton` | Transform, CapsuleCollider, Rigidbody, **Humanoid**, ZNetView, ZSyncTransform, ZSyncAnimation, **MonsterAI**, CharacterDrop, VisEquipment, FootStep | yes | yes | **no** (would need AddComponent) | no |

What static analysis *does* show:

| Candidate | Static + Stage 4 evidence | Missing runtime checks | Verdict |
|---|---|---|---|
| `Haldor` | `Trader.m_name = "Haldor"` literal in `Trader.cs` → **Trader present**. Trader implements `Interactable` | Interact disable? follow stick? | **rejected** — merchant `Interact` blocker |
| `Hildir` | Second trader in content data | same | **rejected** — same merchant class |
| `Skeleton` / `Skeleton_NoArcher` | Humanoid+MonsterAI confirmed; **no Tameable** | AddComponent\<Tameable\> safety; tame stick | **uncertain → fallback** |
| `Skeleton_Friendly` | **Humanoid + MonsterAI + Tameable (native)**. Spawn+tame live OK | follow/command live (Stage 5) | **primary — works so far** (spawn/tame) |
| `Goblin` / `GoblinBrute` | not inspected live | same as Skeleton | **uncertain** |
| Custom / Jötunn `CustomCreature` | API exists | scope cost | reserve |

**Honest summary:** no candidate is proven suitable yet. Human traders (Haldor/Hildir) are the *worst* static fit because of `Trader.Interact`. Skeleton-family is a **compromise**, not a human companion.

---

## 2.3 Creation & init sequence (to implement and prove in Stage 4)

Hypothesis based on confirmed APIs — each step logged, fail-fast with reason:

```
1. Preconditions
   - Player.m_localPlayer != null
   - PrefabManager.OnVanillaPrefabsAvailable / OnPrefabsRegistered already fired
   - prefab = PrefabManager.Instance.GetPrefab(cfg.PrimaryPrefab)  else FallbackPrefab
   - prefab != null && prefab.GetComponent<ZNetView>() != null

2. Instance
   - pos = player.transform.position + player.forward * 2f + Vector3.up
   - go = Object.Instantiate(prefab, pos, Quaternion.identity)   // or PrefabManager clone path
   - nview = go.GetComponent<ZNetView>()
   - if nview == null || !nview.IsValid() -> ERROR "no network identity"

3. Component init (order)
   a. character = go.GetComponent<Character>() / Humanoid     // required
   b. monsterAI = go.GetComponent<MonsterAI>()
      - if null and Humanoid exists -> AddComponent<MonsterAI>()  // ONLY if Stage 2/4 says safe
      - if still null -> REJECT candidate
   c. baseAI = character.GetBaseAI()                            // must be non-null
   d. character.m_faction = Character.Faction.Players           // verify side effects
   e. tameable = go.GetComponent<Tameable>()
      - if null -> AddComponent<Tameable>() only when Humanoid+MonsterAI exist
      - tameable.m_commandable = true
   f. tame (prefer in order):
      - monsterAI.MakeTame()            // public, confirmed
      - else Character.SetTamed(true)   // public, confirmed
      - else zdo.Set(ZDOVars.s_tamed, true) if nview.IsOwner()
   g. merchant neutralize (if Trader/Offerings present):
      - disable Interactable / reject candidate
   h. verify:
      - character.IsTamed() == true
      - character.GetFaction() == Faction.Players
      - tameable.m_commandable == true
      - character.GetZDOID() != ZDOID.None
```

**Failure policy:** log `step N failed: reason`; try fallback prefab once; both fail → "no viable candidate", do not fake an NPC.

---

## 2.4 Merchant / special-AI caveats

1. `Trader.Interact` and `Tameable.Interact` both implement `Interactable` — E-key may open shop instead of command.
2. Human traders are not designed as MonsterAI companions; AI swap may throw or despawn.
3. **Verdicts allowed:** `works` / `works with caveat (name it)` / `rejected (name the failing member)` — *after* Stage 4–5 live tests only.

---

## 2.5 Decision output (provisional until Stage 4)

| Role | Prefab | Why | Condition to keep |
|---|---|---|---|
| **Primary (provisional)** | `Skeleton_Friendly` | no merchant block in static type graph; minion-class hypothesis; closest to "already friendly" | Stage 4: Humanoid+MonsterAI+commandable confirmed |
| **Fallback (provisional)** | `Skeleton` (or `Skeleton_NoArcher`) | standard humanoid enemy; Tameable may be addable | Stage 4: Tameable add + tame sticks |
| **Rejected (static)** | `Haldor`, `Hildir` | `Trader.Interact` merchant blocker | reconsider only if interact can be safely disabled |
| **Reserve** | Jötunn `CustomCreature` | full control | if both skeletons fail Stage 4–5 |

**Move method (Stage 5):** `MonsterAI.SetFollowTarget(markerGO)` only.  
`SetPatrolPoint(dest)` is **not available as public API** (no-arg only) — do not use as silent fallback.

**Init sequence:** §2.3.

---

## 2.6 Confirmed vs deferred

| Confirmed in Stage 2 (static) | Deferred to Stage 4–5 (user runs game) |
|---|---|
| Signatures exist in this DLL (table 2.1) | Instantiate succeeds at runtime |
| `MoveTo` is protected; `SetFollowTarget` is public | AddComponent does not throw |
| `SetPatrolPoint` is no-arg only | Tame/faction/command actually stick |
| Follow stops at &lt; 3 m (success threshold 3.5 m) | NPC appears and moves |
| `Trader` is the Haldor blocker (type + Interactable) | Whether Interact can be disabled |
| Jötunn ConsoleCommand has `Terminal` overload | Command registration + output path live |
| ZDO tag names (`s_tamed`, `s_follow`, …) | ZDO writes persist under ownership |

---

## Exit criteria check

- [x] APIs and component requirements cited (table 2.1, decompiled dumps)
- [x] Unresolved runtime questions explicitly listed
- [x] Primary/fallback chosen only as **provisional**, pending Stage 4
- [x] Human traders rejected on named static evidence (`Trader.Interact`), not on appearance

**Stage 2 result:** creation proposal + suitability table ready.  
**Next:** Stage 3 — minimal loadable mod (deps are now installed).
