# Stage 5 — One controlled action (`SetFollowTarget`)

**Date:** 2026-10-27 · **Build:** 0.3.0 — deployed

## The one move method

`MonsterAI.SetFollowTarget(markerGO)` — public API confirmed in Stage 2.

- Marker: empty `GameObject("VC_MoveMarker")` at destination (not the player).
- Vanilla `BaseAI.Follow` stops at **&lt; 3 m** → success threshold **3.5 m**.
- `SetPatrolPoint(dest)` is **not used** (public API is no-arg only).

## Job state machine (in `ValheimGameAdapter`)

| Step | Behavior |
|---|---|
| BEFORE | snapshot pos, dest, dist, IsAlerted, IsSwimming, follow target |
| Issue | `SetFollowTarget(marker)` |
| Poll | every **0.5 s** — log `dist` |
| Success | dist ≤ **3.5 m** for **2** consecutive polls |
| Timeout | **30 s** (cap 60 s) + diagnostics (final dist, IsAlerted, IsSwimming, follow) |
| AFTER | snapshot |
| Cleanup | `SetFollowTarget(null)`, `StopMoving()`, clear ZDO `s_follow`, destroy marker |

On failure the job **reports** and does not silently switch methods (plan Stage 5 rule).

## How to test (user)

1. Restart Valheim, load local world.
2. ```
   vc spawn
   ```
3. Walk ~15–20 m away from the NPC.
4. ```
   vc move here
   ```
5. Watch console/log for `[Move] BEFORE` … `[Move] poll …` … `[Move] AFTER`.
6. ```
   vc status
   ```
   Expect `move-job : Succeeded` and AFTER dist ≤ 3.5 m.
7. Repeat with explicit coords:
   ```
   vc move <x> <y> <z>
   ```
8. Negative path: `vc move here` twice quickly (second aborts the first), or move far and wait for timeout.

## Success criterion (plan)

Command → **observable game-state change** (distance shrinks / position changes), not just a log line.

## Status

**LIVE VERIFIED 2026-10-27** (build 0.3.0):

```
[Move] BEFORE: pos=(-34.52, 35.08, -220.01) dest=(-20.4, 35.08, -224.3) distToDest=14.76 m
[Move] poll t=0.5s dist=14.06 m
...
[Move] poll t=9.2s dist=3.33 m
[Move] success poll 1/2
[Move] poll t=9.7s dist=2.94 m
[Move] success poll 2/2
[Move] AFTER (Succeeded: dist=2.94 <= 3.5): pos=(-23.34, 35.08, -224.17) dest=(-20.4, 35.08, -224.3) distToDest=2.94 m
[Move] SetFollowTarget(null)
```

| Metric | Value |
|---|---|
| Method | `MonsterAI.SetFollowTarget(marker)` only |
| Start dist | 14.76 m |
| End dist | **2.94 m** (≤ 3.5 m) |
| Elapsed | **9.7 s** (2 success polls) |
| Cleanup | `SetFollowTarget(null)` OK |

**Stage 5 criterion met: observable position change, honest before/after, single method.**

### Follow-up bug (same day)

World reload left persistent NPCs in the save while the adapter lost its `GameObject` ref.
Only the latest spawn accepted commands. **Fixed in 0.4.0:** ZDO tag `vc_companion`,
`vc adopt` / `vc cleanup`, auto-rebind on spawn/state/status/move.
