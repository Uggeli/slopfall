# Kind-driven sprites + yaw facing + activity animations — design

**Date:** 2026-06-20
**Status:** Active — the milestone after guards-as-gatekeepers. Makes town3d render actors with kind-appropriate Daggerfall sprites, face them by the simulation's yaw, and play walk/idle/attack animations driven by the snapshot's activity.
**Depends on:** the sprite pipeline (`SpritePerson` + `/asset/spritesheet|spritemeta|civilians` + town3d billboard renderer) and the guards milestone (guards exist as `CivilianNPC` + `EmploymentData.PublicOwner`). Reference: `docs/daggerfall-sprite-and-flat-archives.md`.

Today every moving actor is drawn with a **civilian** sprite chosen by hashing the entity id — `kind`, `yaw`, and `activity` are sent in the snapshot but ignored. So monsters look like villagers, guards look like villagers, facing is guessed from velocity, and nobody animates beyond walk/idle. This milestone uses the data already in the snapshot.

---

## 1. The Daggerfall animation model (grounded in DFTFU source + real data)

Every mobile TEXTURE archive uses one **universal record layout** (confirmed against archives 255 Rat, 262 Orc, 270 Skeleton, 399 City Watch — all 20 records; from `EnemyBasics.cs` / `MobilePersonBillboard.cs` in the DFTFU source at `/home/uggeli/oddthings/slopfall/Assets/Scripts/`):

| Records | State | Directions |
| --- | --- | --- |
| 0–4 | Walk / Move | S, SW, W, NW, N (NE/E/SE = mirror of 3/2/1) |
| 5–9 | Primary attack | same 5-angle + mirror |
| 10–14 | Hurt | same |
| 15–19 | Idle | same |
| 20+ | Ranged attack 1/2 | if present (not used this pass) |

**5-angle → 8-direction:** records 0–4 are facing S/SW/W/NW/N; NE/E/SE reuse records 3/2/1 horizontally flipped (`FlipLeftRight`).

**Frame rates (FPS):** enemy walk 6, attack 10, hurt 4, idle 4; people walk 4, idle 1. Real frame counts vary by archive (e.g. Rat walk = 8 frames, Orc/Skeleton walk = 4; attack = 6 for all three) — so the client must read per-record frame counts from `SpriteMeta`, never hardcode them.

**Two categories** (the only per-actor difference):
- **Civilian people** (archives 381–456): walk `0–4`, idle **record 5**, **no attack**.
- **Combat mobiles** (monsters AND the City Watch guard 399): walk `0–4`, attack **records 5–9**, idle **records 15–19**.

> **Guard-attack note:** DFTFU's *peaceful-NPC* billboard code wires only walk+idle for people. But archive 399 physically contains records 5–9 (5 frames each) — these are the City Watch's enemy/combat attack frames (the archive serves both the town billboard and the combat guard). We treat the guard with the **combat profile** so guards visibly swing when intercepting. If records 5–9 render wrong for 399, the fallback is trivial: drop the guard's attack state to idle (one client conditional). Monsters' attack frames (5–9) are verified-good.

---

## 2. Render-kind in the snapshot (server)

The snapshot row is `[id, x, z, activity, phase, yaw, kind, groundY]`, built in `WorldRunner.Build` from `AgentRow { Id, X, Z, Yaw, Activity, Phase, Kind }`. Today `Kind = (int)Identity.Kind` and the client ignores it.

Repurpose the `kind` slot to carry a **render-kind** (the client's only use of this slot is sprite selection, which is what we're adding):

- **Monster** — `Identity.Kind == EntityKind.EnemyMonster`.
- **Guard** — `Identity.Kind == EntityKind.CivilianNPC` AND `Employment.TryGet(id) && !PublicOwner.IsNone`.
- **Civilian** — otherwise.

`WorldRunner.Build` already reads `Identity`; it has `_world` in scope, so `Employment` is available for the guard check. Define a small `RenderKind { Civilian = 0, Guard = 1, Monster = 2 }` and emit it. (Confirm no other consumer reads the raw `EntityKind` from the row; the inspect panel keys off `id`. If a consumer exists, add a dedicated field instead of repurposing.)

## 3. Surfacing creature attacks (server)

Guards are agents with `BehaviorData`, so a guard intercepting already reports `Activity == ActivityKind.Attack` — no work needed.

Creatures (`CreatureRegistry`) have **no** `BehaviorData`, so their snapshot `Activity` is always `0`. To animate a monster's bite, `WorldRunner.Build` derives an activity for creatures: **`Attack` for a brief window right after a bite**, else leave it unset (the client derives walk/idle from movement). The bite sets `CreatureData.NextAttackTick = tick + cooldown` (in `CreatureSystem`); the window is computed by comparing `NextAttackTick` to the current tick against a short anim window (e.g. ~1–2 s of ticks). Exact tick math + the constant exposure are an implementation detail for the plan; the **contract** is: *a creature reports `Activity = Attack` for a short window after biting.*

## 4. Sprite pools (server)

`AssetService` exposes three pools via one endpoint **`/asset/spritepools`** → `{ "civilian": [...], "guard": [399], "monster": [...] }`:
- **civilian** — the existing `SpritePerson.CivilianArchives` (24 archives).
- **guard** — `[399]` (City Watch).
- **monster** — a curated pool of **grounded** creatures that use the standard walk/attack/idle layout cleanly: `255` Rat, `257` Spriggan, `259` Grizzly Bear, `260` Sabertooth, `261` Spider, `262` Orc, `263` Centaur, `270` Skeleton, `272` Zombie, `274` Mummy. (Excludes flyers and special-animation creatures — imp/bat/harpy, ghost/wraith, slaughterfish, seducer — whose record layout or motion differs.)

The client picks within a pool by `hash32(id) % pool.length` (as today). **Future hook:** when the sim later specifies a concrete creature type on `CreatureData`, the monster archive will be chosen by that type instead of an id-hash — the pool endpoint stays, the client's selection key changes. Out of scope now.

The existing `/asset/civilians` endpoint may stay (back-compat) or be folded into `/asset/spritepools`; the plan picks one. The client switches to the pooled form.

## 5. Pack all animation records (server)

`SpritePerson.Build` currently caps at `MaxRows = 6` (records 0–5) and the client treats row 5 as idle — correct only for civilians. For combat mobiles, idle is 15–19 and attack is 5–9, so Build must pack those records.

Change Build to pack records `0 .. min(RecordCount, 20)` (all walk/attack/hurt/idle rows), so a packed sheet row index == source record index. `SpriteMeta.Frames[]` grows to one entry per packed record; cell size stays `max` across packed records (Build already measures this); world size still from record 0. Build remains archive-agnostic and defensive (its per-frame try/skip already handles missing frames). The sheet is taller but cached. Civilians whose archives have few records pack few rows; combat archives pack ~20.

## 6. Client rendering (town3d.html)

1. **Pools:** fetch `/asset/spritepools`; select pool by render-kind from the snapshot row; pick archive by `hash32(id) % pool.length`. `getSheet(archive)` is unchanged (works on any archive).
2. **Facing from yaw:** compute the 8-direction index from the snapshot `yaw` (camera-relative, same math the camera-locked billboard uses), replacing the velocity-derived facing. Keep velocity as a fallback when `yaw` is unavailable/zero. Map direction → record offset 0–4 with horizontal flip for NE/E/SE (the universal mirror).
3. **State from activity + movement:**
   - **Attack** — if `activity == Attack` and the actor is **combat** category → attack state, base record `5`; play at attack FPS for the attack frames. (Civilian category has no attack → ignore; fall through.)
   - **Walk** — else if moving (speed above the existing threshold) → walk state, base record `0`, walk FPS.
   - **Idle** — else → idle state: civilian base record `5` (single), combat base record `15` (directional).
   - Packed row = `baseRecord + dirIndex` (with mirror flip for NE/E/SE); frame = `floor(timer * stateFPS) % meta.Frames[row]`.
4. **Category** (civilian vs combat) is derived from render-kind: Civilian → civilian profile; Guard/Monster → combat profile. FPS constants (walk/attack/idle) live as client constants per the §1 table.

## 7. Components / files

| File | Change |
| --- | --- |
| `Headless/Sim.Web/WorldRunner.cs` | compute render-kind (Identity + Employment); derive creature attack-window activity; emit both in `AgentRow` |
| `Headless/Sim.AssetExport/SpritePerson.cs` | pack all records (0..min(RecordCount,20)); `SpriteMeta.Frames[]` per record; monster + guard pool definitions |
| `Headless/Sim.AssetExport/AssetService.cs` | expose civilian/guard/monster pools |
| `Headless/Sim.Web/Program.cs` | `/asset/spritepools` endpoint |
| `Headless/Sim.Web/wwwroot/town3d.html` | pool-by-kind selection; yaw facing; activity→state→record; per-state FPS; mirror |

## 8. Verification

- **Determinism unaffected:** the server changes are read-only snapshot *projections* (render-kind, creature activity) and asset packing — they do not touch sim state. `--engineworld Daggerfall Gallotale 20000` must still print `DETERMINISTIC — parallel == serial OK`.
- **Headless structural checks:** `/asset/spritepools` returns the three pools; `SpritePerson.Build` succeeds (non-empty sheet, `Frames[]` populated) for civilian, guard (399), and each monster archive (255/262/270/…).
- **Human browser eyeball (the rendered result — cannot be verified headlessly):** run `Sim.Web` on Gallotale; confirm monsters render as varied creatures, face the direction they move (yaw), and play a bite when attacking a civilian; guards render as the City Watch and swing when intercepting a monster (or idle if 399's attack frames look wrong → apply the documented fallback); civilians look and animate as before.
- No unit tests (`Sim.Tests` stale; project policy). Verify via the above.

## 9. Out of scope (future)

- Hurt/death animations (records 10–14) and ranged-attack records (20+).
- Sim-specified creature type → fixed monster archive (the pool endpoint already anticipates it).
- Climate/season clothing variants for NPC sprites.
