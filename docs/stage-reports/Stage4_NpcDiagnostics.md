# Stage 4 — Create and diagnose NPC (LIVE VERIFIED)

**Date:** 2026-10-27 · **Build:** 0.2.0 → live log excerpt from user

## Component inventory (`vc inspect`) — Stage 2 gap closed

| Prefab | Humanoid | MonsterAI | Tameable | Other notable |
|---|---|---|---|---|
| **Skeleton_Friendly** | yes | yes | **yes (native)** | ZNetView, ZSyncTransform, ZSyncAnimation, VisEquipment, FootStep, CapsuleCollider, Rigidbody |
| **Skeleton** | yes | yes | **no** | + CharacterDrop |

## Spawn path (live)

```
[NpcBootstrap] resolve prefab 'Skeleton_Friendly' (fallback=False)
[NpcBootstrap] instantiated 'Skeleton_Friendly' at (-25.74, 36.08, -220.99)
[NpcBootstrap] faction set to Players
[NpcBootstrap] MonsterAI.MakeTame() called
[NpcBootstrap] verify: IsTamed=True, Faction=Players, commandable=True, ZDOID=1150485139:718918
[NpcBootstrap] SUCCESS prefab=Skeleton_Friendly ZDOID=1150485139:718918
[vc] spawn OK
```

No `AddComponent` needed — Tameable and MonsterAI already on prefab.

## `vc state` (live)

| # | Field | Value |
|---|---|---|
| 1 | prefab | Skeleton_Friendly |
| 2 | name | `$enemy_skeleton_summoned` |
| 3 | ZDOID | 1150485139:718918 |
| 4 | position | (-25.74, 36.08, -220.99) then (-25.74, 35.08, -220.99) |
| 5 | health | 400 / 400 |
| 6 | IsTamed | True |
| 7 | s_follow | (none) |
| 8 | OnGround/Swim | True / False |
| 9 | IsValid/Owner | True / True |
| 10 | dist to player | 2.24 m → 2 m |
| | faction | Players |
| | IsDead | False |

NPC settled on ground (Y 36.08 → 35.08). Later `vc status` pos (-26.61, 35.08, -213.79) — idle drift observed (expected for untold AI).

## Failure paths (live)

```
[Adapter] spawn refused: already have a living companion
[vc] spawn FAILED: already have a living companion
[vc] companion despawned (if any)
```

## Exit criteria

- [x] player sees NPC
- [x] `vc state` coords match reality (2 m to player)
- [x] re-spawn and failure paths behave

**Stage 4: DONE.** Primary candidate `Skeleton_Friendly` confirmed suitable for spawn+tame.
