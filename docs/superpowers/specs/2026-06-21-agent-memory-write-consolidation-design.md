# Agent Memory: Write + Consolidation (Design)

**Date:** 2026-06-21
**Status:** approved-pending-review (brainstorm), ready for plan.
**Depends on:** memory core A1–A5 ([[memory-core-phase-a]], `Assets/Sim/Memory/`), perceivable atoms ([[perceivable-atoms]]).
**Part of:** memory integration (Phase B), **piece 2** — the `remember` stage of the bunny-doc loop
(`docs/what_is_a_bunny.md`). Map: `docs/wiki/sim-systems-and-memory-integration.md`.

## The idea

Make agent memory **live**: agents perceive the entities around them (their perceivable atom bags),
form memories, and during sleep consolidate those memories into learned categories. This is the
`remember` stage of the loop — it closes the bottom so that, later, `interpret`/`afford` have
something learned to read. It does **not** yet change behavior (the planner doesn't read the new
memory until the ODD-repoint piece); it makes memory accumulate and learn, observable via metrics.

**The demonstrable arc:** day 1, every percept is novel → verbatim records pile up; during sleep,
consolidation MINTs categories from clustered records ("a Breton keeper, usually Working"); day 2,
those percepts *recognize* → low surprise → small deltas. Novelty falls, categories rise — agents
learn the kinds of people around them, with **zero hand-authored categories.**

## Scope decisions (locked)

1. **v1 learns categories, not individuals.** Records are keyed by the **perceived entity's id**
   (per-individual dossier, perfect "which one" memory); recognition + categories run on the
   categorical atom content. No individuating "scent" atom needed. Perceptual individuation deferred.
2. **Store = THINGS** (entity dossiers), keyed by perceived entity id. PLACES/EVENTS deferred.
3. **Agents start with an empty `MeaningsStore`** and learn from scratch (no authored categories —
   that would be the special-casing we avoid). Consolidation mints categories during sleep.
4. **Arousal = 0** in v1 (surprise-gated only). The Fear-drive wiring is deferred.
5. **CQRS reconciliation:** per-agent memory (mutable `MeaningsStore` + `AgentMemoryStores`) lives in
   a new **sole-writer `AgentMemoryRegistry`**. Systems read percepts and emit intents; the registry
   applies them by calling the memory-core methods (`MemoryEncoder.Perceive`, `Consolidation.Pass`).
6. **Perception is cadenced + attention-bounded** (not every sensed entity every tick): each agent
   perceives at most K sensed entities once per `PerceiveCadenceTicks` (staggered, stateless). This
   bounds write volume and matches a realistic memory-formation rate.
7. **Behavior is unchanged** this piece (nothing reads the new memory yet). Observability is a soak
   memory metric, not the activity histogram.

## Architecture

Three units + a config + a soak metric.

### `AgentMemoryRegistry` (sole writer of per-agent memory)

- Holds `Dictionary<EntityId, AgentMemory>` where `AgentMemory { MeaningsStore Meanings;
  AgentMemoryStores Stores; }` (the A2/A3 objects). `MeaningsStore` starts empty.
- `void Seed(EntityId)` — load-time: create an empty `AgentMemory` for the agent.
- Intents:
  - `MemoryPerceiveIntent { EntityId Perceiver; EntityId Perceived; AtomBag Signature; AtomBag Percept; Fixed Arousal; }`
  - `MemoryConsolidateIntent { EntityId Agent; }`
- `Update(tick)`:
  - For each `MemoryPerceiveIntent`: look up the perceiver's `AgentMemory`; run
    `MemoryEncoder.Perceive(mem.Meanings, signature, percept, arousal, key = MemoryKey(Perceived.Value), tick, EncodeConfig)`;
    if the result is `Written`, `mem.Stores.Things.Encode(result.Record)`. (Perceive also folds the
    recognized category's stats — the ungated StatFold.)
  - For each `MemoryConsolidateIntent`: `Consolidation.Pass(mem.Stores.Things, mem.Meanings, ConsolidationConfig)`.
  - Drop despawned agents (`DespawnedEvent`).
- Read API: `bool TryGet(EntityId, out AgentMemory)`, `int Count`, `All` — for the soak metric and
  the future ODD read.

### `MemoryWriteSystem` (awake; reads percepts, emits perceive intents)

Stateless. Per tick, for each agent **due** this tick (`(tick + Phase(agent)) % cfg.PerceiveCadenceTicks == 0`,
`Phase(agent) = (uint)agent.Value % cfg.PerceiveCadenceTicks` — staggered, no per-agent state):
- read `Sensed.TryGet(agent, out var sensed)`; take up to `cfg.AttentionK` entities (deterministic:
  first-K in the list, which `SenseSystem` already orders);
- for each perceived entity, read its `Perceivable.Bag(perceived)`; split into **signature** (atoms
  with `Type.Value < PerceivableAtoms.ActivityBase` — kind/role/race) and **percept** (the full bag);
- emit `MemoryPerceiveIntent { Perceiver = agent, Perceived = perceived, Signature, Percept, Arousal = Fixed.Zero }`.

Skips entities with an empty bag (not yet atomized — e.g. monsters in v1).

### `ConsolidationSystem` (asleep; emits consolidate intents)

Stateless. Per tick, for each agent whose `Behavior.Activity == ActivityKind.Sleep` and due this tick
(`(tick + Phase(agent)) % cfg.ConsolidateCadenceTicks == 0`), emit `MemoryConsolidateIntent { Agent = agent }`.
Decay is sleep-gated by construction (Consolidation.Pass runs Decay); awake memory never fades.

### `AgentMemoryConfig`

`readonly struct` + `Default`: `int PerceiveCadenceTicks` (~600 = 1 game-minute), `int AttentionK`
(~4), `int ConsolidateCadenceTicks` (~36000 = 1 game-hour). Carries `EncodeConfig` and
`ConsolidationConfig` defaults too (or references them). All are timescale config (bunny doc).

### Soak metric

Extend `EngineSoak` to print per-sample memory stats over the agent population: mean category nodes /
agent (`MeaningsStore.Count`), mean THINGS records / agent, and **novelty rate** (fraction of recent
perceives that were novel — approximated as: agents with 0 categories, or a running counter). The arc
to watch: categories/agent rises, novelty falls across days.

## Data flow

```
spawn            -> AgentMemoryRegistry.Seed(agent)            [empty MeaningsStore + stores]
awake, cadenced  -> MemoryWriteSystem: sensed -> top-K -> MemoryPerceiveIntent(signature, percept)
                    AgentMemoryRegistry.Update: MemoryEncoder.Perceive (recognize+fold+surprise+record)
                                               -> Things.Encode(record) if written
asleep, cadenced -> ConsolidationSystem: MemoryConsolidateIntent
                    AgentMemoryRegistry.Update: Consolidation.Pass (RE-DIFF -> MINT -> DECAY)
death            -> DespawnedEvent -> drop AgentMemory
```

## System order

Insert in `SimWorld`'s systems array **after `SubjectiveSystem`** (so the tick's perception is
settled): `MemoryWriteSystem`, then `ConsolidationSystem`. `AgentMemoryRegistry` goes in the
registries array. Both systems only publish intents the registry applies next tick — order among them
is immaterial.

## CQRS / determinism notes

- `AgentMemoryRegistry` is the **sole writer** of agent memory; the mutable `MeaningsStore`/`MemoryStore`
  objects are mutated only inside its `Update`. Systems are stateless and read-only (the cadence is a
  pure function of `tick` + `agent.Value`, no per-agent timers).
- The memory core is float-free + key-ordered, so each agent's memory evolves deterministically given
  its intent stream. Cross-agent order is immaterial (each agent's memory is private). Run-to-run
  parallel nondeterminism is accepted (debug via event/snapshot capture).

## Testing

- **Unit (`AgentMemoryRegistry`):** seed an agent; feed `MemoryPerceiveIntent`s with a fixed percept;
  assert a THINGS record appears (novel → written); feed a `MemoryConsolidateIntent` after enough
  clustered novel records; assert a category was minted (`Meanings.Count` rose) and records re-keyed.
- **Unit (`MemoryWriteSystem`):** rig with `Sensed` + `Perceivable`; assert a due agent emits
  `MemoryPerceiveIntent`s for its top-K sensed entities with the right signature/percept split, and a
  not-due agent emits nothing.
- **Unit (`ConsolidationSystem`):** a sleeping due agent emits a consolidate intent; an awake one does not.
- **Integration (ARENA2-gated):** seed a town, step ~a few game-days, assert mean categories/agent > 0
  and novelty fell from day 1 to day N (the learning arc). Assert behavior histogram is unchanged
  in shape vs baseline (this piece doesn't move behavior).

## Deferred (later pieces)

- **Perceptual individuation** — a per-entity "scent" identity atom so recognition distinguishes
  individuals without their id (true entity dossiers by signature).
- **Arousal** — wire the Fear drive (NeedsSystem) as the `arousal` input (flashbulb memories).
- **PLACES + EVENTS** — spatial facts and episodic (subject-verb-object) records; needs activity/place
  atoms in the percept and an event key.
- **Monsters/other entities atomized** — give creatures perceivable atoms so they're perceived too.
- **The ODD repoint** — `GatherAds`/`V()`/`Interpret` read the new memory (where behavior changes).
