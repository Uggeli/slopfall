# Agent Memory: Write + Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make agent memory live — a sole-writer `AgentMemoryRegistry` (per-agent `MeaningsStore` + stores), a cadenced `MemoryWriteSystem` (perceive → write records + fold categories), and a sleep-gated `ConsolidationSystem` (mint categories). Agents learn the kinds of entities around them; behavior unchanged (nothing reads it yet); observable via a soak memory metric.

**Architecture:** Systems are stateless, read percepts, emit intents; the registry is the sole writer and applies them by calling memory-core methods (`MemoryEncoder.Perceive`, `Consolidation.Pass`). THINGS keyed by perceived-entity-id; empty MeaningsStore start; arousal 0.

**Tech Stack:** C# (.NET 10, C# 7.3), xUnit. Branch `perceivable-atoms` (continues piece 1; depends on A1–A5 + perceivable atoms). Spec: `docs/superpowers/specs/2026-06-21-agent-memory-write-consolidation-design.md`.

## Global Constraints

- **Namespace:** registry/systems/config in `DaggerfallWorkshop.Sim.Engine` (use memory-core types from `DaggerfallWorkshop.Sim.Memory`). Tests `Sim.MemoryTests`.
- **CQRS:** `AgentMemoryRegistry` is sole writer of agent memory; systems are stateless (cadence = pure fn of `tick` + `agent.Value`), read-only, emit intents only.
- **No floats** beyond `Fixed` (the memory core is already float-free).
- **C# 7.3.** Test runner: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`.

## File Structure

- `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` — config + `AgentMemory` + registry + intents (Task 1)
- `Assets/Sim/Engine/Units/MemoryWriteSystem.cs` (Task 2)
- `Assets/Sim/Engine/Units/ConsolidationSystem.cs` (Task 3)
- `Assets/Sim/Engine/SimWorld.cs`, `Assets/Sim/World/TownLoader.cs`, `Assets/Sim/Engine/EngineSoak.cs` (Task 4)
- Tests: `AgentMemoryRegistryTests.cs`, `MemoryWriteSystemTests.cs`, `ConsolidationSystemTests.cs`, `AgentMemoryLearningTests.cs`

---

### Task 1: `AgentMemoryConfig` + `AgentMemory` + `AgentMemoryRegistry`

**Files:** Create `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs`, `Headless/Sim.MemoryTests/AgentMemoryRegistryTests.cs`.

**Interfaces:**
- `AgentMemoryConfig` (readonly struct): `int PerceiveCadenceTicks, AttentionK, ConsolidateCadenceTicks; EncodeConfig Encode; ConsolidationConfig Consolidation;` + `static Default` (600, 4, 36000, defaults).
- `AgentMemory` (sealed class): `MeaningsStore Meanings`, `AgentMemoryStores Stores` (empty at construction).
- intents `MemoryPerceiveIntent { EntityId Perceiver, Perceived; AtomBag Signature, Percept; Fixed Arousal; }`, `MemoryConsolidateIntent { EntityId Agent; }`.
- `AgentMemoryRegistry : Registry`: ctor `(EventBus, AgentMemoryConfig)`; `void Seed(EntityId)`; `bool TryGet(EntityId, out AgentMemory)`; `int Count`; `IEnumerable<KeyValuePair<EntityId, AgentMemory>> All`. `Update` applies perceive (→ `MemoryEncoder.Perceive` → `Things.Encode` if written) then consolidate (→ `Consolidation.Pass`), then drops despawned.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/AgentMemoryRegistryTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AgentMemoryRegistryTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static (EventBus, AgentMemoryRegistry) New()
        {
            var e = new EventBus();
            return (e, new AgentMemoryRegistry(e, AgentMemoryConfig.Default));
        }
        static void Tick(EventBus e, AgentMemoryRegistry r) { e.Tick(); r.Update(0); }

        [Fact]
        public void Seed_CreatesEmptyMemory()
        {
            var (_, r) = New();
            r.Seed(new EntityId(1));
            Assert.True(r.TryGet(new EntityId(1), out var mem));
            Assert.Equal(0, mem.Meanings.Count);
            Assert.Equal(0, mem.Stores.Things.Count);
        }

        [Fact]
        public void Perceive_NovelEntity_WritesThingsRecord()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            var sig = Bag((1001, 1.0), (3007, 1.0));   // kind + race
            e.Publish(new MemoryPerceiveIntent { Perceiver = new EntityId(1), Perceived = new EntityId(50), Signature = sig, Percept = sig, Arousal = Fixed.Zero });
            Tick(e, r);

            r.TryGet(new EntityId(1), out var mem);
            Assert.Equal(1, mem.Stores.Things.Count);                  // novel -> verbatim record
            Assert.True(mem.Stores.Things.TryGet(new MemoryKey(50), out var rec));
            Assert.True(rec.IsNovel);
        }

        [Fact]
        public void Perceive_UnseededPerceiver_NoCrash()
        {
            var (e, r) = New();
            e.Publish(new MemoryPerceiveIntent { Perceiver = new EntityId(9), Perceived = new EntityId(50), Signature = AtomBag.Empty, Percept = AtomBag.Empty, Arousal = Fixed.Zero });
            Tick(e, r);
            Assert.Equal(0, r.Count);
        }

        [Fact]
        public void Consolidate_MintsCategory_FromClusteredNovels()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            var sig = Bag((1001, 1.0), (3007, 1.0));
            // Three distinct perceived entities, same atoms -> three novel THINGS records.
            foreach (var pid in new[] { 50, 51, 52 })
                e.Publish(new MemoryPerceiveIntent { Perceiver = new EntityId(1), Perceived = new EntityId(pid), Signature = sig, Percept = sig, Arousal = Fixed.Zero });
            Tick(e, r);
            r.TryGet(new EntityId(1), out var mem);
            Assert.Equal(3, mem.Stores.Things.Count);
            Assert.Equal(0, mem.Meanings.Count);

            e.Publish(new MemoryConsolidateIntent { Agent = new EntityId(1) });
            Tick(e, r);
            r.TryGet(new EntityId(1), out mem);
            Assert.True(mem.Meanings.Count >= 1);                      // a category was minted from the cluster
        }

        [Fact]
        public void Despawn_DropsMemory()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            e.Publish(new DespawnedEvent { Entity = new EntityId(1) });
            Tick(e, r);
            Assert.False(r.TryGet(new EntityId(1), out _));
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.** (Confirm `EncodeConfig`/`ConsolidationConfig`/`MeaningsConfig`/`MemoryEncoder`/`Consolidation`/`AgentMemoryStores`/`MeaningsStore` names against `Assets/Sim/Memory/` — all exist from A1–A5.)
- [ ] **Step 3: Implement** — `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Timescale knobs for agent memory (bunny doc: rates are config). Cadences in ticks.</summary>
    public readonly struct AgentMemoryConfig
    {
        public readonly int PerceiveCadenceTicks;     // how often an agent forms perceptions
        public readonly int AttentionK;               // top-K sensed entities perceived per batch
        public readonly int ConsolidateCadenceTicks;  // how often a sleeping agent consolidates
        public readonly EncodeConfig Encode;
        public readonly ConsolidationConfig Consolidation;

        public AgentMemoryConfig(int perceiveCadenceTicks, int attentionK, int consolidateCadenceTicks,
                                 EncodeConfig encode, ConsolidationConfig consolidation)
        {
            PerceiveCadenceTicks = perceiveCadenceTicks;
            AttentionK = attentionK;
            ConsolidateCadenceTicks = consolidateCadenceTicks;
            Encode = encode;
            Consolidation = consolidation;
        }

        // 600 ticks ~ 1 game-minute; 36000 ~ 1 game-hour (864000 ticks/day).
        public static readonly AgentMemoryConfig Default =
            new AgentMemoryConfig(600, 4, 36000, EncodeConfig.Default, ConsolidationConfig.Default);
    }

    /// <summary>One agent's private memory: learned categories + the bounded record stores.</summary>
    public sealed class AgentMemory
    {
        public readonly MeaningsStore Meanings = new MeaningsStore(AgentMemoryStores.MeaningsCap, MeaningsConfig.Default);
        public readonly AgentMemoryStores Stores = new AgentMemoryStores();
    }

    /// <summary>Perceive an entity: recognize+fold+surprise, write a record if gated.</summary>
    public struct MemoryPerceiveIntent : IEvent
    {
        public EntityId Perceiver, Perceived;
        public AtomBag Signature, Percept;
        public Fixed Arousal;
    }

    /// <summary>Run a sleep consolidation pass over an agent's memory.</summary>
    public struct MemoryConsolidateIntent : IEvent { public EntityId Agent; }

    /// <summary>
    /// Sole writer of per-agent memory. Systems read percepts and emit intents; this registry
    /// applies them by calling the memory core (Perceive folds + builds a record routed to THINGS;
    /// Consolidate runs RE-DIFF/MINT/DECAY). The mutable stores are mutated only here.
    /// </summary>
    public sealed class AgentMemoryRegistry : Registry
    {
        readonly Dictionary<EntityId, AgentMemory> _d = new Dictionary<EntityId, AgentMemory>();
        readonly AgentMemoryConfig _cfg;

        public AgentMemoryRegistry(EventBus events, AgentMemoryConfig cfg) : base(events) { _cfg = cfg; }

        /// <summary>Load-time: give an agent an empty memory.</summary>
        public void Seed(EntityId id) { if (!_d.ContainsKey(id)) _d[id] = new AgentMemory(); }

        public override void Update(long tick)
        {
            var perceives = Events.GetEvents<MemoryPerceiveIntent>();
            for (int i = 0; i < perceives.Length; i++)
            {
                if (!_d.TryGetValue(perceives[i].Perceiver, out var mem)) continue;
                EncodeResult res = MemoryEncoder.Perceive(
                    mem.Meanings, perceives[i].Signature, perceives[i].Percept, perceives[i].Arousal,
                    new MemoryKey(perceives[i].Perceived.Value), tick, _cfg.Encode);
                if (res.Written) mem.Stores.Things.Encode(res.Record);
            }

            var cons = Events.GetEvents<MemoryConsolidateIntent>();
            for (int i = 0; i < cons.Length; i++)
                if (_d.TryGetValue(cons[i].Agent, out var mem))
                    Consolidation.Pass(mem.Stores.Things, mem.Meanings, _cfg.Consolidation);

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++) _d.Remove(gone[i].Entity);
        }

        public bool TryGet(EntityId id, out AgentMemory mem) => _d.TryGetValue(id, out mem);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, AgentMemory>> All => _d;
    }
}
```

- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(memory): AgentMemoryRegistry — sole-writer per-agent memory (perceive + consolidate)`.

---

### Task 2: `MemoryWriteSystem`

**Files:** Create `Assets/Sim/Engine/Units/MemoryWriteSystem.cs`, `Headless/Sim.MemoryTests/MemoryWriteSystemTests.cs`.

**Interfaces:** `MemoryWriteSystem : SimSystem`, ctor `(EventBus, SensedRegistry, PerceivableRegistry, AgentMemoryConfig)`. `Update`: for each due agent (`(tick + Phase) % PerceiveCadenceTicks == 0`), read its sensed list, take top-`AttentionK`, split each perceived bag into signature (`type < PerceivableAtoms.ActivityBase`) + percept (full), emit `MemoryPerceiveIntent`.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/MemoryWriteSystemTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryWriteSystemTests
    {
        // Cadence 1 so every agent is "due" every tick — keeps the test simple.
        static readonly AgentMemoryConfig Cfg = new AgentMemoryConfig(1, 4, 36000, EncodeConfig.Default, ConsolidationConfig.Default);

        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly SensedRegistry Sensed;
            public readonly PerceivableRegistry Perceivable;
            public readonly MemoryWriteSystem System;
            public Rig()
            {
                Sensed = new SensedRegistry(E);
                Perceivable = new PerceivableRegistry(E);
                System = new MemoryWriteSystem(E, Sensed, Perceivable, Cfg);
            }
            public void Step() { E.Tick(); Sensed.Update(0); Perceivable.Update(0); System.Update(0); }
            public List<MemoryPerceiveIntent> NextPerceives()
            {
                // After System.Update publishes into the next batch, flip and read them.
                E.Tick();
                return E.GetEvents<MemoryPerceiveIntent>().ToArray().ToList();
            }
        }

        [Fact]
        public void DueAgent_EmitsPerceive_ForSensed_WithSignaturePerceptSplit()
        {
            var r = new Rig();
            var agent = new EntityId(1);
            var seen = new EntityId(50);
            // perceived entity's bag: kind(1001) + race(3007) [identity] + activity(4005) [state]
            r.Perceivable.Seed(seen, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);
            r.Perceivable.Seed(seen, new AtomTypeId(3007), Fixed.One);
            r.Perceivable.Seed(seen, new AtomTypeId(4005), Fixed.One);
            r.E.Publish(new SensedSetIntent { Id = agent, Sensed = new List<EntityId> { seen } });
            r.Step();

            var emitted = r.NextPerceives();
            Assert.Single(emitted);
            var p = emitted[0];
            Assert.Equal(agent, p.Perceiver);
            Assert.Equal(seen, p.Perceived);
            // signature = identity atoms only (no activity); percept = full bag
            Assert.DoesNotContain(p.Signature.Atoms, a => a.Type.Value >= PerceivableAtoms.ActivityBase);
            Assert.Contains(p.Percept.Atoms, a => a.Type.Value >= PerceivableAtoms.ActivityBase);
        }

        [Fact]
        public void NotDueAgent_EmitsNothing()
        {
            // Cadence 10, agent whose phase makes it not due at tick 0.
            var cfg = new AgentMemoryConfig(10, 4, 36000, EncodeConfig.Default, ConsolidationConfig.Default);
            var e = new EventBus();
            var sensed = new SensedRegistry(e);
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryWriteSystem(e, sensed, perceivable, cfg);
            var agent = new EntityId(3);   // (0 + 3) % 10 != 0 -> not due at tick 0
            e.Publish(new SensedSetIntent { Id = agent, Sensed = new List<EntityId> { new EntityId(50) } });
            e.Tick(); sensed.Update(0); perceivable.Update(0); sys.Update(0);
            e.Tick();
            Assert.Empty(e.GetEvents<MemoryPerceiveIntent>().ToArray());
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.** (Confirm `EventBus.GetEvents` is publicly readable in tests — it is, used in PerceivableActivityTests; and `SensedSetIntent` shape.)
- [ ] **Step 3: Implement** — `Assets/Sim/Engine/Units/MemoryWriteSystem.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Awake perception → memory. Stateless: each agent is "due" on a staggered cadence (a pure
    /// function of tick + agent id, no per-agent timer). When due, it reads the agent's top-K sensed
    /// entities, splits each perceived bag into a signature (identity atoms, for recognition) and a
    /// full percept (for surprise), and emits a MemoryPerceiveIntent the AgentMemoryRegistry applies.
    /// </summary>
    public sealed class MemoryWriteSystem : SimSystem
    {
        readonly SensedRegistry _sensed;
        readonly PerceivableRegistry _perceivable;
        readonly AgentMemoryConfig _cfg;

        public MemoryWriteSystem(EventBus events, SensedRegistry sensed, PerceivableRegistry perceivable, AgentMemoryConfig cfg)
            : base(events) { _sensed = sensed; _perceivable = perceivable; _cfg = cfg; }

        public override void Update(long tick)
        {
            int cadence = _cfg.PerceiveCadenceTicks;
            foreach (var kv in _sensed.All)
            {
                EntityId agent = kv.Key;
                long phase = (uint)agent.Value % (uint)cadence;
                if ((tick + phase) % cadence != 0) continue;   // not this agent's turn

                List<EntityId> seen = kv.Value;
                if (seen == null) continue;
                int k = seen.Count < _cfg.AttentionK ? seen.Count : _cfg.AttentionK;
                for (int i = 0; i < k; i++)
                {
                    EntityId other = seen[i];
                    AtomBag percept = _perceivable.Bag(other);
                    if (percept.Count == 0) continue;          // not atomized (e.g. a monster in v1)
                    AtomBag signature = IdentityOnly(percept);
                    Events.Publish(new MemoryPerceiveIntent
                    {
                        Perceiver = agent, Perceived = other,
                        Signature = signature, Percept = percept, Arousal = Fixed.Zero,
                    });
                }
            }
        }

        /// <summary>Identity atoms only (type below the activity range) — the stable recognition key.</summary>
        static AtomBag IdentityOnly(AtomBag full)
        {
            List<Atom> ids = null;
            for (int i = 0; i < full.Count; i++)
            {
                if (full[i].Type.Value >= PerceivableAtoms.ActivityBase) continue;
                if (ids == null) ids = new List<Atom>(full.Count);
                ids.Add(full[i]);
            }
            return ids == null ? AtomBag.Empty : AtomBag.Create(ids);
        }
    }
}
```

- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(memory): MemoryWriteSystem — cadenced perceive -> perceive intents`.

---

### Task 3: `ConsolidationSystem`

**Files:** Create `Assets/Sim/Engine/Units/ConsolidationSystem.cs`, `Headless/Sim.MemoryTests/ConsolidationSystemTests.cs`.

**Interfaces:** `ConsolidationSystem : SimSystem`, ctor `(EventBus, BehaviorRegistry, AgentMemoryConfig)`. `Update`: for each agent with `Behavior.Activity == ActivityKind.Sleep` that is due (`(tick + Phase) % ConsolidateCadenceTicks == 0`), emit `MemoryConsolidateIntent`.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/ConsolidationSystemTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationSystemTests
    {
        static readonly AgentMemoryConfig Cfg = new AgentMemoryConfig(1, 4, 1, EncodeConfig.Default, ConsolidationConfig.Default);

        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly BehaviorRegistry Behavior;
            public readonly ConsolidationSystem System;
            public Rig() { Behavior = new BehaviorRegistry(E); System = new ConsolidationSystem(E, Behavior, Cfg); }
            public void SetActivity(EntityId id, ActivityKind a) => E.Publish(new BehaviorSetIntent { Id = id, Data = new BehaviorData { Activity = a } });
            public void Step() { E.Tick(); Behavior.Update(0); System.Update(0); }
            public MemoryConsolidateIntent[] Next() { E.Tick(); return E.GetEvents<MemoryConsolidateIntent>().ToArray(); }
        }

        [Fact]
        public void SleepingAgent_EmitsConsolidate()
        {
            var r = new Rig();
            r.SetActivity(new EntityId(1), ActivityKind.Sleep);
            r.Step();
            var c = r.Next();
            Assert.Single(c);
            Assert.Equal(new EntityId(1), c[0].Agent);
        }

        [Fact]
        public void AwakeAgent_EmitsNothing()
        {
            var r = new Rig();
            r.SetActivity(new EntityId(1), ActivityKind.Work);
            r.Step();
            Assert.Empty(r.Next());
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** — `Assets/Sim/Engine/Units/ConsolidationSystem.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Asleep consolidation trigger. Stateless: a sleeping agent is "due" on a staggered cadence
    /// (pure function of tick + id); when due it emits a MemoryConsolidateIntent the
    /// AgentMemoryRegistry applies (RE-DIFF/MINT/DECAY). Decay is thus sleep-gated — awake memory
    /// never fades, and population-staggered sleep amortizes the cost.
    /// </summary>
    public sealed class ConsolidationSystem : SimSystem
    {
        readonly BehaviorRegistry _behavior;
        readonly AgentMemoryConfig _cfg;

        public ConsolidationSystem(EventBus events, BehaviorRegistry behavior, AgentMemoryConfig cfg)
            : base(events) { _behavior = behavior; _cfg = cfg; }

        public override void Update(long tick)
        {
            int cadence = _cfg.ConsolidateCadenceTicks;
            foreach (var kv in _behavior.All)
            {
                if (kv.Value == null || kv.Value.Activity != ActivityKind.Sleep) continue;
                long phase = (uint)kv.Key.Value % (uint)cadence;
                if ((tick + phase) % cadence != 0) continue;
                Events.Publish(new MemoryConsolidateIntent { Agent = kv.Key });
            }
        }
    }
}
```

- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(memory): ConsolidationSystem — sleep-gated consolidate trigger`.

---

### Task 4: Wire into `SimWorld` + seed at spawn + soak metric + learning-arc test

**Files:** Modify `Assets/Sim/Engine/SimWorld.cs`, `Assets/Sim/World/TownLoader.cs`, `Assets/Sim/Engine/EngineSoak.cs`; create `Headless/Sim.MemoryTests/AgentMemoryLearningTests.cs`.

- [ ] **Step 1: Register in `SimWorld`.**
  - Field: `public readonly AgentMemoryRegistry AgentMemory;`
  - Construct (after `Perceivable = …`): `AgentMemory = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);`
  - Add `AgentMemory` to the registries array.
  - Add systems to the systems array **after `SubjectiveSystem`**: `new MemoryWriteSystem(e, Sensed, Perceivable, AgentMemoryConfig.Default), new ConsolidationSystem(e, Behavior, AgentMemoryConfig.Default),`. (Use one shared `AgentMemoryConfig.Default` — or hoist to a local so registry + systems share an instance.)

- [ ] **Step 2: Seed at spawn.** In `Assets/Sim/World/TownLoader.cs`, right after the perceivable
  identity seeding (the `world.Perceivable.Seed(id, …Role(role)…)` line added in piece 1), add:
  `world.AgentMemory.Seed(id);`

- [ ] **Step 3: Soak memory metric.** In `Assets/Sim/Engine/EngineSoak.cs`'s reporter (the block that
  prints the activity histogram), compute over `w.AgentMemory.All`: mean `Meanings.Count`, mean
  `Stores.Things.Count`, and `%` of agents with `Meanings.Count > 0`. Append to the sample line, e.g.
  `mem[cat/agent=… rec/agent=… learned%=…]`. (Read the reporter first; match its formatting.)

- [ ] **Step 4: Write the learning-arc integration test (ARENA2-gated)** —
  `Headless/Sim.MemoryTests/AgentMemoryLearningTests.cs`. Model the ARENA2 gate + `SimBoot.CreateTown`
  on `PerceivableSeedingTests`. Step the world ~2 game-days (`for (long t=0;t<2*864000;t++) world.Step();`
  — or use the soak's stepping if cheaper). Assert:
  - mean `Meanings.Count` across `world.AgentMemory.All` is `> 0` after 2 days (categories formed);
  - at least one agent has a `Things.Count > 0` early and `Meanings.Count > 0` later (the arc).
  Keep the step count modest if 2 days is too slow — even ~1 day past the first sleep should mint
  categories. Skip (return) when `DAGGERFALL_ARENA2` is unset.

- [ ] **Step 5: Run** the full memory suite (ARENA2 set) — all green; **run a 1-day soak** and confirm
  the activity histogram shape matches baseline (this piece doesn't move behavior) while the new
  `mem[…]` metric shows categories/agent rising.
- [ ] **Step 6: Commit** `feat(memory): wire agent memory into SimWorld + seed + soak metric`.

---

## Self-Review

**Spec coverage:** `AgentMemoryRegistry` (sole writer, perceive+consolidate) T1; `MemoryWriteSystem`
(cadenced, signature/percept split) T2; `ConsolidationSystem` (sleep-gated) T3; SimWorld wiring + spawn
seed + soak metric + learning-arc test T4. THINGS keyed by perceived id, empty start, arousal 0,
behavior-unchanged — all honored. Deferred items (individuation, arousal, PLACES/EVENTS, ODD repoint)
correctly absent.

**Type consistency:** `MemoryEncoder.Perceive`/`EncodeResult`/`MemoryKey`/`Consolidation.Pass`/
`AgentMemoryStores`/`MeaningsStore`/`EncodeConfig`/`ConsolidationConfig` are the A1–A5 signatures;
`PerceivableAtoms.ActivityBase` is the signature/percept split line; `SensedRegistry.All`/`SensedSetIntent`,
`BehaviorRegistry.All`, `DespawnedEvent.Entity` match existing usage. Cadence is `(tick + (uint)id % cad) % cad == 0`.

**CQRS/determinism:** registry is the sole writer; systems stateless (cadence pure-fn); per-agent memory
private so cross-agent order is immaterial; float-free core. Behavior untouched — observability is the
memory metric, not the histogram.

**Risk note:** T4 touches live files; each step reads the target first. The learning-arc test is heavy
(steps a real town for ~a day) and ARENA2-gated; keep step count to just past the first sleep bout.
