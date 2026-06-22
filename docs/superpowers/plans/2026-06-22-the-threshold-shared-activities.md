# The Threshold — Shared Activities, Queues & Percept-Driven Preemption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make agents respect contended shared affordances — they queue at a shopkeeper, take turns, and balk when something more important comes up — built on a generic shared-activity instance model with one protocol (`ServiceQueue`) and a percept-driven preemption loop.

**Architecture:** The sim is a CQRS engine (`Assets/Sim/Engine`): each tick, `EventBus` flips, registries apply last tick's intents, then systems read settled state and publish new intents. We add one registry (`SharedActivityRegistry`, holding live queue instances), one system (`SharedActivitySystem`, bridging queue membership to behaviour phase), a new `ActivityPhase.Queued`, and extend `ExecutionSystem`/`MovementSystem`/`OddSystem`. The queue gates *who* reaches `ActivityPhase.Doing`, so `EconomySystem.PaySale` serialises with zero transaction changes. Preemption is "the agent re-values its commitment against the best available action and switches if meaningfully better"; sleep is the carve-out (suppress preemption, wake on a hit).

**Tech Stack:** C# / .NET 10, namespace `DaggerfallWorkshop.Sim` (+ `.Engine`, `.Memory`). Tests: xUnit in `Headless/Sim.MemoryTests`. Viewer: `Headless/Sim.Web` (ASP.NET minimal API + `wwwroot/town3d.html` WebGL client).

## Global Constraints

- **CQRS discipline:** systems NEVER mutate registry state directly. A system reads registries (last tick's settled state) and calls `Events.Publish(SomeIntent)`. The registry's `Update(long tick)` applies intents. Intents published in tick N are visible in tick N+1.
- **Determinism:** no wall-clock, no `System.Random`. Use the existing `Hash(value, salt)` helper in `OddSystem` for any per-agent jitter. Tick order is fixed by the `systems` array in `SimWorld.cs`.
- **Wire stability:** `ActivityPhase` and `ActivityKind` ordinals are serialised to the web client (`Program.cs:384` compact rows `[id, x, z, activity, phase, yaw, kind, groundY]`). APPEND new enum members at the end; never reorder.
- **Test framework:** xUnit `[Fact]`. Run from `/home/uggeli/slopfall/Headless`: `dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj`. The `Sim.World` project compiles `Assets/Sim/**`; the test project references it.
- **Stale suite:** the broader sim test suite is being rewritten — write NEW tests for everything here; do not rely on pre-existing integration tests for confidence.
- **Tuning constants** (`QueueSpacing`, `PreemptEveryTicks`, `Hysteresis`, shop `Capacity = 1`) are first-cut values flagged in the spec's open questions. Define them as named constants so the soak can tune them.
- **Performance budget: a single sim tick must complete in ≤ 100 ms** at the milestone's target population (region-scale soak). This is a HARD cap. The chief risk is Pillar B: percept-driven preemption re-runs `OddSystem.Decide` (GatherAds + scoring) for committed agents. `PreemptEveryTicks` staggering keeps that to ~1/N of committed agents per tick — if a tick exceeds 100 ms, **raise `PreemptEveryTicks`** (cheaper, coarser balking) before anything else. The soak (Task 13) measures and gates this.

---

## Testing Approach (codebase idiom — there is NO `TestWorld`)

Verified against the existing suite. Match these patterns exactly; do NOT invent an integration harness.

1. **Registry / low-dependency system tests → a per-test `Rig`.** Mirror `MemoryWriteSystemTests`/`ConsolidationSystemTests`: a `sealed class Rig` holding `EventBus E`, the few registries the unit needs, and the system under test. `Step()` = `E.Tick(); reg.Update(0); sys.Update(0);`. Drain published intents with a helper: `Next() { E.Tick(); return E.GetEvents<T>().ToArray(); }`. Assert on **published intents** and **registry reads**, never on private state. Tasks 2, 4, 5, 6, 9 use this.

2. **`OddSystem` logic → pure static helpers, tested directly.** `OddSystem` has ~24 constructor dependencies and is NEVER instance-constructed in a test. The established idiom (`OddSystem.PlaceAversion`, `OddSystem.ProvisionPreference`, `OddSystem.BuildSnapshot` in `PlaceScoringTests`/`OddSnapshotTests`) is: **extract the decision rule into a pure `public static` method on `OddSystem`, unit-test the method, and call it from `Decide`/`Update`.** Tasks 7, 10, 11 each add one such helper (`ShouldLeaveQueue`, `ShouldSwitchCommitment`, `SleepShouldWake`) and test it purely. The wiring into `Decide`/`Update` carries no new logic of its own and is covered end-to-end by the Task 13 soak.

3. **Multi-agent / economy invariants → the soak (Task 13), not a unit test.** The "never more than `Capacity` in `Doing`" and "only the served agent pays" claims are emergent from the wired sim; assert them in the `Sim.Host --soak` histogram. (The capacity invariant is ALSO proven structurally at the registry level in Task 2.)

Run command for all unit tests, from `/home/uggeli/slopfall/Headless`: `dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj` (and `Sim.SpatialTests/Sim.SpatialTests.csproj` where noted).

---

## File Structure

**New files:**
- `Assets/Sim/Engine/Units/SharedActivityRegistry.cs` — `QueueAnchor`, `QueueJoinIntent`, `QueueLeaveIntent`, `SharedActivityInstance`, `SharedActivityRegistry`. Holds live queue instances; applies join/leave/promote; exposes read API. The heart of Pillar A.
- `Assets/Sim/Engine/Units/SharedActivitySystem.cs` — bridges queue membership to behaviour: flips promoted (served) agents `Queued → Doing`, assigns waiters their slot positions.
- `Assets/Sim/Engine/Units/SomaticPerceptSystem.cs` — stamps the agent's own need axes into its own `AtomBag` as somatic atoms (Pillar B seam).
- `Assets/Sim/Memory/SomaticAtoms.cs` — `AtomTypeId` constants for somatic percepts.

**Modified files:**
- `Assets/Sim/Registries/BehaviorRegistry.cs` — add `ActivityPhase.Queued`.
- `Assets/Sim/Engine/Units/ExecutionSystem.cs` — route queued-affordance arrivals to `Queued` + `QueueJoinIntent` instead of `Doing`.
- `Assets/Sim/Engine/Units/MovementSystem.cs` — also walk `Queued` agents toward their slot (without re-publishing arrival).
- `Assets/Sim/Engine/Units/OddSystem.cs` — leave the queue on re-decide-to-a-different-target; throttled preemption with hysteresis; sleep gating + wake on `DamageEvent`.
- `Assets/Sim/Engine/SimWorld.cs` — instantiate the registry + system; add both to the arrays; inject the registry into `ExecutionSystem`, `MovementSystem`, `OddSystem`, `SharedActivitySystem`.
- `Headless/Sim.Web/Program.cs` — inspect endpoint adds queue position from the registry. (The compact phase field already ships.)
- `Headless/Sim.Web/wwwroot/town3d.html` (+ client modules) — render `Queued` phase distinctly.

**Build order rationale:** runtime first (Tasks 1–7) gives a working, draining queue using ODD's *existing* hourly/dawn re-decide; preemption (Tasks 10–12) is layered on once there's a queue to balk from; viewer + soak (13–14) last.

---

## Task 1: Add the `Queued` activity phase

**Files:**
- Modify: `Assets/Sim/Registries/BehaviorRegistry.cs:37-41`
- Test: `Headless/Sim.MemoryTests/ActivityPhaseTests.cs` (create)

**Interfaces:**
- Produces: `ActivityPhase.Queued` (ordinal 2).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.MemoryTests
{
    public class ActivityPhaseTests
    {
        [Fact]
        public void Queued_IsAppended_WireStable()
        {
            Assert.Equal(0, (int)ActivityPhase.Moving);
            Assert.Equal(1, (int)ActivityPhase.Doing);
            Assert.Equal(2, (int)ActivityPhase.Queued);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter ActivityPhaseTests`
Expected: FAIL — `ActivityPhase` does not contain a definition for `Queued`.

- [ ] **Step 3: Add the enum member**

In `Assets/Sim/Registries/BehaviorRegistry.cs`, change the enum to:

```csharp
public enum ActivityPhase
{
    Moving,      // walking toward TargetX/Z; MovementSystem drives Position
    Doing,       // at the spot; NeedsSystem applies the activity's deltas
    Queued,      // arrived at a serviced affordance; holding for a turn. Appended for wire stability.
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter ActivityPhaseTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Registries/BehaviorRegistry.cs Headless/Sim.MemoryTests/ActivityPhaseTests.cs
git commit -m "feat(threshold): add ActivityPhase.Queued (wire-stable append)"
```

---

## Task 2: `SharedActivityRegistry` — types, join/leave/promote, reads

**Files:**
- Create: `Assets/Sim/Engine/Units/SharedActivityRegistry.cs`
- Test: `Headless/Sim.MemoryTests/SharedActivityRegistryTests.cs` (create)

**Interfaces:**
- Consumes: `EntityId`, `IEvent`, `Registry`, `EventBus`, `ActivityKind` (existing in `DaggerfallWorkshop.Sim` / `.Engine`).
- Produces:
  - `struct QueueAnchor { int Building; ActivityKind Verb; }` with value equality.
  - `struct QueueJoinIntent : IEvent { EntityId Agent; QueueAnchor Anchor; int Capacity; float AnchorX, AnchorZ; }`
  - `struct QueueLeaveIntent : IEvent { EntityId Agent; }`
  - `class SharedActivityInstance { QueueAnchor Anchor; int Capacity; float AnchorX, AnchorZ; List<EntityId> Waiters; List<EntityId> Served; }`
  - `class SharedActivityRegistry : Registry` with reads: `bool TryGet(QueueAnchor, out SharedActivityInstance)`, `bool AnchorOf(EntityId, out QueueAnchor)`, `bool IsServed(EntityId)`, `int PositionOf(EntityId)` (waiter index, `-1` if not waiting), `IReadOnlyList<EntityId> ServedSnapshot()` (all currently-served agents).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class SharedActivityRegistryTests
    {
        static (EventBus, SharedActivityRegistry) New()
        {
            var e = new EventBus();
            return (e, new SharedActivityRegistry(e));
        }
        static void Tick(EventBus e, SharedActivityRegistry r) { e.Tick(); r.Update(0); }
        static QueueAnchor Shop => new QueueAnchor(7, ActivityKind.Buy);
        static QueueJoinIntent Join(int agent) => new QueueJoinIntent
            { Agent = new EntityId(agent), Anchor = Shop, Capacity = 1, AnchorX = 10, AnchorZ = 20 };

        [Fact]
        public void Join_FirstAgent_IsPromotedToServed()
        {
            var (e, r) = New();
            e.Publish(Join(1));
            Tick(e, r);
            Assert.True(r.IsServed(new EntityId(1)));
            Assert.Equal(-1, r.PositionOf(new EntityId(1)));   // served, not waiting
        }

        [Fact]
        public void Join_BeyondCapacity_Waits_InOrder()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(2)); e.Publish(Join(3));
            Tick(e, r);
            Assert.True(r.IsServed(new EntityId(1)));            // capacity 1
            Assert.Equal(0, r.PositionOf(new EntityId(2)));      // head of line
            Assert.Equal(1, r.PositionOf(new EntityId(3)));
        }

        [Fact]
        public void Leave_Served_PromotesNextHead()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(2)); Tick(e, r);
            e.Publish(new QueueLeaveIntent { Agent = new EntityId(1) }); Tick(e, r);
            Assert.False(r.IsServed(new EntityId(1)));
            Assert.True(r.IsServed(new EntityId(2)));
        }

        [Fact]
        public void Leave_MidLine_ClosesGap()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(2)); e.Publish(Join(3)); Tick(e, r);
            e.Publish(new QueueLeaveIntent { Agent = new EntityId(2) }); Tick(e, r);
            Assert.Equal(0, r.PositionOf(new EntityId(3)));      // 3 moves up
        }

        [Fact]
        public void Leave_LastMember_ReapsInstance()
        {
            var (e, r) = New();
            e.Publish(Join(1)); Tick(e, r);
            e.Publish(new QueueLeaveIntent { Agent = new EntityId(1) }); Tick(e, r);
            Assert.False(r.TryGet(Shop, out _));
        }

        [Fact]
        public void AnchorOf_TracksMembership()
        {
            var (e, r) = New();
            e.Publish(Join(1)); Tick(e, r);
            Assert.True(r.AnchorOf(new EntityId(1), out var a));
            Assert.Equal(Shop, a);
            Assert.False(r.AnchorOf(new EntityId(99), out _));
        }

        [Fact]
        public void Join_Idempotent_NoDuplicateMembership()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(1)); Tick(e, r);
            Assert.True(r.IsServed(new EntityId(1)));
            Assert.Single(r.ServedSnapshot());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter SharedActivityRegistryTests`
Expected: FAIL — `QueueAnchor`/`SharedActivityRegistry` not found.

- [ ] **Step 3: Write the implementation**

Create `Assets/Sim/Engine/Units/SharedActivityRegistry.cs`:

```csharp
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Identifies a serviced affordance: a building + the verb you queue for.
    public struct QueueAnchor : System.IEquatable<QueueAnchor>
    {
        public int Building;
        public ActivityKind Verb;
        public QueueAnchor(int building, ActivityKind verb) { Building = building; Verb = verb; }
        public bool Equals(QueueAnchor o) => Building == o.Building && Verb == o.Verb;
        public override bool Equals(object o) => o is QueueAnchor q && Equals(q);
        public override int GetHashCode() => (Building * 397) ^ (int)Verb;
    }

    /// Intent: "this agent has arrived and wants a turn at this affordance."
    public struct QueueJoinIntent : IEvent
    {
        public EntityId Agent;
        public QueueAnchor Anchor;
        public int Capacity;            // how many the affordance serves at once
        public float AnchorX, AnchorZ;  // the affordance's standing point (for slot geometry)
    }

    /// Intent: "this agent is leaving the line / counter (served, balked, or re-decided away)."
    public struct QueueLeaveIntent : IEvent { public EntityId Agent; }

    /// A live serviced-affordance instance. The ServiceQueue protocol: ordered
    /// Waiters, up to Capacity in Served at once.
    public sealed class SharedActivityInstance
    {
        public QueueAnchor Anchor;
        public int Capacity;
        public float AnchorX, AnchorZ;
        public readonly List<EntityId> Waiters = new List<EntityId>();
        public readonly List<EntityId> Served = new List<EntityId>();
    }

    /// Holds live shared-activity instances keyed by anchor. Apply order each tick:
    /// leaves (free slots), then joins (append waiters), then promote (fill free
    /// Served slots from the head of the line), then reap empties. CQRS: systems
    /// publish Join/Leave intents; reads are settled state.
    public sealed class SharedActivityRegistry : Registry
    {
        readonly Dictionary<QueueAnchor, SharedActivityInstance> _byAnchor =
            new Dictionary<QueueAnchor, SharedActivityInstance>();
        readonly Dictionary<EntityId, QueueAnchor> _ofAgent =
            new Dictionary<EntityId, QueueAnchor>();
        static readonly List<EntityId> NoAgents = new List<EntityId>();

        public SharedActivityRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (ref readonly var lv in Events.GetEvents<QueueLeaveIntent>())
                RemoveAgent(lv.Agent);

            foreach (ref readonly var jn in Events.GetEvents<QueueJoinIntent>())
                JoinAgent(jn);

            // Promote: deterministic — fill free Served slots from the head.
            foreach (var inst in _byAnchor.Values)
                while (inst.Served.Count < inst.Capacity && inst.Waiters.Count > 0)
                {
                    var head = inst.Waiters[0];
                    inst.Waiters.RemoveAt(0);
                    inst.Served.Add(head);
                }

            // Reap empties.
            if (_pendingReap.Count > 0) _pendingReap.Clear();
            foreach (var kv in _byAnchor)
                if (kv.Value.Served.Count == 0 && kv.Value.Waiters.Count == 0)
                    _pendingReap.Add(kv.Key);
            for (int i = 0; i < _pendingReap.Count; i++) _byAnchor.Remove(_pendingReap[i]);
        }
        readonly List<QueueAnchor> _pendingReap = new List<QueueAnchor>();

        void JoinAgent(in QueueJoinIntent jn)
        {
            if (_ofAgent.ContainsKey(jn.Agent)) return;     // idempotent
            if (!_byAnchor.TryGetValue(jn.Anchor, out var inst))
            {
                inst = new SharedActivityInstance
                {
                    Anchor = jn.Anchor, Capacity = jn.Capacity < 1 ? 1 : jn.Capacity,
                    AnchorX = jn.AnchorX, AnchorZ = jn.AnchorZ,
                };
                _byAnchor[jn.Anchor] = inst;
            }
            inst.Waiters.Add(jn.Agent);
            _ofAgent[jn.Agent] = jn.Anchor;
        }

        void RemoveAgent(EntityId agent)
        {
            if (!_ofAgent.TryGetValue(agent, out var anchor)) return;
            _ofAgent.Remove(agent);
            if (!_byAnchor.TryGetValue(anchor, out var inst)) return;
            inst.Served.Remove(agent);
            inst.Waiters.Remove(agent);
        }

        public bool TryGet(QueueAnchor anchor, out SharedActivityInstance inst)
            => _byAnchor.TryGetValue(anchor, out inst);

        public bool AnchorOf(EntityId agent, out QueueAnchor anchor)
            => _ofAgent.TryGetValue(agent, out anchor);

        public bool IsServed(EntityId agent)
            => _ofAgent.TryGetValue(agent, out var a)
               && _byAnchor.TryGetValue(a, out var inst) && inst.Served.Contains(agent);

        public int PositionOf(EntityId agent)
            => _ofAgent.TryGetValue(agent, out var a) && _byAnchor.TryGetValue(a, out var inst)
               ? inst.Waiters.IndexOf(agent) : -1;

        public IReadOnlyList<EntityId> ServedSnapshot()
        {
            var all = new List<EntityId>();
            foreach (var inst in _byAnchor.Values) all.AddRange(inst.Served);
            return all.Count == 0 ? NoAgents : all;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter SharedActivityRegistryTests`
Expected: PASS (all 7 facts).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Engine/Units/SharedActivityRegistry.cs Headless/Sim.MemoryTests/SharedActivityRegistryTests.cs
git commit -m "feat(threshold): SharedActivityRegistry — ServiceQueue membership (join/leave/promote)"
```

---

## Task 3: Wire `SharedActivityRegistry` into `SimWorld`

**Files:**
- Modify: `Assets/Sim/Engine/SimWorld.cs` (registry field + construction + registries array)

**Interfaces:**
- Consumes: `SharedActivityRegistry` (Task 2).
- Produces: `SimWorld.SharedActivity` public field so systems/endpoints can read it.

- [ ] **Step 1: Add the field and construct it**

In `SimWorld.cs`, alongside the other registry fields (e.g. near `Occupancy`), add:

```csharp
public readonly SharedActivityRegistry SharedActivity;
```

In the constructor, where `Occupancy = new OccupancyRegistry(e);` appears (around line 79), add:

```csharp
SharedActivity = new SharedActivityRegistry(e);
```

- [ ] **Step 2: Register it in the registries array**

Find the `registries` array passed to `new SimEngine(e, registries, systems)` and add `SharedActivity` to it (order among registries does not matter — they all apply before systems run):

```csharp
// ... existing registries ...,
SharedActivity,
```

- [ ] **Step 3: Build to verify it compiles**

Run: `cd /home/uggeli/slopfall && dotnet build Headless/Sim.World/Sim.World.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Assets/Sim/Engine/SimWorld.cs
git commit -m "feat(threshold): register SharedActivityRegistry in SimWorld"
```

---

## Task 4: `SharedActivitySystem` — bridge served agents to `Doing`, place waiters

**Files:**
- Create: `Assets/Sim/Engine/Units/SharedActivitySystem.cs`
- Modify: `Assets/Sim/Engine/SimWorld.cs` (instantiate + add to systems array)
- Test: `Headless/Sim.MemoryTests/SharedActivitySystemTests.cs` (create)

**Interfaces:**
- Consumes: `SharedActivityRegistry` (Task 2), `BehaviorRegistry` (`_behavior.TryGet`, `BehaviorSetIntent`, `BehaviorData`), `ActivityCatalog.SpecFor(ActivityKind).DurationMinutes`.
- Produces: `class SharedActivitySystem : SimSystem`. Behaviour effects: a served agent still in `ActivityPhase.Queued` is flipped to `Doing` with `RemainingGameMinutes = spec.DurationMinutes`; each waiter's `TargetX/TargetZ` is set to its slot `(AnchorX, AnchorZ - position * QueueSpacing)`.
- Constant: `const float QueueSpacing = 1.5f;`

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class SharedActivitySystemTests
    {
        [Fact]
        public void ServedAgent_StillQueued_IsFlippedToDoing()
        {
            var e = new EventBus();
            var shared = new SharedActivityRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var sys = new SharedActivitySystem(e, shared, behavior);

            // Agent arrived & queued for Buy.
            e.Publish(new BehaviorSetIntent { Id = new EntityId(1), Data = new BehaviorData
                { Activity = ActivityKind.Buy, Phase = ActivityPhase.Queued, TargetBuilding = 7,
                  TargetX = 10, TargetZ = 20 } });
            e.Publish(new QueueJoinIntent { Agent = new EntityId(1),
                Anchor = new QueueAnchor(7, ActivityKind.Buy), Capacity = 1, AnchorX = 10, AnchorZ = 20 });
            e.Tick(); behavior.Update(0); shared.Update(0);   // apply behaviour + promote to Served

            sys.Update(1);                                    // system sees Served-but-Queued → publishes flip
            e.Tick(); behavior.Update(1);                     // apply the flip

            behavior.TryGet(new EntityId(1), out var b);
            Assert.Equal(ActivityPhase.Doing, b.Phase);
            Assert.True(b.RemainingGameMinutes > 0);          // service clock started
        }

        [Fact]
        public void Waiter_TargetSetToSlot()
        {
            var e = new EventBus();
            var shared = new SharedActivityRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var sys = new SharedActivitySystem(e, shared, behavior);

            foreach (var id in new[] { 1, 2 })
                e.Publish(new BehaviorSetIntent { Id = new EntityId(id), Data = new BehaviorData
                    { Activity = ActivityKind.Buy, Phase = ActivityPhase.Queued, TargetBuilding = 7,
                      TargetX = 10, TargetZ = 20 } });
            foreach (var id in new[] { 1, 2 })
                e.Publish(new QueueJoinIntent { Agent = new EntityId(id),
                    Anchor = new QueueAnchor(7, ActivityKind.Buy), Capacity = 1, AnchorX = 10, AnchorZ = 20 });
            e.Tick(); behavior.Update(0); shared.Update(0);   // agent 1 served, agent 2 waiting at pos 0

            sys.Update(1);
            e.Tick(); behavior.Update(1);

            behavior.TryGet(new EntityId(2), out var w);
            Assert.Equal(20f - 0 * 1.5f, w.TargetZ);          // slot 0 = AnchorZ - 0*spacing
            Assert.Equal(ActivityPhase.Queued, w.Phase);      // still waiting
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter SharedActivitySystemTests`
Expected: FAIL — `SharedActivitySystem` not found.

- [ ] **Step 3: Write the implementation**

Create `Assets/Sim/Engine/Units/SharedActivitySystem.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Engine
{
    /// Bridges ServiceQueue membership (SharedActivityRegistry) to agent behaviour:
    ///  - a Served agent still in Queued phase is flipped to Doing (service begins,
    ///    its clock reset to the activity's full duration — waiting doesn't erode it);
    ///  - each Waiter is steered to its slot position so the line is physical & visible.
    /// CQRS: reads registries, publishes BehaviorSetIntent only.
    public sealed class SharedActivitySystem : SimSystem
    {
        public const float QueueSpacing = 1.5f;   // metres between people in line (tunable)

        readonly SharedActivityRegistry _shared;
        readonly BehaviorRegistry _behavior;

        public SharedActivitySystem(EventBus events, SharedActivityRegistry shared, BehaviorRegistry behavior)
            : base(events)
        {
            _shared = shared;
            _behavior = behavior;
        }

        public override void Update(long tick)
        {
            // Promote: Served agents whose behaviour still says Queued → start service.
            var served = _shared.ServedSnapshot();
            for (int i = 0; i < served.Count; i++)
            {
                var id = served[i];
                if (!_behavior.TryGet(id, out var b) || b.Phase != ActivityPhase.Queued) continue;
                var spec = ActivityCatalog.SpecFor(b.Activity);
                Events.Publish(new BehaviorSetIntent
                {
                    Id = id,
                    Data = new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Doing,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = b.TargetX,
                        TargetZ = b.TargetZ,
                        RemainingGameMinutes = spec != null ? spec.DurationMinutes : 0,
                        SinceDecisionGameMinutes = 0,
                        TargetItem = b.TargetItem,
                    }
                });
            }

            // Place waiters at their slot so the line is visible and shuffles forward.
            foreach (var kv in _behavior.All)
            {
                var id = kv.Key;
                var b = kv.Value;
                if (b.Phase != ActivityPhase.Queued) continue;
                int pos = _shared.PositionOf(id);
                if (pos < 0) continue;                          // not waiting (e.g. served this tick)
                if (!_shared.AnchorOf(id, out var anchor)) continue;
                if (!_shared.TryGet(anchor, out var inst)) continue;
                float slotX = inst.AnchorX;
                float slotZ = inst.AnchorZ - pos * QueueSpacing;
                if (b.TargetX == slotX && b.TargetZ == slotZ) continue;   // already targeting it
                Events.Publish(new BehaviorSetIntent
                {
                    Id = id,
                    Data = new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Queued,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = slotX,
                        TargetZ = slotZ,
                        RemainingGameMinutes = b.RemainingGameMinutes,
                        SinceDecisionGameMinutes = b.SinceDecisionGameMinutes,
                        TargetItem = b.TargetItem,
                    }
                });
            }
        }
    }
}
```

- [ ] **Step 4: Wire it into `SimWorld`**

In `SimWorld.cs`, add to the `systems` array **after** `ExecutionSystem` and **before** `MovementSystem` (so promotion/slot intents are published before movement reads targets next tick):

```csharp
new SharedActivitySystem(e, SharedActivity, Behavior),
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter SharedActivitySystemTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Engine/Units/SharedActivitySystem.cs Assets/Sim/Engine/SimWorld.cs Headless/Sim.MemoryTests/SharedActivitySystemTests.cs
git commit -m "feat(threshold): SharedActivitySystem — promote served→Doing, place waiters in line"
```

---

## Task 5: Route `Buy` arrivals into the queue (`ExecutionSystem`)

**Files:**
- Modify: `Assets/Sim/Engine/Units/ExecutionSystem.cs:41-60` (the arrivals loop) + constructor
- Modify: `Assets/Sim/Engine/SimWorld.cs` (pass `SharedActivity` to `ExecutionSystem`)
- Test: `Headless/Sim.MemoryTests/ExecutionQueueRoutingTests.cs` (create)

**Interfaces:**
- Consumes: `ArrivedAtTargetEvent { EntityId Entity }`, `BehaviorData`, `QueueJoinIntent` (Task 2).
- Produces: on arrival, if `b.Activity == ActivityKind.Buy` → publish `BehaviorSetIntent` with `Phase = Queued` AND `QueueJoinIntent { Capacity = 1, Anchor = (TargetBuilding, Buy), AnchorX/Z = TargetX/Z }`. All other activities keep the existing `→ Doing` behaviour.
- Constant: `const int ShopCapacity = 1;`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class ExecutionQueueRoutingTests
    {
        [Fact]
        public void BuyArrival_GoesToQueued_AndJoins()
        {
            var e = new EventBus();
            var intent = new IntentRegistry(e);          // existing registry ExecutionSystem needs
            var behavior = new BehaviorRegistry(e);
            var position = new PositionRegistry(e);
            var clock = new WorldClockRegistry(e);
            var shared = new SharedActivityRegistry(e);
            var sys = new ExecutionSystem(e, intent, behavior, position, clock, shared);

            e.Publish(new BehaviorSetIntent { Id = new EntityId(1), Data = new BehaviorData
                { Activity = ActivityKind.Buy, Phase = ActivityPhase.Moving, TargetBuilding = 7,
                  TargetX = 10, TargetZ = 20 } });
            e.Tick(); behavior.Update(0);

            e.Publish(new ArrivedAtTargetEvent { Entity = new EntityId(1) });
            e.Tick(); behavior.Update(1);                 // make the arrival event visible to the system
            sys.Update(1);
            e.Tick(); behavior.Update(2); shared.Update(2);

            behavior.TryGet(new EntityId(1), out var b);
            Assert.Equal(ActivityPhase.Queued, b.Phase);
            Assert.True(shared.AnchorOf(new EntityId(1), out var a));
            Assert.Equal(new QueueAnchor(7, ActivityKind.Buy), a);
        }
    }
}
```

> Note: the exact registry type names for `intent`, `position`, `clock` are whatever `ExecutionSystem`'s current constructor takes (`new ExecutionSystem(e, Intent, Behavior, Position, WorldClock)` in `SimWorld.cs`). Match the existing field types; this test only needs them to construct the system. If a registry has no public parameterless-friendly constructor, follow the pattern other tests use to build it, or assert on the published `QueueJoinIntent` instead by reading `e.GetEvents<QueueJoinIntent>()` right after `sys.Update`.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter ExecutionQueueRoutingTests`
Expected: FAIL — `ExecutionSystem` constructor has no `SharedActivityRegistry` parameter.

- [ ] **Step 3: Add the dependency and branch the arrival**

In `ExecutionSystem.cs`, add a field + constructor parameter:

```csharp
readonly SharedActivityRegistry _shared;
```

Update the constructor signature to accept it (append the parameter) and assign `_shared = shared;`.

Replace the arrivals loop (lines 41-60) with a queued-affordance branch:

```csharp
// --- arrivals: the walk is done. A serviced affordance (Buy) joins the
//     queue and waits for a turn; everything else settles straight in. ---
foreach (ref readonly var a in Events.GetEvents<ArrivedAtTargetEvent>())
{
    if (!_behavior.TryGet(a.Entity, out var b) || b.Phase != ActivityPhase.Moving)
        continue;

    if (b.Activity == ActivityKind.Buy)
    {
        Events.Publish(new BehaviorSetIntent
        {
            Id = a.Entity,
            Data = new BehaviorData
            {
                Activity = b.Activity,
                Phase = ActivityPhase.Queued,
                TargetBuilding = b.TargetBuilding,
                TargetX = b.TargetX,
                TargetZ = b.TargetZ,
                RemainingGameMinutes = b.RemainingGameMinutes,
                TargetItem = b.TargetItem,
            }
        });
        Events.Publish(new QueueJoinIntent
        {
            Agent = a.Entity,
            Anchor = new QueueAnchor(b.TargetBuilding, ActivityKind.Buy),
            Capacity = ShopCapacity,
            AnchorX = b.TargetX,
            AnchorZ = b.TargetZ,
        });
    }
    else
    {
        Events.Publish(new BehaviorSetIntent
        {
            Id = a.Entity,
            Data = new BehaviorData
            {
                Activity = b.Activity,
                Phase = ActivityPhase.Doing,
                TargetBuilding = b.TargetBuilding,
                TargetX = b.TargetX,
                TargetZ = b.TargetZ,
                RemainingGameMinutes = b.RemainingGameMinutes,
                TargetItem = b.TargetItem,
            }
        });
    }
}
```

Add the constant inside the class:

```csharp
const int ShopCapacity = 1;   // one counter; tunable per the spec's open questions
```

- [ ] **Step 4: Pass the registry in `SimWorld`**

Update the construction in `SimWorld.cs`:

```csharp
new ExecutionSystem(e, Intent, Behavior, Position, WorldClock, SharedActivity),
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter ExecutionQueueRoutingTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Engine/Units/ExecutionSystem.cs Assets/Sim/Engine/SimWorld.cs Headless/Sim.MemoryTests/ExecutionQueueRoutingTests.cs
git commit -m "feat(threshold): route Buy arrivals into the ServiceQueue (Queued + Join)"
```

---

## Task 6: Walk queued agents to their slot (`MovementSystem`)

**Files:**
- Modify: `Assets/Sim/Engine/Units/MovementSystem.cs:92-98` (arrival gating) + the phase filter that decides who moves
- Test: `Headless/Sim.MemoryTests/MovementQueuedTests.cs` (create)

**Interfaces:**
- Consumes: `BehaviorData.Phase` (now includes `Queued`).
- Produces:
  - Pure predicates `public static bool MovementSystem.PhaseMoves(ActivityPhase p)` (`p == Moving || p == Queued`) and `public static bool MovementSystem.PhaseArrives(ActivityPhase p)` (`p == Moving`).
  - Wiring: the per-agent loop processes any phase where `PhaseMoves` is true; the arrival publish fires only where `PhaseArrives` is true (so a waiter reaching its slot doesn't re-trigger the queue-join logic).
- Test: pure unit tests on the two predicates (idiom: `PlaceScoringTests`). The walking-toward-slot behaviour itself needs `TownGrid`/`Path` setup and is covered by the Task 13 soak (visible line) and Task 12 (browser).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class MovementQueuedTests
    {
        [Fact]
        public void MovingAndQueued_BothMove()
        {
            Assert.True(MovementSystem.PhaseMoves(ActivityPhase.Moving));
            Assert.True(MovementSystem.PhaseMoves(ActivityPhase.Queued));
        }

        [Fact]
        public void Doing_DoesNotMove()
            => Assert.False(MovementSystem.PhaseMoves(ActivityPhase.Doing));

        [Fact]
        public void OnlyMoving_PublishesArrival()
        {
            Assert.True(MovementSystem.PhaseArrives(ActivityPhase.Moving));
            Assert.False(MovementSystem.PhaseArrives(ActivityPhase.Queued));   // waiter doesn't re-arrive
            Assert.False(MovementSystem.PhaseArrives(ActivityPhase.Doing));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter MovementQueuedTests`
Expected: FAIL — `MovementSystem` has no `PhaseMoves`/`PhaseArrives`.

- [ ] **Step 3a: Add the pure predicates**

In `MovementSystem.cs`:

```csharp
/// Moving and Queued agents both walk toward TargetX/Z (a waiter walks up its line).
public static bool PhaseMoves(ActivityPhase p)
    => p == ActivityPhase.Moving || p == ActivityPhase.Queued;

/// Only a Moving agent's arrival flips into the activity / joins a queue; a waiter
/// reaching its slot must not re-trigger that.
public static bool PhaseArrives(ActivityPhase p) => p == ActivityPhase.Moving;
```

- [ ] **Step 3b: Wire the predicates into the loop**

Find the per-agent loop's phase filter and replace it with `PhaseMoves`:

```csharp
if (!PhaseMoves(behavior.Phase)) continue;
```

Gate the arrival publish (lines 92-98) with `PhaseArrives`:

```csharp
float gx = behavior.TargetX - x, gz = behavior.TargetZ - z;
if (plan.Next >= plan.Points.Count
    || gx * gx + gz * gz <= ArriveDistance * ArriveDistance)
{
    Events.Publish(new PathClearIntent { Id = kv.Key });
    if (PhaseArrives(behavior.Phase))
        Events.Publish(new ArrivedAtTargetEvent { Entity = kv.Key });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter MovementQueuedTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Engine/Units/MovementSystem.cs Headless/Sim.MemoryTests/MovementQueuedTests.cs
git commit -m "feat(threshold): MovementSystem walks queued agents to their slot (no re-arrival)"
```

---

## Task 7: Leave the queue on re-decide to a different target (`OddSystem`)

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` — constructor (inject `SharedActivityRegistry`); end of `Decide(...)` (publish `QueueLeaveIntent` when the new target differs from the current anchor); the decide loop (allow `Queued` agents to be re-decided on dawn/dusk + cap).
- Modify: `Assets/Sim/Engine/SimWorld.cs` (pass `SharedActivity` to `OddSystem`)
- Test: `Headless/Sim.MemoryTests/OddQueueLeaveTests.cs` (create)

**Interfaces:**
- Consumes: `SharedActivityRegistry.AnchorOf(EntityId, out QueueAnchor)`.
- Produces:
  - Pure helper `public static bool OddSystem.ShouldStayInQueue(bool inQueue, int heldBuilding, ActivityKind winnerKind, int winnerBuilding)` — `true` only when `inQueue && winnerKind == ActivityKind.Buy && winnerBuilding == heldBuilding` (same shop+Buy still wins → keep your place). Its negation (when `inQueue`) means "leave the line."
  - Wiring: `Decide` calls the helper; if in a queue and not staying, publish `QueueLeaveIntent { Agent = id }`; if staying, early-`return` (no new `BehaviorSetIntent`, no lost position). The decide loop also lets `Queued` agents re-decide on dawn/dusk + the staggered cap.
- Test: pure unit tests on `ShouldStayInQueue` (idiom: `PlaceScoringTests`).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddQueueLeaveTests
    {
        [Fact]
        public void SameShopBuyWins_StaysInQueue()
            => Assert.True(OddSystem.ShouldStayInQueue(true, 7, ActivityKind.Buy, 7));

        [Fact]
        public void DifferentBuildingWins_LeavesQueue()
            => Assert.False(OddSystem.ShouldStayInQueue(true, 7, ActivityKind.Buy, 9));

        [Fact]
        public void NonBuyWins_LeavesQueue()
            => Assert.False(OddSystem.ShouldStayInQueue(true, 7, ActivityKind.Sleep, 7));

        [Fact]
        public void NotInQueue_NeverStays()
            => Assert.False(OddSystem.ShouldStayInQueue(false, 7, ActivityKind.Buy, 7));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddQueueLeaveTests`
Expected: FAIL — `OddSystem` has no `ShouldStayInQueue`.

- [ ] **Step 3a: Add the pure helper**

In `OddSystem.cs`, add (near `PlaceAversion`/`ProvisionPreference`):

```csharp
/// A queued agent keeps its place only if the same shop+Buy still wins; any
/// other winner means it leaves the line. Pure — unit-tested in isolation.
public static bool ShouldStayInQueue(bool inQueue, int heldBuilding,
    ActivityKind winnerKind, int winnerBuilding)
    => inQueue && winnerKind == ActivityKind.Buy && winnerBuilding == heldBuilding;
```

- [ ] **Step 3b: Inject the registry and wire the helper**

Add the field + constructor parameter:

```csharp
readonly SharedActivityRegistry _shared;
```

Let `Queued` agents re-decide. In `Update` (lines 119-133), alongside the `Doing` branch, add:

```csharp
else if (behavior.Phase == ActivityPhase.Queued)
{
    double since = behavior.SinceDecisionGameMinutes + gameMinutes;
    double cap = 48 + (Hash(id.Value, 0) & 0x1F);
    if (since >= cap)            // dawn/dusk already sets decide via reDecideAll
        decide = true;
}
```

At the END of `Decide(...)`, before the existing `Events.Publish(new BehaviorSetIntent { ... })`, gate on the helper (you have `bestKind`, `bestBuilding`):

```csharp
if (_shared.AnchorOf(id, out var heldAnchor))
{
    if (ShouldStayInQueue(true, heldAnchor.Building, bestKind, bestBuilding))
        return;                                  // keep your place; no new intent
    Events.Publish(new QueueLeaveIntent { Agent = id });   // leave the line, fall through
}
```

> The early `return` when staying prevents resetting `RemainingGameMinutes`/position for an agent already correctly queued. `bestKind`/`bestBuilding` are the winner locals in `Decide` (lines 185-259); confirm their names on the live code.

- [ ] **Step 4: Pass the registry in `SimWorld`**

Update `new OddSystem(e, Residency, Behavior, Needs, ... seed)` to append `SharedActivity` in the correct position (match the constructor's new parameter order):

```csharp
new OddSystem(e, Residency, Behavior, Needs, Buildings, Position, Personality, Weather,
    Holiday, Employment, Coin, Larder, Occupancy, Conscience, Subjective, Creatures,
    Affects, Relations, Meanings, PlaceMemory, AgentMemory, Stock, Items, WorldClock, SharedActivity, seed),
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddQueueLeaveTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs Assets/Sim/Engine/SimWorld.cs Headless/Sim.MemoryTests/OddQueueLeaveTests.cs
git commit -m "feat(threshold): OddSystem leaves the queue on re-decide to a different target"
```

---

## Task 8: Verify serialized service — capacity throttles `Doing`

**Files:**
- Test only: `Headless/Sim.MemoryTests/QueueSerializesSaleTests.cs` (create)
- (No production change. `EconomySystem.PaySale` already pays only `Phase == Doing`; since the queue keeps all but `Capacity` agents in `Queued`, serialization is emergent. This is a `Rig` regression guard at the registry+system level.)

**Interfaces:**
- Consumes: `SharedActivityRegistry` (Task 2) + `SharedActivitySystem` (Task 4) + `BehaviorRegistry`. The invariant: across join/serve/leave cycles, never more than `Capacity` agents are in `ActivityPhase.Doing` at one anchor — so `EconomySystem` (which only acts on `Doing`) pays at most `Capacity` patrons at once.

- [ ] **Step 1: Write the test (Rig over registry + system)**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class QueueSerializesSaleTests
    {
        static readonly QueueAnchor Shop = new QueueAnchor(7, ActivityKind.Buy);

        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly SharedActivityRegistry Shared;
            public readonly BehaviorRegistry Behavior;
            public readonly SharedActivitySystem System;
            public Rig()
            {
                Shared = new SharedActivityRegistry(E);
                Behavior = new BehaviorRegistry(E);
                System = new SharedActivitySystem(E, Shared, Behavior);
            }
            public void Queue(int id)
            {
                E.Publish(new BehaviorSetIntent { Id = new EntityId(id), Data = new BehaviorData
                    { Activity = ActivityKind.Buy, Phase = ActivityPhase.Queued, TargetBuilding = 7,
                      TargetX = 10, TargetZ = 20 } });
                E.Publish(new QueueJoinIntent { Agent = new EntityId(id), Anchor = Shop,
                    Capacity = 1, AnchorX = 10, AnchorZ = 20 });
            }
            public void Leave(int id) => E.Publish(new QueueLeaveIntent { Agent = new EntityId(id) });
            public void Step(long t) { E.Tick(); Behavior.Update(t); Shared.Update(t); System.Update(t); }
            public int DoingCount(int[] ids) => ids.Count(i =>
                Behavior.TryGet(new EntityId(i), out var b) && b.Phase == ActivityPhase.Doing
                && b.TargetBuilding == 7);
        }

        [Fact]
        public void ThreeBuyers_OneCounter_NeverMoreThanOneDoing()
        {
            var r = new Rig();
            var ids = new[] { 1, 2, 3 };
            foreach (var i in ids) r.Queue(i);

            int maxDoing = 0;
            for (long t = 0; t < 12; t++)
            {
                r.Step(t);
                maxDoing = System.Math.Max(maxDoing, r.DoingCount(ids));
                // whoever is being served finishes and leaves, freeing the counter
                foreach (var i in ids)
                    if (r.Behavior.TryGet(new EntityId(i), out var b) && b.Phase == ActivityPhase.Doing)
                        r.Leave(i);
            }
            Assert.True(maxDoing <= 1, $"capacity 1, but saw {maxDoing} agents Doing at once");
        }
    }
}
```

- [ ] **Step 2: Run the test**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter QueueSerializesSaleTests`
Expected: PASS. If it FAILS with >1 Doing, the promotion/capacity logic in Tasks 2/4 has a bug — fix there.

> The end-to-end "only the served agent's coin moves" claim (through `EconomySystem`) is observed in the Task 13 soak histogram; this Rig proves the structural invariant it rests on.

- [ ] **Step 3: Commit**

```bash
git add Headless/Sim.MemoryTests/QueueSerializesSaleTests.cs
git commit -m "test(threshold): one-counter shop never exceeds capacity in Doing"
```

---

## Task 9: Somatic percepts — stamp the body into the agent's own bag

**Files:**
- Create: `Assets/Sim/Memory/SomaticAtoms.cs`
- Create: `Assets/Sim/Engine/Units/SomaticPerceptSystem.cs`
- Modify: `Assets/Sim/Engine/SimWorld.cs` (instantiate + add to systems array)
- Test: `Headless/Sim.MemoryTests/SomaticPerceptTests.cs` (create)

**Interfaces:**
- Consumes: `NeedsRegistry.TryGet(EntityId, out NeedsData)`, `NeedsData.V[NeedAxis.Hunger|EnergyDef|Fear]`, `PerceivableRegistry` via `StampAtomIntent { EntityId Entity; AtomTypeId Type; Fixed Value; }`, `Fixed.FromDouble`.
- Produces: `static class SomaticAtoms { AtomTypeId Hunger, Energy, Fear; }`; `class SomaticPerceptSystem : SimSystem` that, every `SenseEveryTicks` (5), stamps each resident's hunger/energy/fear need values into its OWN `AtomBag`.
- Constant: `const int SenseEveryTicks = 5;` (match `SubjectiveSystem`'s cadence).

> This realises "the body is perceived as atoms" (the perceivable-atoms seam) WITHOUT rerouting ODD scoring, which still reads `NeedsData` directly. No double-counting: nothing consumes these atoms for scoring yet; they exist for sleep salience and future protocols.

Pick atom-type IDs that don't collide with existing ones. `PerceivableAtoms.Activity(...)` uses certain ranges (see `Program.cs:527-530`); the memory tests use IDs like `1004`, `4002`. Use a dedicated reserved band `7000-7099` for somatic atoms.

- [ ] **Step 1: Write the failing test (Rig over NeedsRegistry + system + PerceivableRegistry)**

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SomaticPerceptTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly NeedsRegistry Needs;
            public readonly PerceivableRegistry Perceivable;
            public readonly SomaticPerceptSystem System;
            public Rig()
            {
                Needs = new NeedsRegistry(E);
                Perceivable = new PerceivableRegistry(E);
                System = new SomaticPerceptSystem(E, Needs);
            }
            public void SetHunger(int id, double v)
            {
                var d = new NeedsData();
                d.V[NeedAxis.Hunger] = v;
                E.Publish(new NeedsSetIntent { Id = new EntityId(id), Data = d });
            }
            public void Step(long t) { E.Tick(); Needs.Update(t); Perceivable.Update(t); System.Update(t); }
        }

        [Fact]
        public void HungerNeed_IsStampedIntoOwnBag()
        {
            var r = new Rig();
            r.SetHunger(1, 0.7);
            r.Step(0);     // needs applied; system stamps at tick % SenseEveryTicks == 0
            r.Step(1);     // perceivable applies the StampAtomIntent published on the previous step

            var bag = r.Perceivable.Bag(new EntityId(1));
            Assert.True(bag.TryGet(SomaticAtoms.Hunger, out var v));
            Assert.True(v > Fixed.Zero);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter SomaticPerceptTests`
Expected: FAIL — `SomaticAtoms`/`SomaticPerceptSystem` not found.

- [ ] **Step 3: Write the atom constants**

Create `Assets/Sim/Memory/SomaticAtoms.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// Atom types for an agent's perception of its OWN body. Reserved band 7000-7099
    /// to avoid colliding with entity Kind/Role/Race/Activity atom ranges.
    public static class SomaticAtoms
    {
        public static readonly AtomTypeId Hunger = new AtomTypeId(7000);
        public static readonly AtomTypeId Energy = new AtomTypeId(7001);   // tiredness deficit
        public static readonly AtomTypeId Fear   = new AtomTypeId(7002);
    }
}
```

- [ ] **Step 4: Write the system**

Create `Assets/Sim/Engine/Units/SomaticPerceptSystem.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// The agent perceives its own body: each sense tick, the salient need axes
    /// are stamped into the agent's own AtomBag as somatic atoms. This is the
    /// perceivable-atoms model applied inward — the seam future protocols and
    /// sleep-salience read from. ODD scoring still reads NeedsData directly.
    public sealed class SomaticPerceptSystem : SimSystem
    {
        public const int SenseEveryTicks = 5;

        readonly NeedsRegistry _needs;

        public SomaticPerceptSystem(EventBus events, NeedsRegistry needs) : base(events)
        {
            _needs = needs;
        }

        public override void Update(long tick)
        {
            if (tick % SenseEveryTicks != 0) return;
            foreach (var kv in _needs.All)
            {
                var v = kv.Value.V;
                Stamp(kv.Key, SomaticAtoms.Hunger, v[NeedAxis.Hunger]);
                Stamp(kv.Key, SomaticAtoms.Energy, v[NeedAxis.EnergyDef]);
                Stamp(kv.Key, SomaticAtoms.Fear,   v[NeedAxis.Fear]);
            }
        }

        void Stamp(EntityId id, AtomTypeId type, double value)
        {
            if (value <= 0) return;
            Events.Publish(new StampAtomIntent
            {
                Entity = id, Type = type,
                Value = Fixed.FromDouble(value > 1.0 ? 1.0 : value),
            });
        }
    }
}
```

> Confirm `NeedsRegistry` exposes an `All` enumeration like the other registries. If it only exposes `TryGet`, add a public `IEnumerable<KeyValuePair<EntityId, NeedsData>> All` getter to `NeedsRegistry` mirroring `OccupancyRegistry`/`BehaviorRegistry`, in this same task.

- [ ] **Step 5: Wire into `SimWorld`**

Add to the `systems` array near the other perception systems (after `NeedsSystem`, before `SubjectiveSystem`):

```csharp
new SomaticPerceptSystem(e, Needs),
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter SomaticPerceptTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Memory/SomaticAtoms.cs Assets/Sim/Engine/Units/SomaticPerceptSystem.cs Assets/Sim/Engine/SimWorld.cs Headless/Sim.MemoryTests/SomaticPerceptTests.cs
git commit -m "feat(threshold): somatic percepts — stamp body needs into the agent's own bag"
```

---

## Task 10: Preemption — re-value the commitment, switch only if meaningfully better

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` — throttled preemption trigger for `Queued`/`Doing`; hysteresis in the switch decision.
- Test: `Headless/Sim.MemoryTests/OddPreemptionTests.cs` (create)

**Interfaces:**
- Consumes: existing `GatherAds(id)`, `V(ad, sc)` scoring, `OddTree` traversal inside `Decide`.
- Produces:
  - Pure helper `public static bool OddSystem.ShouldSwitchCommitment(bool committed, double currentScore, double winnerScore, double hysteresis)` — a non-committed agent always picks freely (`true`); a committed agent switches only if `winnerScore > currentScore * (1 + hysteresis)`. This IS the interrupt rule: "switch only when something is meaningfully more important than what I'm doing."
  - Wiring: a throttled `decide = true` for committed agents on a stagger (`tick % PreemptEveryTicks == Hash(id.Value, 1) % PreemptEveryTicks`); inside `Decide`, gate the switch through the helper using the current activity's score vs. the winner's score.
- Constants: `const int PreemptEveryTicks = 5;`, `const double Hysteresis = 0.15;` (both tunable — spec open questions).
- Test: pure unit tests on `ShouldSwitchCommitment` (idiom: `PlaceScoringTests`).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddPreemptionTests
    {
        const double Hyst = 0.15;

        [Fact]
        public void Committed_UrgentWinner_Switches()      // a clearly better option interrupts
            => Assert.True(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 2.0, Hyst));

        [Fact]
        public void Committed_MarginalWinner_DoesNotSwitch()   // anti-thrash
            => Assert.False(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 1.10, Hyst));

        [Fact]
        public void Committed_WinnerBelowCurrent_DoesNotSwitch()
            => Assert.False(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 0.8, Hyst));

        [Fact]
        public void NotCommitted_AlwaysPicksFreely()
            => Assert.True(OddSystem.ShouldSwitchCommitment(false, currentScore: 5.0, winnerScore: 0.1, Hyst));

        [Fact]
        public void Committed_ExactlyAtThreshold_DoesNotSwitch()
            => Assert.False(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 1.15, Hyst));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddPreemptionTests`
Expected: FAIL — `OddSystem` has no `ShouldSwitchCommitment`.

- [ ] **Step 3a: Add the pure helper**

In `OddSystem.cs`, near the other pure helpers:

```csharp
/// The interrupt rule: a committed agent (Queued/Doing) abandons what it's doing
/// only when the best alternative is meaningfully better — beats the current
/// commitment's score by the hysteresis margin. A free agent picks the winner.
/// Pure — unit-tested in isolation.
public static bool ShouldSwitchCommitment(bool committed, double currentScore,
    double winnerScore, double hysteresis)
    => !committed || winnerScore > currentScore * (1.0 + hysteresis);
```

- [ ] **Step 3b: Add the throttled trigger**

In `OddSystem.Update`, extend the `Doing`/`Queued` branches (from Task 7) to also fire on the preemption cadence:

```csharp
int stagger = (int)(Hash(id.Value, 1) % PreemptEveryTicks);
bool preemptTick = (tick % PreemptEveryTicks) == stagger;

if (behavior.Phase == ActivityPhase.Doing)
{
    currentRemaining = behavior.RemainingGameMinutes - gameMinutes;
    double since = behavior.SinceDecisionGameMinutes + gameMinutes;
    double cap = 48 + (Hash(id.Value, 0) & 0x1F);
    if (currentRemaining <= 0 || since >= cap || preemptTick)
        decide = true;
}
else if (behavior.Phase == ActivityPhase.Queued)
{
    double since = behavior.SinceDecisionGameMinutes + gameMinutes;
    double cap = 48 + (Hash(id.Value, 0) & 0x1F);
    if (since >= cap || preemptTick)
        decide = true;
}
```

Add the constants to the class:

```csharp
const int PreemptEveryTicks = 5;     // re-value commitments ~every 5 ticks (tunable)
const double Hysteresis = 0.15;      // winner must beat current by 15% to switch (anti-thrash)
```

- [ ] **Step 4: Gate the switch through the helper in `Decide`**

Inside `Decide`, after scoring all ads but before committing, compute the current commitment's score and the winner's score, then call the helper. Keep this BEFORE the Task-7 queue-leave block so a no-switch returns early without leaving the line:

```csharp
// The interrupt gate: a committed agent only switches if the winner is
// meaningfully better (ShouldSwitchCommitment). Otherwise keep what it's doing.
bool committed = current != null
    && (current.Phase == ActivityPhase.Queued || current.Phase == ActivityPhase.Doing)
    && current.Activity != ActivityKind.None;
double currentScore = 0;
if (committed && verbIndex.TryGetValue(current.Activity, out var ci))
    currentScore = verbScore[ci];
double winnerScore = verbScore[verbIndex[bestKind]];
if (!ShouldSwitchCommitment(committed, currentScore, winnerScore, Hysteresis))
    return;   // not worth interrupting — keep the current commitment untouched
```

> The locals `verbScore`/`verbIndex`/`bestKind` come from `OddSystem.Decide` (lines 185-259); confirm their exact names on the live code. If the winner's score isn't already retained, capture it where the winner is computed (`verbScore[winner]`). The early `return` leaves a queued agent in place (no `QueueLeaveIntent`, no new `BehaviorSetIntent`) and leaves a `Doing` agent's activity untouched.

- [ ] **Step 5: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddPreemptionTests`
Expected: PASS (urgent balks; marginal doesn't thrash).

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs Headless/Sim.MemoryTests/OddPreemptionTests.cs
git commit -m "feat(threshold): percept-driven preemption — balk on urgency, hysteresis vs thrash"
```

---

## Task 11: Sleep gating — suppress preemption, wake on a hit

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` — skip preemption while `Activity == Sleep`; force a decide for a sleeper that took a `DamageEvent`.
- Test: `Headless/Sim.MemoryTests/OddSleepGatingTests.cs` (create)

**Interfaces:**
- Consumes: `DamageEvent { EntityId Target, Source; int Amount; DamageType Type; }`.
- Produces:
  - Pure helper `public static bool OddSystem.SleepShouldWake(bool wasHit, bool reDecideAll, bool durationExpired, bool capReached)` — a sleeper wakes ONLY on a hit, the dawn/dusk rethink, sleep duration expiry, or the staggered cap. Crucially it has NO `preemptTick` parameter: cadence-based percept preemption can never wake a sleeper. This encodes "percepts are suppressed during sleep; only a hit (or natural end) wakes you."
  - Wiring: in `Update`, when `behavior.Activity == ActivityKind.Sleep`, decide via `SleepShouldWake(...)` and `continue` — skipping the generic Doing/Queued preemption branches entirely.
- Test: pure unit tests on `SleepShouldWake` (idiom: `PlaceScoringTests`).

> Loud-noise wake is deferred: no loud-noise event exists in `SimEvents.cs` yet (`HeardUtterance` is speech, not a salient physical noise). Scope this task to wake-on-hit; note loud-noise as future (a new salient-noise event + an added bool to this helper).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddSleepGatingTests
    {
        [Fact]
        public void Hit_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(wasHit: true, reDecideAll: false,
                                                     durationExpired: false, capReached: false));

        [Fact]
        public void Dawn_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(false, reDecideAll: true, false, false));

        [Fact]
        public void DurationExpired_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(false, false, durationExpired: true, false));

        [Fact]
        public void NothingSalient_StaysAsleep()   // percepts suppressed: no cadence wake here
            => Assert.False(OddSystem.SleepShouldWake(false, false, false, false));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddSleepGatingTests`
Expected: FAIL — `OddSystem` has no `SleepShouldWake`.

- [ ] **Step 3a: Add the pure helper**

In `OddSystem.cs`, near the other pure helpers:

```csharp
/// Sleep suppresses percepts: a sleeper wakes only on a hit, the dawn/dusk
/// rethink, sleep-duration expiry, or the staggered cap — NEVER on the percept
/// preemption cadence (note: no preemptTick parameter). Pure — unit-tested.
public static bool SleepShouldWake(bool wasHit, bool reDecideAll,
    bool durationExpired, bool capReached)
    => wasHit || reDecideAll || durationExpired || capReached;
```

- [ ] **Step 3b: Gate sleep in the decide loop**

In `OddSystem.Update`, before the phase branches, build the set of sleepers hit this tick:

```csharp
var hitThisTick = new HashSet<EntityId>();
foreach (ref readonly var d in Events.GetEvents<DamageEvent>())
    hitThisTick.Add(d.Target);
```

Then, inside the per-agent loop, special-case sleepers BEFORE the Doing/Queued branches from Tasks 7/10:

```csharp
if (behavior.Activity == ActivityKind.Sleep)
{
    double sinceSleep = behavior.SinceDecisionGameMinutes + gameMinutes;
    double capSleep = 48 + (Hash(id.Value, 0) & 0x1F);
    bool wake = SleepShouldWake(
        wasHit: hitThisTick.Contains(id),
        reDecideAll: reDecideAll,
        durationExpired: behavior.RemainingGameMinutes - gameMinutes <= 0,
        capReached: sinceSleep >= capSleep);
    if (wake)
        Decide(id, kv.Value, behavior, behavior.RemainingGameMinutes - gameMinutes, clock.Hour, tick);
    continue;   // percept-cadence preemption never applies to a sleeper
}
```

> Place this `Sleep` special-case right after `_behavior.TryGet(id, out behavior)` succeeds. The `continue` ensures `preemptTick` (Task 10) never reaches a sleeper.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddSleepGatingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs Headless/Sim.MemoryTests/OddSleepGatingTests.cs
git commit -m "feat(threshold): sleep gating — suppress preemption, wake on a hit"
```

---

## Task 12: Viewer — render the `Queued` phase and show queue position

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` (inspect endpoint, ~line 620-648 — add queue position)
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` and/or its client module that colours agents by phase
- Test: manual (browser) + `Headless/Sim.Web/parity` re-baseline

**Interfaces:**
- Consumes: `world.SharedActivity.PositionOf(id)`, `world.SharedActivity.IsServed(id)`; the already-shipped compact phase int (`Program.cs:400`).
- Produces: inspect JSON gains `queuePos` (int, `-1` if not queued) and `queueServed` (bool); client tints/labels `phase == 2` (Queued) distinctly.

- [ ] **Step 1: Add queue fields to the inspect endpoint**

In `Program.cs`, in the inspect handler that builds the per-agent object (around lines 620-648 where `activity`/`phase` are set), add:

```csharp
int queuePos = world.SharedActivity.PositionOf(id);
bool queueServed = world.SharedActivity.IsServed(id);
```

and include them in the returned anonymous object:

```csharp
activity = beh != null ? beh.Activity.ToString() : "—",
phase = beh != null ? beh.Phase.ToString() : "—",
queuePos,         // -1 if not in a line
queueServed,      // true while being served at the counter
```

- [ ] **Step 2: Render the Queued phase in the client**

In the client renderer (the module that colours agents — search `town3d.html` and the client JS for where it reads the `phase` column from the compact rows `[id, x, z, activity, phase, yaw, kind, groundY]`), add a branch for the new phase value `2`:

```javascript
// phase: 0 Moving, 1 Doing, 2 Queued
const PHASE_QUEUED = 2;
// where agent tint/material is chosen:
if (phase === PHASE_QUEUED) {
    tint = QUEUED_TINT;        // e.g. a muted amber so a line reads as "waiting"
}
```

Define `QUEUED_TINT` near the other phase colours. If the inspector panel shows phase text, append the queue position when present:

```javascript
if (info.queuePos >= 0) label += ` — in line (#${info.queuePos + 1})`;
else if (info.queueServed) label += ` — at counter`;
```

- [ ] **Step 3: Verify in the browser**

Run the web sim (per project convention, e.g. `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web`), open `town3d.html`, find a shop at a busy hour, and confirm: agents form a visible line, the head advances to the counter, waiters are tinted as queued, and clicking a waiter shows "in line (#N)".

- [ ] **Step 4: Re-baseline the parity gate**

The queue changes agent placement, so the parity baseline shifts (expected, per the spec). Regenerate it:

Run: `cd /home/uggeli/slopfall/Headless/Sim.Web && npm run parity -- --update` (or the project's documented baseline-refresh command; the baseline lives at `parity/baseline/local-r2-base.png`).
Then confirm `npm run parity` passes against the new baseline.

- [ ] **Step 5: Commit**

```bash
git add Headless/Sim.Web/Program.cs Headless/Sim.Web/wwwroot/town3d.html Headless/Sim.Web/parity/baseline/
git commit -m "feat(threshold): viewer renders Queued phase + inspector shows queue position"
```

---

## Task 13: Soak verification — lines form, agents balk, nothing deadlocks

**Files:**
- Test/verify: run the existing soak harness (`Sim.Host --soak`); add assertions or a histogram check if the harness supports them.

**Interfaces:**
- Consumes: the full wired sim from Tasks 1-12.

- [ ] **Step 1: Run a town soak and capture the activity histogram**

Run a multi-day soak on a small walled town with active shops (per the project's soak invocation, e.g. `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --soak --days 5 --town <small-town>`). Capture the per-day histogram of what agents DO.

- [ ] **Step 2: Assert the contention signals**

Confirm from the histogram / logs:
- **Lines form:** at popular shops, multiple agents are in `Queued` at the same anchor during peak hours (>1 waiter observed).
- **Service is serialized:** never more than `ShopCapacity` (1) agents `Doing` Buy at one building in a tick.
- **Balking happens:** `QueueLeaveIntent` count from preemption is > 0 over the run (agents leave lines for more urgent needs).
- **No deadlock:** no agent remains `Queued` indefinitely — every queued agent eventually reaches `Doing` or leaves. (Check the max continuous `Queued` duration per agent is bounded well under the run length.)
- **No economy collapse:** `SalesRevenue` and deaths are within the same band as a pre-Threshold baseline soak (throttled `Buy` shouldn't starve the town). Compare against the figures in the relevant memory/soak notes.

- [ ] **Step 2b: Gate the performance budget (≤ 100 ms/tick)**

Measure per-tick wall-clock time during the soak at region-scale population. The existing `MetricsSystem` is the natural home for a tick-duration metric; if it doesn't already record one, time `SimEngine.Step()` in the soak harness (`System.Diagnostics.Stopwatch` around the step call — soak harness only, NOT inside the deterministic sim) and report max + p99 + mean.

Assert: **max tick time ≤ 100 ms** across the run. Capture the numbers before AND after enabling preemption (Tasks 10/11) so the preemption cost is attributable. If the cap is exceeded, raise `PreemptEveryTicks` (Task 10) and re-measure — this is the first lever, since per-tick `Decide` re-runs are the dominant new cost. Record the final tick-time figures with the tuning outcome (Step 4).

- [ ] **Step 3: If a signal is wrong, debug at its source**

- Lines never form → check Task 5 routing (`Buy` → `Queued`) and Task 4 promotion.
- Deadlock (stuck `Queued`) → check Task 4 promote loop and Task 7/10 re-decide triggers fire for `Queued`.
- Thrash (agents join/leave every cadence) → raise `Hysteresis` (Task 10).
- Economy collapse → raise `ShopCapacity` (Task 5) or shorten `Buy` `serviceTime`; re-soak.
- **Tick > 100 ms** → raise `PreemptEveryTicks` (Task 10) first; if still over, profile `GatherAds`/scoring and consider only re-valuing `Queued` (not `Doing`) agents on the cadence.

- [ ] **Step 4: Record the tuning outcome**

Note the final `ShopCapacity`, `QueueSpacing`, `PreemptEveryTicks`, `Hysteresis` values and the soak figures in the milestone doc (`docs/Milestones/`), so the next milestone (doors) inherits calibrated constants.

- [ ] **Step 5: Commit any tuning changes**

```bash
git add Assets/Sim docs/Milestones
git commit -m "test(threshold): soak verification + tuned queue/preemption constants"
```

---

## Self-Review

**Spec coverage** (against `2026-06-22-the-threshold-shared-activities-design.md`):
- Pillar A — shared-activity instances + `ServiceQueue` → Tasks 2, 4 (instance/registry/system, `Queued` phase, Join/HeadReady-via-promote/Release-via-Leave). ✓
- Pillar B — somatic percepts → Task 9; interrupt-as-revaluation → Task 10; sleep gating → Task 11. ✓
- Shop = destination affordance, capacity 1, serialized PaySale → Tasks 5, 8. ✓
- Balking via the preemption loop, not bespoke logic → Tasks 7 + 10. ✓
- Viewer: phase already on wire; queue line from real slot positions; inspector lines → Tasks 4 (slots) + 12. ✓
- Testing: queue invariants (Task 2), serialization (Task 8), preemption invariants (Task 10), sleep (Task 11), deadlock guard + honesty-histogram soak (Task 13). ✓
- Performance: ≤ 100 ms/tick hard cap (Global Constraints), measured + gated in the soak (Task 13 Step 2b), with `PreemptEveryTicks` as the primary lever. ✓
- Seams designed-for: `protocolState`/anchor model (Task 2) accommodates Barter/Combat; somatic atoms (Task 9) ready for sleep-salience/future; single PaySale call-site preserved (Task 8). ✓
- **Doors (Phase 3) are intentionally NOT in this plan** — they reuse this runtime and get their own plan (`Open`/`Close` executors, movement-time door trigger, wall gates + building doors, curfew). Recorded as out of scope here.

**Testing idiom (confirmed against the live suite):** there is NO `TestWorld`/full-`SimWorld` test harness. Registry/low-dep-system tasks (2, 4, 5, 8, 9) use the per-test `Rig` pattern (EventBus + the few registries + the system; assert on published intents). `OddSystem` tasks (7, 10, 11) extract the decision rule into a `public static` pure helper (`ShouldStayInQueue`, `ShouldSwitchCommitment`, `SleepShouldWake`) and unit-test it directly — the codebase idiom (`PlaceScoringTests`/`OddSnapshotTests`). `MovementSystem` (Task 6) extracts pure predicates (`PhaseMoves`/`PhaseArrives`). End-to-end behaviour is the Task 13 soak. No task depends on a harness that doesn't exist.

**Placeholder scan:** no "TBD"/"add error handling"/"similar to Task N". Each task carries real test + implementation code.

**Type consistency:** `QueueAnchor`, `QueueJoinIntent`/`QueueLeaveIntent`, `SharedActivityInstance`, `SharedActivityRegistry` (reads: `TryGet`, `AnchorOf`, `IsServed`, `PositionOf`, `ServedSnapshot`), `SharedActivitySystem`, `SomaticAtoms.{Hunger,Energy,Fear}`, `SomaticPerceptSystem`, `ActivityPhase.Queued`, constants `QueueSpacing`/`ShopCapacity`/`SenseEveryTicks`/`PreemptEveryTicks`/`Hysteresis` — all referenced names are defined in the task that introduces them and used consistently downstream.

**Known uncertainty to confirm during execution (not placeholders — verify against the live code):**
1. `NeedsRegistry` may need a public `All` enumerator (flagged in Task 9 Step 4); mirror `OccupancyRegistry`/`BehaviorRegistry`.
2. The exact constructor parameter list/order for `ExecutionSystem`/`OddSystem`/`MovementSystem` — append the new registry and match the call site in `SimWorld.cs`.
3. Inside `OddSystem.Decide`, the precise local names for the winner / its score (`verbScore`/`verbIndex`/`bestKind`/`bestBuilding`) — confirm at lines 185-259 and capture the winner's score there.
4. Whether `MetricsSystem` already records a per-tick duration (Task 13 Step 2b); if not, time `SimEngine.Step()` in the soak harness only.
