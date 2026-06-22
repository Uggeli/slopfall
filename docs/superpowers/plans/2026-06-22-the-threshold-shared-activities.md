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
- Produces: `MovementSystem` advances agents whose `Phase` is `Moving` OR `Queued` toward `TargetX/TargetZ`; it publishes `ArrivedAtTargetEvent` ONLY for `Moving` agents (so a waiter arriving at its slot does not re-trigger the arrival/queue logic).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class MovementQueuedTests
    {
        [Fact]
        public void QueuedAgent_MovesTowardSlot_NoArrivalEvent()
        {
            var (e, world) = TestWorld.New();   // see note below
            var id = world.SpawnAgentAt(50, 50);
            world.SetBehavior(id, ActivityKind.Buy, ActivityPhase.Queued, targetX: 50, targetZ: 40);

            for (int i = 0; i < 20; i++) world.Step();

            var pos = world.PositionOf(id);
            Assert.True(pos.Z < 50, "queued agent should walk toward its slot at Z=40");
            Assert.Empty(world.DrainEvents<ArrivedAtTargetEvent>().Where(ev => ev.Entity.Equals(id)));
        }
    }
}
```

> Note: if no `TestWorld` harness exists, assert at the unit level instead: construct a `MovementSystem` with the same registries `SimWorld` passes it, seed one `PositionRegistry` + `BehaviorRegistry` entry with `Phase = Queued`, call `Update`, and assert (a) a `PositionSetIntent`/position delta toward the target was published and (b) no `ArrivedAtTargetEvent` was published for a `Queued` agent. Match the registry types from `new MovementSystem(e, WorldClock, Behavior, Position, TownGrid, Path, seed)`.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter MovementQueuedTests`
Expected: FAIL — queued agents don't move today (only `Moving` is advanced), or an arrival event fires.

- [ ] **Step 3: Let `Queued` agents move; gate the arrival event**

In `MovementSystem.cs`, find the per-agent loop that currently processes `Moving` agents. Change the phase guard so the body also runs for `Queued`. Then change the arrival publish (lines 92-98) so the `ArrivedAtTargetEvent` is only emitted for `Moving`:

```csharp
float gx = behavior.TargetX - x, gz = behavior.TargetZ - z;
if (plan.Next >= plan.Points.Count
    || gx * gx + gz * gz <= ArriveDistance * ArriveDistance)
{
    Events.Publish(new PathClearIntent { Id = kv.Key });
    if (behavior.Phase == ActivityPhase.Moving)
        Events.Publish(new ArrivedAtTargetEvent { Entity = kv.Key });
}
```

If the loop currently `continue`s for non-`Moving` phases, change that filter to include `Queued`:

```csharp
if (behavior.Phase != ActivityPhase.Moving && behavior.Phase != ActivityPhase.Queued)
    continue;
```

(The exact line depends on how the loop filters; the rule is: process `Moving` and `Queued`, emit `ArrivedAtTargetEvent` only for `Moving`.)

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
- Produces: when `Decide` commits a behaviour whose `(TargetBuilding, Activity)` differs from the agent's current `QueueAnchor`, OR whose chosen activity is not `Buy`, publish `QueueLeaveIntent { Agent = id }`. When the same shop+Buy wins, do NOT leave and do NOT reset the behaviour (avoid losing queue position).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddQueueLeaveTests
    {
        [Fact]
        public void RedecideToDifferentTarget_PublishesLeave()
        {
            // A queued agent whose ODD now prefers Sleep (night) must leave the line.
            var (e, world) = TestWorld.New();
            var id = world.SpawnQueuedBuyer(building: 7);     // helper: agent Queued at shop 7
            world.MakeNight();                                // Sleep should now win
            world.StepUntilRedecide(id);

            Assert.Contains(world.DrainEvents<QueueLeaveIntent>(), lv => lv.Agent.Equals(id));
        }

        [Fact]
        public void RedecideToSameShop_DoesNotLeave()
        {
            var (e, world) = TestWorld.New();
            var id = world.SpawnQueuedBuyer(building: 7);
            world.ForceRedecide(id);                          // cap/dawn rethink, Buy still wins
            Assert.DoesNotContain(world.DrainEvents<QueueLeaveIntent>(), lv => lv.Agent.Equals(id));
        }
    }
}
```

> Note: if `TestWorld` helpers don't exist, drive `OddSystem` directly: seed `SharedActivityRegistry` with the agent (publish a `QueueJoinIntent`, tick), seed `NeedsData`/`ResidencyData`/`Position` so `Decide` produces the desired winner, call `OddSystem.Update`, and inspect `e.GetEvents<QueueLeaveIntent>()`. Reuse the construction args from `SimWorld`'s `new OddSystem(...)` call.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddQueueLeaveTests`
Expected: FAIL — `OddSystem` constructor has no `SharedActivityRegistry`, no leave published.

- [ ] **Step 3: Inject the registry and publish conditional leave**

In `OddSystem.cs`, add the field + constructor parameter:

```csharp
readonly SharedActivityRegistry _shared;
```

Allow `Queued` agents to participate in the decide loop. In `Update` (lines 119-133), the current code only sets `decide` for `Doing` on expiry/cap. Add a `Queued` branch so dawn/dusk + the staggered cap can re-decide a waiter:

```csharp
if (behavior.Phase == ActivityPhase.Doing)
{
    currentRemaining = behavior.RemainingGameMinutes - gameMinutes;
    double since = behavior.SinceDecisionGameMinutes + gameMinutes;
    double cap = 48 + (Hash(id.Value, 0) & 0x1F);
    if (currentRemaining <= 0 || since >= cap)
        decide = true;
}
else if (behavior.Phase == ActivityPhase.Queued)
{
    double since = behavior.SinceDecisionGameMinutes + gameMinutes;
    double cap = 48 + (Hash(id.Value, 0) & 0x1F);
    if (since >= cap)            // dawn/dusk already sets decide via reDecideAll
        decide = true;
}
```

At the END of `Decide(...)`, after the winning behaviour is chosen (you have `bestKind`, `bestBuilding`), guard the queue membership before publishing the new `BehaviorSetIntent`:

```csharp
// Queue bookkeeping: if this agent is in a line and the new choice points
// somewhere else (or isn't a Buy), it leaves. If the same shop+Buy still
// wins, keep its place — don't churn the line or reset its behaviour.
if (_shared.AnchorOf(id, out var heldAnchor))
{
    bool sameTarget = bestKind == ActivityKind.Buy
        && bestBuilding == heldAnchor.Building;
    if (sameTarget)
        return;                                  // stay queued in place; no new intent
    Events.Publish(new QueueLeaveIntent { Agent = id });
}
```

> Place this block immediately before the existing `Events.Publish(new BehaviorSetIntent { ... })` (or `IntentSetIntent`) at the end of `Decide`. The early `return` when `sameTarget` prevents resetting `RemainingGameMinutes`/position for an agent that's already correctly queued.

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

## Task 8: Verify serialized service — only the served agent pays

**Files:**
- Test only: `Headless/Sim.MemoryTests/QueueSerializesSaleTests.cs` (create)
- (No production change expected — `EconomySystem` already pays only `Phase == Doing`. This task is a regression guard that the queue actually throttles transactions.)

**Interfaces:**
- Consumes: full `SimWorld` step loop; `EconomySystem.PaySale` gated by `behavior.Phase == ActivityPhase.Doing` (line 201).

- [ ] **Step 1: Write the failing-then-passing regression test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class QueueSerializesSaleTests
    {
        [Fact]
        public void ThreeBuyers_OneCounter_AtMostOneIsDoingPerTick()
        {
            var (e, world) = TestWorld.New();
            var shopBuilding = world.CreateShopWithKeeper();
            var buyers = Enumerable.Range(0, 3).Select(_ => world.SpawnBuyerHeadedTo(shopBuilding)).ToArray();

            int maxDoingAtShop = 0;
            for (int i = 0; i < 500; i++)
            {
                world.Step();
                int doing = buyers.Count(b => world.PhaseOf(b) == ActivityPhase.Doing
                                              && world.TargetBuildingOf(b) == shopBuilding);
                maxDoingAtShop = System.Math.Max(maxDoingAtShop, doing);
            }
            Assert.True(maxDoingAtShop <= 1, $"counter capacity is 1, saw {maxDoingAtShop} simultaneous");
        }
    }
}
```

> Note: if `TestWorld` helpers are unavailable, this becomes an integration test in the `Sim.Host` soak harness instead (assert via the activity histogram that `Doing@shop` never exceeds capacity). Keep the assertion: **never more than `Capacity` agents in `Doing` at one anchor.**

- [ ] **Step 2: Run the test**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter QueueSerializesSaleTests`
Expected: PASS (the queue already throttles `Doing`). If it FAILS with >1 doing, the promotion/capacity logic in Tasks 2/4 has a bug — fix there, not here.

- [ ] **Step 3: Commit**

```bash
git add Headless/Sim.MemoryTests/QueueSerializesSaleTests.cs
git commit -m "test(threshold): regression — one-counter shop serializes Doing/PaySale"
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

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SomaticPerceptTests
    {
        [Fact]
        public void HungerNeed_IsStampedIntoOwnBag()
        {
            var (e, world) = TestWorld.New();
            var id = world.SpawnAgentAt(0, 0);
            world.SetNeed(id, NeedAxis.Hunger, 0.7);

            for (int i = 0; i < 6; i++) world.Step();        // cross a SenseEveryTicks boundary

            var bag = world.PerceivableBag(id);
            Assert.True(bag.TryGet(SomaticAtoms.Hunger, out var v));
            Assert.True(v > Fixed.Zero);
        }
    }
}
```

> Note: if no `TestWorld`, unit-test `SomaticPerceptSystem` directly with a `NeedsRegistry` seeded via `NeedsSetIntent` and a `PerceivableRegistry`; after `sys.Update(5)` + tick + `perceivable.Update(6)`, assert `perceivable.Bag(id).TryGet(SomaticAtoms.Hunger, out _)`.

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
- Produces: a throttled `decide = true` for committed agents on a stagger (`tick % PreemptEveryTicks == Hash(id.Value, 1) % PreemptEveryTicks`); inside `Decide`, when the agent is already committed (currently `Queued`/`Doing` with a real activity), only switch if the winner beats the current commitment's score by `Hysteresis`.
- Constants: `const int PreemptEveryTicks = 5;`, `const double Hysteresis = 0.15;` (both tunable — spec open questions).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddPreemptionTests
    {
        [Fact]
        public void UrgentHunger_BalksFromQueue_BeforeCap()
        {
            var (e, world) = TestWorld.New();
            var id = world.SpawnQueuedBuyer(building: 7);
            world.SetNeed(id, NeedAxis.Hunger, 1.4);          // far outranks waiting to Buy
            world.MakeFoodAtHomeCheaper(id);                  // EatHome should now win

            world.StepN(PreemptCadence());                    // < the 48-min cap
            Assert.Contains(world.DrainEvents<QueueLeaveIntent>(), lv => lv.Agent.Equals(id));
        }

        [Fact]
        public void MarginalGain_DoesNotThrash()
        {
            var (e, world) = TestWorld.New();
            var id = world.SpawnQueuedBuyer(building: 7);
            world.SetNeed(id, NeedAxis.Hunger, 0.32);         // only slightly tips another action
            world.StepN(PreemptCadence());
            Assert.DoesNotContain(world.DrainEvents<QueueLeaveIntent>(), lv => lv.Agent.Equals(id));
        }

        static int PreemptCadence() => 6;   // one cadence window
    }
}
```

> Note: if `TestWorld` helpers don't exist, drive `OddSystem` directly: seed the agent as queued (`QueueJoinIntent` + `Queued` behaviour), set `NeedsData`, and step `OddSystem.Update` across one `PreemptEveryTicks` window; assert on published `QueueLeaveIntent`. The two facts encode the core invariants: urgency balks; marginal gain does not thrash.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddPreemptionTests`
Expected: FAIL — committed agents are not re-valued before the cap; no balk.

- [ ] **Step 3: Add the throttled trigger**

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

- [ ] **Step 4: Add hysteresis to the switch**

Inside `Decide`, after scoring all ads but before committing, compute the *current commitment's* score and gate the switch. Locate where the winner (`bestKind`, its score) is selected (the `OddTree.Traverse` result and its `verbScore`). Add:

```csharp
// Anti-thrash: a committed agent (Queued/Doing) only switches if the winner
// clearly beats staying put. Find the current activity's score among the ads.
bool committed = current != null
    && (current.Phase == ActivityPhase.Queued || current.Phase == ActivityPhase.Doing)
    && current.Activity != ActivityKind.None;
if (committed)
{
    double currentScore = 0;
    if (verbIndex.TryGetValue(current.Activity, out var ci))
        currentScore = verbScore[ci];
    double winnerScore = verbScore[/* index of bestKind */ verbIndex[bestKind]];
    if (winnerScore <= currentScore * (1.0 + Hysteresis))
    {
        // Not worth switching — keep the current commitment untouched.
        if (_shared.AnchorOf(id, out _)) return;   // stay in line, no new intent
        // For Doing (non-queued), also leave the behaviour as-is:
        return;
    }
}
```

> The exact variable names (`verbScore`, `verbIndex`, `bestKind`) come from `OddSystem.Decide` lines 185-224. If `bestKind`'s score isn't already retained, capture it where `winner` is computed: `double winnerScore = verbScore[winner < verbScore.Count ? winner : 0];`. Keep this block BEFORE the Task-7 queue-leave block so a no-switch returns early without leaving.

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
- Produces: while `behavior.Activity == ActivityKind.Sleep`, `preemptTick` does NOT trigger a decide; only the normal night/dawn/cap path OR a `DamageEvent` targeting the sleeper sets `decide = true`.

> Loud-noise wake is deferred: no loud-noise event exists in `SimEvents.cs` yet (`HeardUtterance` is speech, not a salient physical noise). Scope this task to wake-on-hit; note loud-noise as future.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddSleepGatingTests
    {
        [Fact]
        public void Sleeping_IgnoresPreemption()
        {
            var (e, world) = TestWorld.New();
            var id = world.SpawnSleeper(id: 1);               // Activity=Sleep, Doing, night
            world.SetNeed(id, NeedAxis.SocialDef, 0.9);       // a tempting non-urgent pull
            world.StepN(6);                                   // a full preempt window
            Assert.DoesNotContain(world.DrainBehaviorChanges(id),
                bd => bd.Activity != ActivityKind.Sleep);     // never woke for a mere social itch
        }

        [Fact]
        public void Sleeping_WakesOnDamage()
        {
            var (e, world) = TestWorld.New();
            var id = world.SpawnSleeper(id: 1);
            world.Publish(new DamageEvent { Target = id, Source = new EntityId(2), Amount = 3 });
            world.StepN(2);
            Assert.Contains(world.DrainBehaviorChanges(id),
                bd => bd.Activity != ActivityKind.Sleep);     // a hit forced a re-decide
        }
    }
}
```

> Note: if `TestWorld` helpers don't exist, drive `OddSystem` directly with a sleeping `BehaviorData`, publish a `DamageEvent`, and assert a `BehaviorSetIntent` away from `Sleep` is (or isn't) published.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddSleepGatingTests`
Expected: FAIL — sleepers currently get preempted by the cadence; no damage-wake path.

- [ ] **Step 3: Gate sleep in the decide loop**

In `OddSystem.Update`, before the phase branches, build the set of sleepers hit this tick, and special-case `Sleep`:

```csharp
// Hits force a sleeper awake; nothing else interrupts sleep (percepts are suppressed).
var hitThisTick = new HashSet<EntityId>();
foreach (ref readonly var d in Events.GetEvents<DamageEvent>())
    hitThisTick.Add(d.Target);
```

Then inside the per-agent loop, when the agent is asleep:

```csharp
if (behavior.Activity == ActivityKind.Sleep)
{
    // Suppress percept-driven preemption while asleep. Wake only on a hit, or
    // let the normal night→dawn / cap path end sleep as before.
    bool woke = hitThisTick.Contains(id);
    double sinceSleep = behavior.SinceDecisionGameMinutes + gameMinutes;
    double capSleep = 48 + (Hash(id.Value, 0) & 0x1F);
    decide = reDecideAll || woke || behavior.RemainingGameMinutes - gameMinutes <= 0
             || sinceSleep >= capSleep;
    if (decide) Decide(id, kv.Value, behavior, behavior.RemainingGameMinutes - gameMinutes, clock.Hour, tick);
    continue;   // skip the generic Doing/Queued preemption branches
}
```

> Place this `Sleep` special-case at the top of the loop body, right after `_behavior.TryGet(id, out behavior)` succeeds and before the `Doing`/`Queued` branches from Tasks 7/10. The `continue` ensures the cadence-based `preemptTick` never applies to a sleeper.

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

- [ ] **Step 3: If a signal is wrong, debug at its source**

- Lines never form → check Task 5 routing (`Buy` → `Queued`) and Task 4 promotion.
- Deadlock (stuck `Queued`) → check Task 4 promote loop and Task 7/10 re-decide triggers fire for `Queued`.
- Thrash (agents join/leave every cadence) → raise `Hysteresis` (Task 10).
- Economy collapse → raise `ShopCapacity` (Task 5) or shorten `Buy` `serviceTime`; re-soak.

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
- Seams designed-for: `protocolState`/anchor model (Task 2) accommodates Barter/Combat; somatic atoms (Task 9) ready for sleep-salience/future; single PaySale call-site preserved (Task 8). ✓
- **Doors (Phase 3) are intentionally NOT in this plan** — they reuse this runtime and get their own plan (`Open`/`Close` executors, movement-time door trigger, wall gates + building doors, curfew). Recorded as out of scope here.

**Placeholder scan:** no "TBD"/"add error handling"/"similar to Task N". Each task carries real test + implementation code. The "Note" callouts on Tasks 5-11 give a concrete fallback (drive the system directly, assert on published intents) when a `TestWorld` harness is absent — they are alternatives, not gaps.

**Type consistency:** `QueueAnchor`, `QueueJoinIntent`/`QueueLeaveIntent`, `SharedActivityInstance`, `SharedActivityRegistry` (reads: `TryGet`, `AnchorOf`, `IsServed`, `PositionOf`, `ServedSnapshot`), `SharedActivitySystem`, `SomaticAtoms.{Hunger,Energy,Fear}`, `SomaticPerceptSystem`, `ActivityPhase.Queued`, constants `QueueSpacing`/`ShopCapacity`/`SenseEveryTicks`/`PreemptEveryTicks`/`Hysteresis` — all referenced names are defined in the task that introduces them and used consistently downstream.

**Known uncertainty to confirm during execution (not placeholders — verify against the live code):**
1. `NeedsRegistry` may need a public `All` enumerator (flagged in Task 9 Step 4).
2. The exact constructor parameter list/order for `ExecutionSystem`/`OddSystem`/`MovementSystem` — append the new registry and match the call site in `SimWorld.cs`.
3. Inside `OddSystem.Decide`, the precise local names for the winner's score (`verbScore`/`verbIndex`/`bestKind`) — confirm at lines 185-224 and capture the winner's score there.
4. Whether a `TestWorld` integration harness exists; if not, use the per-task unit fallback noted in each test step.
