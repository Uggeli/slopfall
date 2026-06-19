# Sim core refactor — the CQRS split (per `docs/Architechturesample.cs`)

**Status:** design, aligned to the user's `Architechturesample.cs`. The sample is the reference; this doc maps the existing sim onto it and sequences the migration.

**The rule:** every system reads tick-N data and produces intents for tick N+1; tick data is immutable during the read phase; reads are coordination-free → systems run truly parallel (nested: systems ∥, `Parallel.ForEach` interiors).

---

## 1. The model (three roles, one channel)

### System — pure logic, ZERO data
Reads registries **read-only**, publishes intent events. Holds no storage, no caches, no cross-tick state — only references to the bus and the registries it reads.
```csharp
public override void Update()
{
    Parallel.ForEach(_movement.Components, kv => {       // read-only → parallel over entities
        if (kv.Value.X < 100f)
            EventBus.Publish(new MoveEntityEvent { EntityId = kv.Key, DeltaX = 5f });
    });
}
```

### Registry — owns data, sole writer, applies events
Pulls last tick's events, mutates its **own** storage in place. Reads **only its own data + events** — never another registry.
```csharp
public override void Update()
{
    foreach (ref readonly var ev in _eventBus.GetEvents<MoveEntityEvent>()) {
        if (_storage.TryGetValue(ev.EntityId, out var p)) { p.X += ev.DeltaX; _storage[ev.EntityId] = p; }
    }
}
```

### EventBus — per-type, double-buffered, zero-alloc
`EventBuffer<T>` holds `_incoming` (locked writes this tick) + `_processing` (read-only this tick); `Flip()` swaps. `GetEvents<T>()` → `ReadOnlySpan<T>` via `CollectionsMarshal.AsSpan`. Events are **structs** (`: IEvent`). No `Subscribe`, no callbacks, no `EmitImmediate`.

### The loop
```
eventBus.Tick();                              // flip every per-type buffer
Parallel.For(registries):  r.Update();        // WRITE: apply N-1 events to own data
Parallel.For(systems):     s.Update();        // READ + EMIT: read settled data, publish intents
```
Why it's safe without per-registry buffering: **phase separation.** In the write phase only registries run, each the sole writer of its own storage (different storage → no contention; events are read-only). In the read phase only systems run, all registry data settled and read-only. The two never overlap. The bus is the only cross-tick/cross-system channel, and it's buffered.

---

## 2. What this SUPERSEDES from the in-progress work (revert)

| Built this session | Fate |
|---|---|
| `BufferedDictionary` / `IDoubleBuffered` (`Core/DoubleBuffered.cs`) | **Delete.** Registries mutate in place; the bus is the buffer. |
| 18 registries converted to `BufferedDictionary` | **Revert** to plain storage; their writes move to `Registry.Update`. |
| `SimulationContext.CommitAll` + two-phase commit in TickLoop | **Delete.** Replaced by `eventBus.Tick()` flip + the two parallel phases. |
| Bucket B copy-on-write (Aging/Affects/Meanings/Creature/Economy) | **Moot.** Those writes leave systems entirely; in-place is fine inside registries (phase-separated). |
| NeedsSystem discomfort fold, ItemOps COW (D2) | Logic relocates (system emits / registry applies); the edits don't survive as-is. |
| **D1 — precompute TownGrid connectivity at load** | **Keep.** Independent correctness win, model-agnostic. |
| Loop simplification intent (one swap) | Spirit kept; loop shape changes to flip + 2 phases. |

Net: revert the buffering branch of work, keep D1, rebuild on the sample.

---

## 3. The real restructure — every system splits in two

Each current "system" both computes and writes. Split it:
- **Computation** stays in a System: read registries, produce intent events. (The hard part — cross-registry reads — lives here, where all data is settled.)
- **The write** moves into the owning Registry's `Update`: consume the intent events, apply to own storage.
- **Cross-registry data the write needs is carried in the event payload** (the system computed it). Registries can't read each other, so the event must be fat enough.

Mapping (representative; intent event names provisional):

| Current system | Becomes System (reads → emits) | Owning Registry applies |
|---|---|---|
| NeedsSystem | reads Needs+Behavior+Personality+Larder+Coin → `NeedsDelta{id, dV[]}` | NeedsRegistry |
| MovementSystem | reads Behavior+Position+TownGrid (pathfind) → `MoveTo{id,x,z,yaw}` | PositionRegistry |
| OddSystem | reads many → `SetIntent{id,…}` | IntentRegistry |
| ExecutionSystem | reads Intent+Position → `SetBehavior{id,…}` | BehaviorRegistry |
| EconomySystem | reads all; simulates trades on **method-local** balances → `CoinTransfer`/`StockDelta`/`LarderDelta`/… | Coin/Stock/Larder/Treasury/Ledger |
| EffectTick + EffectLifecycle | read Effects+events → `EffectDelta` | EffectsRegistry (one owner) |
| StatusFlagDerive / EffectAggregate | read Effects → `SetStatusFlags` / `SetAggregate` | those registries |
| Sense / Subjective | read Position/Behavior/Sensed → whole-value `SensedSet` / `SubjectiveSet` | Sensed/Subjective |
| Affects / Meanings | read interaction events → `AffectDelta` / `MeaningFold` | Affects/Meanings |
| Social | read Behavior+Relations → `RelationDelta` + `OccupancySet` | Relations/Memory/Occupancy |
| Combat / Creature | read Position/Creatures → `Damage` / `CreatureMove` | Creatures/Vitals (one owner each) |
| Aging / Health / Skill / Progression | already event-shaped → deltas | Life/Vitals/Stats/Progression |

The A1 event spools, A3 timers, the Subscribe model, `EmitImmediate`, `ProcessEvents`/`Update` split, `_ctx` fields — **all deleted**: systems read `GetEvents<T>()` directly; cadence reads the existing time events; per-tick scratch becomes locals.

---

## 4. Migration order (each step builds + runs)

1. **EventBus rewrite** to the sample (`EventBuffer<T>`, per-type, `Publish`/`GetEvents`/`Tick`). Convert the ~44 event types from classes to `struct : IEvent`.
2. **Base abstractions**: `abstract Registry { void Update(); }`, `abstract System { void Update(); }`. New loop (flip → registries → systems).
3. **Vertical slice first** (prove the pattern end-to-end): Movement. PositionRegistry owns position + applies `MoveTo`; MovementSystem reads & emits. Run it.
4. **Migrate the rest system-by-system**, each producing intent events + giving the owning registry an apply step. Define intent structs as you go. Delete each system's fields until it has none.
5. **New registries** for the A2 state that had no home: paths, social cooldowns, daily-ledger.
6. **Serial-correct gate**: whole sim on the model, loop still single-threaded (run registries/systems sequentially). Correct before threads.
7. **Parallelize**: the loop's `Parallel.For` over registries + systems, `Parallel.ForEach` interiors, per-system RNG streams. Determinism-by-seed check (two runs identical).

---

## 5. Decisions the existing sim forces (need your call)

1. **Delta vs whole-value intents.** Sample uses deltas (`DeltaX`). Quantities (Coin/Stock/Needs) fit deltas; derived wholesale stores (Sensed/Subjective/StatusFlags) fit whole-value sets. OK to mix per registry?
2. **Per-entity event volume.** NeedsDelta/MoveTo are ~one event per agent per tick (~600+). Struct + span = zero-alloc, but confirm you want that granularity vs batched array events.
3. **Render/snapshot reader.** Registries mutate in place during the write phase; the render thread reads concurrently. Options: build the snapshot in the read phase, or give registries an external read-flip. Defer, but flag.
4. **Economy intra-tick chains.** Confirm the approach: EconomySystem simulates the whole money chain on **method-local** balances (reads tick-N registry values once), emits net transfer events; CoinRegistry applies. No registry-side chaining.
5. **Revert confirmation.** OK to `git`-revert the buffering work (keep D1) and rebuild from step 1? It's superseded, not salvageable in place.
