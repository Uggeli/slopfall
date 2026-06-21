# Perceivable Atoms — Entities as a Bag of Atoms (Design)

**Date:** 2026-06-21
**Status:** approved (brainstorm), ready for implementation plan.
**Depends on:** memory core A1 (`AtomBag`, `AtomTypeId`, `Atom`, `Fixed` under `Assets/Sim/Memory/`).
**Part of:** memory integration (Phase B), **piece 1 of 5**. See
[`docs/wiki/sim-systems-and-memory-integration.md`](../../wiki/sim-systems-and-memory-integration.md).

## The idea

Give each entity a native **bag of atoms** representing its *perceivable surface* — what others
can sense about it. Perception then reads that bag directly. There is **no projection layer** that
maps "entity X → atoms Y" at perception time; that mapper is exactly the special-case rot we want
to avoid. Instead, the system that *owns* a perceivable fact stamps its *own* atom onto the entity.

This is piece 1 of the memory integration: the memory core (A1–A5) expects percepts to be
`AtomBag`s, but the live sim's percepts are bare entity IDs. This spec gives entities the atom bags;
a **separate follow-on spec** wires those bags into the memory core (the `MemoryWriteSystem`).

## Scope

**In scope:** the perceivable-atom representation and how it is populated and kept current —
a `PerceivableRegistry`, a `PerceivableAtoms` catalog, the stamp/clear intents, the wiring in the
owning systems, and identity-atom seeding at spawn.

**Success criterion (testable without any memory wiring):** every entity carries a correct, current
`AtomBag` reflecting its perceivable state — identity atoms present from spawn, observable atoms
updating when the owning system's state changes.

**Out of scope (later specs):**
- Reading the bags into the memory core (`MemoryWriteSystem` calling `MemoryEncoder.Perceive`),
  arousal exposure, consolidation/sleep system, `OddSystem` read adaptation — Phase-B pieces 2–5.
- Clarity/distance degradation of perception (a far/dim entity perceived with a partial bag) — a
  `MemoryWriteSystem` concern.
- Graded (continuous) atoms — none exist in the current perceivable surface; added when a
  continuous perceivable does.

## Decisions locked (from the brainstorm)

1. **Atoms are the perceivable surface only.** Internal mechanics (coin, hunger, vitals,
   pathfinding, skill) stay in their typed registries; the memory math never touches them.
2. **One sole-writer `PerceivableRegistry`.** Owning systems publish stamp/clear intents; the
   registry is the only writer of the bag (CQRS sole-writer rule).
3. **Event-driven, on-change.** Identity atoms stamped once at spawn; observable atoms re-stamped
   by their owner only when that state changes — not recomputed every tick.
4. **Categoricals are presence atoms.** A categorical attribute becomes one *presence* atom per
   member (value `Fixed.One`), never a single value-encoded atom — so the memory core's L1 distance
   and variance gate stay meaningful by construction. Continuous attributes (none yet) become one
   graded atom.

## Architecture

Three units, all under `Assets/Sim/` (compiled into `Sim.Core`).

### `PerceivableAtoms` (the catalog — single id authority)

A static catalog of `AtomTypeId`s, organized by range so categories are separable and debuggable:

| Range | Category | Helper |
|---|---|---|
| 1000– | Kind | `Kind(EntityKind)` |
| 2000– | Role | `Role(ResidentRole)` |
| 3000– | Race | `Race(int raceId)` |
| 4000– | Activity | `Activity(ActivityKind)` |

Each helper returns `new AtomTypeId(Base + (int)enum)`. The `Base + (int)enum` offset is a **stable
convention**, not a per-value mapper: the owning system chooses *which* catalog atom to stamp, and
the catalog only guarantees collision-free ids. `Activity(ActivityKind.None)` maps to "no activity
atom" (the owner clears rather than stamps).

### `PerceivableRegistry` (the bag — sole writer)

- `Dictionary<EntityId, List<Atom>>` internally (a mutable working set per entity), exposed as a
  sorted `AtomBag` via a `Bag(EntityId)` read accessor (built on demand, or cached and rebuilt on
  change).
- Intents (following the established registry-intent pattern):
  - `StampAtomIntent { EntityId Entity; AtomTypeId Type; Fixed Value }` — add or replace the atom of
    that type.
  - `ClearAtomIntent { EntityId Entity; AtomTypeId Type }` — remove the atom of that type.
  - `ClearEntityIntent { EntityId Entity }` — drop the whole bag (on death/despawn).
- `Update(tick)` applies queued intents and keeps each entity's atoms unique-by-type and sorted
  (reusing the `AtomBag.Create` invariant: one atom per type, ascending `AtomTypeId`).

### Stamp wiring (in the owning systems)

| Atoms | Owner (source of truth) | When stamped |
|---|---|---|
| `Kind`, `Race` | `IdentityRegistry` (already holds `Kind`, `Race`) | once, at spawn |
| `Role` | `ResidencyRegistry` | at spawn; re-stamped if role changes |
| `Activity` | `BehaviorRegistry` (set by `ExecutionSystem`) | on activity change (clear old, stamp new) |

Spawn paths that create entities (`TownLoader`/`RegionLoader` seeding, `RepopulationSystem` births)
publish the identity `StampAtom` seed intents alongside creating the entity. `ExecutionSystem`
already detects activity changes; it additionally publishes `ClearAtom(Activity(old))` +
`StampAtom(Activity(new))`. `LifecycleSystem` (death/despawn) publishes `ClearEntity`.

## Vocabulary (small, complete for what exists today)

All **presence** atoms (value `Fixed.One`) — the entire current perceivable surface is categorical.

| Attribute | Source field | Atoms |
|---|---|---|
| Kind | `IdentityRegistry.Kind` (`EntityKind`) | `KindPerson`, `KindMonster`, … (one per `EntityKind`) |
| Role | `ResidencyRegistry` role | `Role(Resident)`, `Role(Keeper)` |
| Race | `IdentityRegistry.Race` (int; -1 = none) | `Race(raceId)` when `Race >= 0` |
| Activity | `BehaviorRegistry.Activity` (`ActivityKind`) | `Activity(kind)` for the current activity (none when `Idle`/`None`) |

No graded atoms in the starter set; the memory core's graded path is already unit-tested, so nothing
is lost. Graded atoms arrive when a continuous perceivable appears (e.g. visible wealth/condition).

## Data flow

```
spawn (seed/birth)  -> StampAtom(Kind), StampAtom(Race), StampAtom(Role)   [identity, once]
activity change     -> ClearAtom(Activity old), StampAtom(Activity new)    [ExecutionSystem]
role change (rare)  -> ClearAtom(Role old), StampAtom(Role new)            [ResidencySystem]
death / despawn     -> ClearEntity                                          [LifecycleSystem]
                                                                            |
PerceivableRegistry.Update applies intents -> per-entity sorted AtomBag     v
(future MemoryWriteSystem reads Bag(entity) directly — no projection)
```

## Determinism / CQRS notes

- `PerceivableRegistry` is the **sole writer**; many systems publish intents, the registry applies
  them — consistent with the engine's read-N/write-N+1 discipline.
- Intent application is order-independent per entity *except* stamp-vs-clear of the same type in one
  tick; resolve by **last-writer-by-publish-order is non-deterministic**, so the registry applies
  **clears before stamps** within a tick (a re-stamp = clear-old + stamp-new lands as stamp-new).
  Document this as the conflict rule.
- The bag stays sorted/unique-by-type, so reads are deterministic and binary-searchable (A1
  `AtomBag` invariant). No floats beyond `Fixed`.

## Testing

- **Unit (`PerceivableRegistry`):** publish stamp/clear/clear-entity intents → `Update` → assert the
  `Bag(entity)` sorted contents. Cover: stamp adds, re-stamp replaces value, clear removes, clear of
  absent type is a no-op, clear-entity empties, clear-before-stamp conflict rule.
- **Unit (`PerceivableAtoms`):** the range helpers return distinct, range-correct ids; no cross-
  category collisions; `Activity(None)` is the "no atom" sentinel.
- **Integration:** seed a small town (e.g. Gallotale), sample a person NPC → assert its bag has
  `KindPerson` + a role atom + a race atom; flip its `Activity` via `ExecutionSystem` → assert the
  activity atom swapped (old cleared, new present); a creature → `KindMonster`.

## Deferred / open

- The `Activity(None)`/`Idle` "no observable activity" choice (clear vs a neutral atom) — start with
  *clear* (no atom); revisit if recognition wants an explicit idle.
- Whether `Gender`/`Level`/`FactionId`/`Team` (also in `IdentityRegistry`) become perceivable atoms
  — deferred; YAGNI until the memory layer or a system actually reads them perceptually.
- Per-creature **species** atoms (beyond `KindMonster`) — deferred until monster recognition needs
  finer grain.
