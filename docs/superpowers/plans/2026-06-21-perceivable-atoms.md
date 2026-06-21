# Perceivable Atoms Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give each entity a native `AtomBag` perceivable surface — a `PerceivableRegistry` (sole writer) populated by owning systems stamping their own atoms, so perception can later read the bag directly with no projection layer.

**Architecture:** A `PerceivableAtoms` catalog (id authority, `Base + (int)enum` helpers), a `PerceivableRegistry` (CQRS registry applying stamp/clear intents), and stamp wiring at the spawn site (identity atoms) and `ExecutionSystem` (activity atom). Reuses memory-core A1 (`AtomBag`/`AtomTypeId`/`Atom`/`Fixed`).

**Tech Stack:** C# (.NET 10, C# 7.3-compatible), xUnit. Branch `perceivable-atoms` (off `memory-core`).

**Spec:** [`docs/superpowers/specs/2026-06-21-perceivable-atoms-design.md`](../specs/2026-06-21-perceivable-atoms-design.md).

## Global Constraints

- **Namespace:** catalog + registry data in `DaggerfallWorkshop.Sim`; the registry class in
  `DaggerfallWorkshop.Sim.Engine` (matches `MeaningsRegistry`'s split). Tests `Sim.MemoryTests`.
- **CQRS:** `PerceivableRegistry` is the **sole writer**; systems publish intents. Conflict rule:
  in `Update`, apply **clears before stamps** so a re-stamp (clear-old + stamp-new) is deterministic
  regardless of publish order.
- **Categoricals = presence atoms** (value `Fixed.One`). Bags stay sorted/unique-by-type (A1
  `AtomBag` invariant). No floats beyond `Fixed`.
- **C# 7.3**, integer/`Fixed` only.
- **Test runner:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`.

## File Structure

- `Assets/Sim/Memory/PerceivableAtoms.cs` — the catalog (Task 1)
- `Assets/Sim/Engine/Units/PerceivableRegistry.cs` — the registry + intents (Task 2)
- `Assets/Sim/Engine/SimWorld.cs` — register it (Task 3)
- `Assets/Sim/World/TownLoader.cs` — seed identity atoms at civilian spawn (Task 3)
- `Assets/Sim/Engine/Units/ExecutionSystem.cs` — stamp activity atom on change (Task 4)
- Tests: `PerceivableAtomsTests.cs`, `PerceivableRegistryTests.cs`, `PerceivableSeedingTests.cs`

---

### Task 1: `PerceivableAtoms` catalog

**Files:** Create `Assets/Sim/Memory/PerceivableAtoms.cs`, `Headless/Sim.MemoryTests/PerceivableAtomsTests.cs`

**Interfaces:**
- Produces `PerceivableAtoms` (static): `Kind(EntityKind)`, `Role(ResidentRole)`, `Race(int)`,
  `Activity(ActivityKind)` → `AtomTypeId`; `bool TryActivity(ActivityKind, out AtomTypeId)` returning
  false for `None`; range consts `KindBase=1000, RoleBase=2000, RaceBase=3000, ActivityBase=4000`.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/PerceivableAtomsTests.cs`:

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PerceivableAtomsTests
    {
        [Fact]
        public void Helpers_AreRangeCorrect_AndDistinct()
        {
            Assert.Equal(1000 + (int)EntityKind.CivilianNPC, PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
            Assert.Equal(2000 + (int)ResidentRole.Keeper, PerceivableAtoms.Role(ResidentRole.Keeper).Value);
            Assert.Equal(3000 + 7, PerceivableAtoms.Race(7).Value);
            Assert.Equal(4000 + (int)ActivityKind.Beg, PerceivableAtoms.Activity(ActivityKind.Beg).Value);
        }

        [Fact]
        public void Categories_DoNotCollide()
        {
            Assert.NotEqual(PerceivableAtoms.Kind(EntityKind.EnemyMonster), PerceivableAtoms.Role(ResidentRole.Resident));
            Assert.NotEqual(PerceivableAtoms.Role(ResidentRole.Resident), PerceivableAtoms.Activity(ActivityKind.Sleep));
        }

        [Fact]
        public void TryActivity_None_IsNoAtom()
        {
            Assert.False(PerceivableAtoms.TryActivity(ActivityKind.None, out _));
            Assert.True(PerceivableAtoms.TryActivity(ActivityKind.Work, out var a));
            Assert.Equal(PerceivableAtoms.Activity(ActivityKind.Work), a);
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL** (`PerceivableAtoms` missing).
- [ ] **Step 3: Implement** — `Assets/Sim/Memory/PerceivableAtoms.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The single id authority for perceivable atoms. Each helper maps an owning enum to a
    /// collision-free AtomTypeId via a stable Base + (int)enum offset — a convention, not a
    /// per-value mapper: the owning system picks which catalog atom to stamp. Ranges keep
    /// categories separable. All current perceivable attributes are categorical -> presence atoms.
    /// </summary>
    public static class PerceivableAtoms
    {
        public const int KindBase = 1000;
        public const int RoleBase = 2000;
        public const int RaceBase = 3000;
        public const int ActivityBase = 4000;

        public static AtomTypeId Kind(EntityKind kind) => new AtomTypeId(KindBase + (int)kind);
        public static AtomTypeId Role(ResidentRole role) => new AtomTypeId(RoleBase + (int)role);
        public static AtomTypeId Race(int raceId) => new AtomTypeId(RaceBase + raceId);
        public static AtomTypeId Activity(ActivityKind kind) => new AtomTypeId(ActivityBase + (int)kind);

        /// <summary>The current activity as an atom; false for None (no observable-activity atom).</summary>
        public static bool TryActivity(ActivityKind kind, out AtomTypeId atom)
        {
            if (kind == ActivityKind.None) { atom = AtomTypeId.None; return false; }
            atom = Activity(kind);
            return true;
        }
    }
}
```

Note: `EntityKind`/`ResidentRole`/`ActivityKind` live in `DaggerfallWorkshop.Sim` — add
`using DaggerfallWorkshop.Sim;` if the build needs it (the file is in `.Sim.Memory`).

- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(perceivable): PerceivableAtoms catalog (id authority)`.

---

### Task 2: `PerceivableRegistry`

**Files:** Create `Assets/Sim/Engine/Units/PerceivableRegistry.cs`, `Headless/Sim.MemoryTests/PerceivableRegistryTests.cs`

**Interfaces:**
- Produces intents `StampAtomIntent { EntityId Entity; AtomTypeId Type; Fixed Value }`,
  `ClearAtomIntent { EntityId Entity; AtomTypeId Type }`; class `PerceivableRegistry : Registry`
  with `void Seed(EntityId, AtomTypeId, Fixed)` (load-time direct), `AtomBag Bag(EntityId)`,
  `int Count`. `Update` applies clears then stamps, then drops despawned entities; rebuilds the
  cached sorted bag for touched entities.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/PerceivableRegistryTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PerceivableRegistryTests
    {
        static (EventBus, PerceivableRegistry) New()
        {
            var e = new EventBus();
            return (e, new PerceivableRegistry(e));
        }

        static void Tick(EventBus e, PerceivableRegistry r) { e.Tick(); r.Update(0); }

        [Fact]
        public void Stamp_AddsAtom_SortedBag()
        {
            var (e, r) = New();
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002), Value = Fixed.One });
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(1004), Value = Fixed.One });
            Tick(e, r);

            var bag = r.Bag(new EntityId(1));
            Assert.Equal(new[] { 1004, 4002 }, bag.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Restamp_ReplacesValue()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(4002), Fixed.One);
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002), Value = Fixed.FromDouble(0.5) });
            Tick(e, r);
            r.Bag(new EntityId(1)).TryGet(new AtomTypeId(4002), out var v);
            Assert.Equal(Fixed.FromDouble(0.5), v);
            Assert.Equal(1, r.Bag(new EntityId(1)).Count);
        }

        [Fact]
        public void Clear_RemovesAtom_AbsentIsNoop()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(4002), Fixed.One);
            r.Seed(new EntityId(1), new AtomTypeId(1004), Fixed.One);
            e.Publish(new ClearAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002) });
            e.Publish(new ClearAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(9999) }); // absent
            Tick(e, r);
            Assert.Equal(new[] { 1004 }, r.Bag(new EntityId(1)).Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void ClearBeforeStamp_SameType_StampWins()
        {
            // A re-stamp emits clear(old) + stamp(new) in one tick. Clears apply first, so the new value lands.
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(4002), Fixed.One);
            e.Publish(new ClearAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002) });
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002), Value = Fixed.FromDouble(0.25) });
            Tick(e, r);
            r.Bag(new EntityId(1)).TryGet(new AtomTypeId(4002), out var v);
            Assert.Equal(Fixed.FromDouble(0.25), v);
        }

        [Fact]
        public void Despawn_DropsBag()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(1004), Fixed.One);
            e.Publish(new DespawnedEvent { Entity = new EntityId(1) });
            Tick(e, r);
            Assert.Equal(0, r.Bag(new EntityId(1)).Count);
        }

        [Fact]
        public void Bag_UnknownEntity_IsEmpty()
        {
            var (_, r) = New();
            Assert.Same(AtomBag.Empty, r.Bag(new EntityId(99)));
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.** (If `EventBus.Tick`/`Publish`/`DespawnedEvent` signatures differ,
  read `Assets/Sim/Engine/EventBus.cs` + the `DespawnedEvent` definition and adjust the test
  harness — match `MeaningsRegistry`'s usage of `Events.GetEvents<DespawnedEvent>()`.)
- [ ] **Step 3: Implement** — `Assets/Sim/Engine/Units/PerceivableRegistry.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Add/replace one perceivable atom on an entity.</summary>
    public struct StampAtomIntent : IEvent { public EntityId Entity; public AtomTypeId Type; public Fixed Value; }

    /// <summary>Remove one perceivable atom from an entity (no-op if absent).</summary>
    public struct ClearAtomIntent : IEvent { public EntityId Entity; public AtomTypeId Type; }

    /// <summary>
    /// Sole writer of each entity's perceivable surface — its broadcast bag of atoms (identity +
    /// observable state). Owning systems stamp their own atoms via intents; perception reads Bag()
    /// directly. Clears apply before stamps within a tick so a re-stamp is deterministic.
    /// </summary>
    public sealed class PerceivableRegistry : Registry
    {
        readonly Dictionary<EntityId, Dictionary<int, Fixed>> _work = new Dictionary<EntityId, Dictionary<int, Fixed>>();
        readonly Dictionary<EntityId, AtomBag> _bags = new Dictionary<EntityId, AtomBag>();

        public PerceivableRegistry(EventBus events) : base(events) { }

        /// <summary>Load-time direct stamp (no intent), mirroring IdentityRegistry.Seed.</summary>
        public void Seed(EntityId id, AtomTypeId type, Fixed value)
        {
            Set(id, type, value);
            Rebuild(id);
        }

        public override void Update(long tick)
        {
            var dirty = new HashSet<EntityId>();

            var clears = Events.GetEvents<ClearAtomIntent>();   // clears first
            for (int i = 0; i < clears.Length; i++)
                if (_work.TryGetValue(clears[i].Entity, out var m) && m.Remove(clears[i].Type.Value))
                    dirty.Add(clears[i].Entity);

            var stamps = Events.GetEvents<StampAtomIntent>();
            for (int i = 0; i < stamps.Length; i++)
            {
                Set(stamps[i].Entity, stamps[i].Type, stamps[i].Value);
                dirty.Add(stamps[i].Entity);
            }

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
            {
                _work.Remove(gone[i].Entity);
                _bags.Remove(gone[i].Entity);
                dirty.Remove(gone[i].Entity);
            }

            foreach (var id in dirty) Rebuild(id);
        }

        void Set(EntityId id, AtomTypeId type, Fixed value)
        {
            if (!_work.TryGetValue(id, out var m)) { m = new Dictionary<int, Fixed>(); _work[id] = m; }
            m[type.Value] = value;
        }

        void Rebuild(EntityId id)
        {
            if (!_work.TryGetValue(id, out var m) || m.Count == 0) { _bags[id] = AtomBag.Empty; return; }
            var atoms = new List<Atom>(m.Count);
            foreach (var kv in m) atoms.Add(new Atom(new AtomTypeId(kv.Key), kv.Value));
            _bags[id] = AtomBag.Create(atoms);   // sorts + dedups by type
        }

        // --- read API ---
        public AtomBag Bag(EntityId id) => _bags.TryGetValue(id, out var b) ? b : AtomBag.Empty;
        public int Count => _bags.Count;
    }
}
```

- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(perceivable): PerceivableRegistry — sole-writer atom bags`.

---

### Task 3: Register in `SimWorld` + seed identity atoms at spawn

**Files:** Modify `Assets/Sim/Engine/SimWorld.cs`, `Assets/Sim/World/TownLoader.cs`; create
`Headless/Sim.MemoryTests/PerceivableSeedingTests.cs`.

**Interfaces:** `SimWorld.Perceivable` field, constructed + added to the registries array. At the
civilian spawn (`TownLoader.cs:~525`, after `world.Identity.Seed(...)`), seed the identity atoms.

- [ ] **Step 1: Read the exact spawn site.** Open `Assets/Sim/World/TownLoader.cs` around line 525.
  Confirm `id`, the `IdentityData` (`Kind`, `Race`), and whether residency/role is known there
  (`world.Residency`). Note the exact local variable names.

- [ ] **Step 2: Add `Perceivable` to `SimWorld`.** In `Assets/Sim/Engine/SimWorld.cs`:
  - Add a field beside the other registries: `public readonly PerceivableRegistry Perceivable;`
  - In the constructor (with the other `new …Registry(e)` lines, ~line 89): `Perceivable = new PerceivableRegistry(e);`
  - Add `Perceivable` to the `registries` array (~line 99).

- [ ] **Step 3: Write the failing integration test** — `Headless/Sim.MemoryTests/PerceivableSeedingTests.cs`.
  Gated on ARENA2 like the existing region-load test (`FactionRegionLoadTests`); skip if
  `DAGGERFALL_ARENA2` unset. Seed a small town via `SimBoot.CreateTown`, `ApplySeed()`, then assert a
  sampled `CivilianNPC` entity's `Perceivable.Bag(id)` contains `PerceivableAtoms.Kind(EntityKind.CivilianNPC)`
  and a `Race(...)` atom. (Model the harness on `Headless/Sim.FactionTests/FactionRegionLoadTests.cs`
  — read it first for the exact `SimBoot` call + ARENA2 gate.)

```csharp
// Skeleton — fill the SimBoot call + sampling from FactionRegionLoadTests' pattern:
//   string arena2 = System.Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2");
//   if (string.IsNullOrEmpty(arena2)) return;   // skip when data absent
//   var world = SimBoot.CreateTown(arena2, "Daggerfall", "Gothway Garden", 600f, 12345);
//   world.ApplySeed();
//   foreach (var kv in world.Identity.All) if (kv.Value.Kind == EntityKind.CivilianNPC) { sample = kv.Key; break; }
//   var bag = world.Perceivable.Bag(sample);
//   Assert.True(bag.Contains(PerceivableAtoms.Kind(EntityKind.CivilianNPC)));
//   Assert.True(bag.Atoms.Any(a => a.Type.Value >= PerceivableAtoms.RaceBase && a.Type.Value < PerceivableAtoms.RaceBase + 1000));
```

- [ ] **Step 4: Wire the seeding.** At `TownLoader.cs:~525`, immediately after the `Identity.Seed`
  call, add (using the confirmed locals):

```csharp
            world.Perceivable.Seed(id, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);
            if (race >= 0) world.Perceivable.Seed(id, PerceivableAtoms.Race(race), Fixed.One);
            // role atom: if residency/role is known at this point, seed PerceivableAtoms.Role(role) too.
```

  Add `using DaggerfallWorkshop.Sim.Memory;` to `TownLoader.cs` if absent. If the role isn't known
  at this spawn line, seed only Kind+Race here and leave Role to its owner; note it in the commit.

- [ ] **Step 5: Run the integration test (with ARENA2 set), verify PASS;** run the full memory suite
  to confirm no regressions.
- [ ] **Step 6: Commit** `feat(perceivable): register registry + seed identity atoms at spawn`.

---

### Task 4: Activity atom via `ExecutionSystem` (on change) + despawn cleanup

**Files:** Modify `Assets/Sim/Engine/Units/ExecutionSystem.cs`; add a unit test to
`PerceivableRegistryTests.cs` (or a focused harness).

**Interfaces:** When `ExecutionSystem` emits a `BehaviorSetIntent` that changes the activity, it also
emits `ClearAtomIntent(Activity(old))` + `StampAtomIntent(Activity(new))` (skipping `None`).

- [ ] **Step 1: Read `ExecutionSystem.cs`.** Find each `Events.Publish(new BehaviorSetIntent { … Activity = X … })`
  and where the prior activity is known (read `_behavior.TryGet(entity, out var b)` → `b.Activity`).
  Confirm `ExecutionSystem` can take a `PerceivableRegistry` (it doesn't need to *read* it — it only
  publishes intents, so no new dependency is needed; it just publishes the two atom intents).

- [ ] **Step 2: Write the failing test.** A focused unit test: drive a behavior change through the
  perceivable path (publish the activity stamp/clear the way ExecutionSystem will), assert the bag's
  activity atom swapped old→new. (If ExecutionSystem is hard to unit-drive, assert via an
  ARENA2-gated soak-step integration test instead — pick the cheaper.)

- [ ] **Step 3: Wire it.** At each activity transition in `ExecutionSystem`, when `newActivity !=
  oldActivity`:

```csharp
            if (PerceivableAtoms.TryActivity(oldActivity, out var oldAtom))
                Events.Publish(new ClearAtomIntent { Entity = entity, Type = oldAtom });
            if (PerceivableAtoms.TryActivity(newActivity, out var newAtom))
                Events.Publish(new StampAtomIntent { Entity = entity, Type = newAtom, Value = Fixed.One });
```

  Add `using DaggerfallWorkshop.Sim.Memory;`. Only emit when the activity actually changes (avoid
  per-tick churn — the spec's on-change rule).

- [ ] **Step 4: Run, verify PASS;** full memory suite green.
- [ ] **Step 5: Commit** `feat(perceivable): stamp activity atom on behavior change`.

---

## Self-Review

**Spec coverage:** catalog (T1), registry + intents + conflict rule (T2), SimWorld registration +
identity seeding (T3), activity-on-change + despawn cleanup (T2/T4). Vocabulary kind/role/race/activity
all present-valued. Out-of-scope items (memory reading, arousal, graded atoms) correctly absent.

**Type consistency:** `PerceivableAtoms.*` → `AtomTypeId` consumed by intents + tests; `Fixed.One`
for presence; `AtomBag.Create` keeps the sorted/unique invariant; `DespawnedEvent` reused for cleanup
(same as `MeaningsRegistry`). `Bag()` returns `AtomBag.Empty` for unknowns.

**Determinism:** clears-before-stamps in `Update` makes re-stamp order-independent; `AtomBag.Create`
sorts; no floats beyond `Fixed`.

**Risk note (executor):** T3/T4 touch live files (`TownLoader`, `SimWorld`, `ExecutionSystem`) whose
exact locals must be confirmed by reading at execution — each task's Step 1 does that read first.
Monster (`KindMonster`) and birth identity stamping are deferred to a follow-up once the civilian +
activity path is proven (keeps the first integration small and reviewable).
