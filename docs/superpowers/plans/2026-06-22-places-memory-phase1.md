# PLACES Memory — Phase 1 (write/seed layer) Implementation Plan

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development / executing-plans. Steps use `- [ ]`.

**Goal:** Agents accumulate a rich atom-bag memory per place (BuildingKind/ProvisionsHere/DangerHere) in the new PLACES store — seeded + learned — with **no ODD change** (additive; behavior must stay == baseline). Phase 2 (ODD scoring) is separate.

**Spec:** `docs/superpowers/specs/2026-06-22-places-memory-design.md`. Branch `perceivable-atoms`.

## Global Constraints
- CQRS: `AgentMemoryRegistry` is sole writer of agent memory; places written via `PlaceObserveIntent` (runtime) + direct `SeedPlace` (load). Owner-stamps-its-own.
- No floats beyond `Fixed`. C# 7.3. Phase 1 must not change behavior (no ODD read yet).
- Test: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`.

## Files
- `Assets/Sim/Memory/PlaceAtoms.cs` (Task 1)
- `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` — `PlaceObserveIntent` + merge handling + `SeedPlace` (Task 1)
- `Assets/Sim/World/AgentMemorySeeding.cs` + `TownLoader.cs` wiring (Task 2)
- `Assets/Sim/Engine/Units/EconomySystem.cs` — provisions write (Task 2)
- `Assets/Sim/Engine/Units/PlaceDangerSystem.cs` + `SimWorld.cs` + `ConsolidationSystem` PLACES decay (Task 3)
- `Assets/Sim/Engine/EngineSoak.cs` — places metric (Task 3)
- Tests: `PlaceMemoryWriteTests.cs`, `PlaceSeedingTests.cs`, `PlaceDangerTests.cs`

---

### Task 1: `PlaceAtoms` + `PlaceObserveIntent` + registry merge + `SeedPlace`

**Interfaces:**
- `PlaceAtoms` (static): `const int KindBase=5000, ProvisionsHere=6000, DangerHere=6001`; `AtomTypeId Kind(BuildingKind)` (= `KindBase + (int)kind`); `static AtomTypeId Provisions => new AtomTypeId(6000)`, `Danger => new AtomTypeId(6001)`.
- `PlaceObserveIntent { EntityId Agent; int Building; AtomTypeId Atom; Fixed Value; }`.
- `AgentMemoryRegistry`: handle `PlaceObserveIntent` (merge atom into the agent's PLACES record for the building, refreshed strength 200); `void SeedPlace(EntityId agent, int building, AtomTypeId atom, Fixed value)` (load-time direct merge).

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/PlaceMemoryWriteTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PlaceMemoryWriteTests
    {
        static (EventBus, AgentMemoryRegistry) New()
        { var e = new EventBus(); return (e, new AgentMemoryRegistry(e, AgentMemoryConfig.Default)); }

        [Fact]
        public void PlaceAtoms_RangesDistinct()
        {
            Assert.Equal(5000 + (int)BuildingKind.Tavern, PlaceAtoms.Kind(BuildingKind.Tavern).Value);
            Assert.Equal(6000, PlaceAtoms.Provisions.Value);
            Assert.Equal(6001, PlaceAtoms.Danger.Value);
            Assert.True(PlaceAtoms.Kind(BuildingKind.Tavern).Value >= 5000);   // above the activity range
        }

        [Fact]
        public void Observe_AccumulatesAtoms_OnBuildingRecord_ValueWins()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            e.Publish(new PlaceObserveIntent { Agent = new EntityId(1), Building = 5, Atom = PlaceAtoms.Kind(BuildingKind.Tavern), Value = Fixed.One });
            e.Publish(new PlaceObserveIntent { Agent = new EntityId(1), Building = 5, Atom = PlaceAtoms.Danger, Value = Fixed.FromDouble(0.5) });
            e.Tick(); r.Update(0);

            r.TryGet(new EntityId(1), out var mem);
            Assert.True(mem.Stores.Places.TryGet(new MemoryKey(5), out var rec));
            Assert.Equal(new[] { 5000 + (int)BuildingKind.Tavern, 6001 }, rec.DeltaBag.Atoms.Select(a => a.Type.Value).OrderBy(x => x).ToArray());

            // re-observe danger with a new value -> value wins
            e.Publish(new PlaceObserveIntent { Agent = new EntityId(1), Building = 5, Atom = PlaceAtoms.Danger, Value = Fixed.FromDouble(0.9) });
            e.Tick(); r.Update(0);
            r.TryGet(new EntityId(1), out mem);
            mem.Stores.Places.TryGet(new MemoryKey(5), out rec);
            rec.DeltaBag.TryGet(PlaceAtoms.Danger, out var d);
            Assert.Equal(Fixed.FromDouble(0.9), d);
        }

        [Fact]
        public void SeedPlace_WritesDirectly()
        {
            var (_, r) = New();
            r.Seed(new EntityId(1));
            r.SeedPlace(new EntityId(1), 7, PlaceAtoms.Kind(BuildingKind.GeneralStore), Fixed.One);
            r.TryGet(new EntityId(1), out var mem);
            Assert.True(mem.Stores.Places.TryGet(new MemoryKey(7), out var rec));
            Assert.True(rec.DeltaBag.Contains(PlaceAtoms.Kind(BuildingKind.GeneralStore)));
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement.**
  - `Assets/Sim/Memory/PlaceAtoms.cs`:

    ```csharp
    namespace DaggerfallWorkshop.Sim.Memory
    {
        /// <summary>Id authority for PLACES atoms (id range 5000+, above the entity/activity ranges).
        /// BuildingKind is presence-per-kind; Provisions/Danger are graded facts.</summary>
        public static class PlaceAtoms
        {
            public const int KindBase = 5000;
            public static AtomTypeId Kind(BuildingKind kind) => new AtomTypeId(KindBase + (int)kind);
            public static AtomTypeId Provisions => new AtomTypeId(6000);
            public static AtomTypeId Danger => new AtomTypeId(6001);
        }
    }
    ```
    (`BuildingKind` is in `DaggerfallWorkshop.Sim`; `.Sim.Memory` sees it via enclosing-namespace scope.)
  - In `AgentMemoryRegistry.cs`: add the intent struct beside the others:

    ```csharp
    /// <summary>Stamp/refresh one atom on the agent's memory of a place (building).</summary>
    public struct PlaceObserveIntent : IEvent { public EntityId Agent; public int Building; public AtomTypeId Atom; public Fixed Value; }
    ```
    Add a private merge helper + the load-time method + the intent handling. Inside the class:

    ```csharp
        const byte PlaceStrength = 200;   // observed places stay vivid; unrefreshed fade via consolidation

        /// <summary>Load-time direct place stamp (no intent).</summary>
        public void SeedPlace(EntityId agent, int building, AtomTypeId atom, Fixed value)
        { if (_d.TryGetValue(agent, out var mem)) MergePlaceAtom(mem, building, atom, value, 0); }

        static void MergePlaceAtom(AgentMemory mem, int building, AtomTypeId atom, Fixed value, long tick)
        {
            var key = new MemoryKey(building);
            var add = AtomBag.Create(new[] { new Atom(atom, value) });
            AtomBag delta = mem.Stores.Places.TryGet(key, out var rec) ? AtomBag.Merge(rec.DeltaBag, add) : add;
            mem.Stores.Places.Encode(new MemoryRecord(key, CategoryId.None, delta, PlaceStrength, tick, tick, MemoryFlags.None));
        }
    ```
    And in `Update`, after the reinforce loop:

    ```csharp
            var places = Events.GetEvents<PlaceObserveIntent>();
            for (int i = 0; i < places.Length; i++)
                if (_d.TryGetValue(places[i].Agent, out var pm))
                    MergePlaceAtom(pm, places[i].Building, places[i].Atom, places[i].Value, tick);
    ```

- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(memory): PlaceAtoms + PlaceObserveIntent + per-building atom accumulation`.

---

### Task 2: `AgentMemorySeeding.SeedPlaces` (load) + `EconomySystem` provisions write

**Interfaces:** `AgentMemorySeeding.SeedPlaces(SimWorld world, EntityId agent)` — for each building the agent `Known`s (via `PlaceMemory`), `world.AgentMemory.SeedPlace(agent, building, PlaceAtoms.Kind(kind), One)` (kind from `BuildingRegistry`, skip `None`). `EconomySystem`: at each existing `Note(id, b, ProvisionsHere, v, t)`, also `Events.Publish(new PlaceObserveIntent { Agent=id, Building=b, Atom=PlaceAtoms.Provisions, Value=Fixed.FromDouble(v) })`.

- [ ] **Step 1: Read** `TownLoader` (the AgentMemory.Seed line + how `Known`/residency is available at spawn — note: `Known` may be seeded AFTER spawn by `SeedTownKnowledge`, so `SeedPlaces` must run after knowledge seeding — call it from `SeedSettlement`/`SeedTownKnowledge`'s tail, iterating residents, not from per-agent `Spawn`). Confirm `BuildingRegistry.TryGet(building).Kind`.
- [ ] **Step 2: Write the failing test** — `Headless/Sim.MemoryTests/PlaceSeedingTests.cs`, ARENA2-gated: seed a town, `ApplySeed`, sample a civilian, assert `world.AgentMemory.TryGet(agent).Stores.Places.Count > 0` and a record carries a `KindBase..` atom. (Model on `PerceivableSeedingTests`.)
- [ ] **Step 3: Implement** `AgentMemorySeeding.SeedPlaces` + call it where settlement knowledge is finalized; add the `PlaceObserveIntent` publish beside each `EconomySystem` provisions `Note`.
- [ ] **Step 4: Run, verify PASS** (ARENA2 set).
- [ ] **Step 5: Commit** `feat(memory): seed place-kind atoms at load + economy stamps provisions`.

---

### Task 3: `PlaceDangerSystem` + PLACES decay + soak metric

**Interfaces:** `PlaceDangerSystem : SimSystem(EventBus, SensedRegistry, CreatureRegistry, PositionRegistry?, BuildingRegistry?)` — on `DeathEvent` where the killer is a creature, for each agent who **sensed the victim** (and/or is near), emit `PlaceObserveIntent(witness, nearestBuilding, PlaceAtoms.Danger, severity)`. `ConsolidationSystem`/`Consolidation.Pass`: also `store.Decay` the PLACES store so danger fades. Soak: report PLACES records/agent + mean Danger.

- [ ] **Step 1: Read** `DeathEvent` publish (CombatSystem) + how to find the building/place of a death (victim Position → nearest building, or the killer's). Decide the witness set (agents whose `Sensed` includes victim or killer — simplest).
- [ ] **Step 2: Write the failing test** — `Headless/Sim.MemoryTests/PlaceDangerTests.cs`: rig a `DeathEvent` + a witness who sensed the victim, assert a `Danger` `PlaceObserveIntent` is emitted for that witness.
- [ ] **Step 3: Implement** `PlaceDangerSystem`; wire into `SimWorld` (after the memory systems); extend `Consolidation.Pass` (or the ConsolidationSystem path) to decay PLACES; add the soak metric.
- [ ] **Step 4: Run, verify PASS;** **Phase-1 soak (1 day): assert the activity histogram shape == baseline** (ODD unchanged) while the new places metric shows records + danger accumulating.
- [ ] **Step 5: Commit** `feat(memory): PlaceDangerSystem (witness-learned danger) + PLACES decay + metric`.

---

## Self-Review
Coverage: PlaceAtoms + write/merge (T1), general seeding + provisions (T2), danger + decay + metric (T3). Additive — no ODD read in Phase 1, so behavior must match baseline (the Phase-1 soak asserts it). Seeding via `AgentMemorySeeding.SeedPlaces` is the first `SeedX` (generalizes later). Type consistency: `AtomBag.Merge/Create/Contains`, `MemoryStore.Encode/TryGet`, `MemoryKey(building)`, `MemoryRecord` (A1/A2); `BuildingKind`/`PlaceMemory.Known`/`DeathEvent` existing. Phase 2 (ODD scoring) deferred to its own plan.
