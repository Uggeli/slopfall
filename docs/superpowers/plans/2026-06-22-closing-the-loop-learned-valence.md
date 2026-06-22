# Closing the Loop: Learned Social Valence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Steps use `- [ ]`.

**Goal:** Agents read a perceived stranger's valence from their **own learned memory** — reinforce the new `MeaningsStore` categories from social-interaction outcomes (Part 1, additive), then blend that learned valence into `Interpret` weighted by confidence (Part 2, behavior changes, no day-1 regression).

**Architecture:** A `MemoryReinforceSystem` folds the 4 social events into the new store via the registry; `Interpret`'s stranger `baseValence` becomes `lerp(oldValence, newValence, newConfidence)`. Spec: `docs/superpowers/specs/2026-06-22-closing-the-loop-learned-valence-design.md`.

**Tech Stack:** C# (.NET 10, C# 7.3), xUnit. Branch `perceivable-atoms`.

## Global Constraints

- **No mangling:** `Interpret` legitimately produces a valence; it now reads the new store. Confidence-weighted blend (conf 0 = today's behavior; conf 1 = fully learned).
- **CQRS:** reinforcement flows as intents the sole-writer `AgentMemoryRegistry` applies; systems stateless.
- **No floats** beyond `Fixed`/the existing `double` interpretation math. **C# 7.3.**
- **Outcomes mirror the old constants:** Grant +1.0, Refuse −1.0, Greet +0.5, Dislike −0.5.
- Test runner: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`.

## File Structure

- `Assets/Sim/Engine/Units/PerceivableRegistry.cs` — add `Signature(id)` (Task 1)
- `Assets/Sim/Memory/MeaningsStore.cs` — add `RecognizedValence(...)` (Task 1)
- `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` — add `MemoryReinforceIntent` + handling (Task 1)
- `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs` (Task 2)
- `Assets/Sim/Engine/Units/MemoryWriteSystem.cs` — refactor to use `Signature(id)` (Task 1)
- `Assets/Sim/Engine/Units/SubjectiveSystem.cs` + `SimWorld.cs` (Task 3)
- `Assets/Sim/Engine/EngineSoak.cs` — valence/confidence metric (Task 3)
- Tests: `LearnedValenceReinforceTests.cs`, `MemoryReinforceSystemTests.cs`, `LearnedValenceBlendTests.cs`

---

### Task 1: Reinforce foundations — `Signature`, `RecognizedValence`, reinforce intent

**Files:** Modify `PerceivableRegistry.cs`, `Assets/Sim/Memory/MeaningsStore.cs`, `AgentMemoryRegistry.cs`, `MemoryWriteSystem.cs`; create `Headless/Sim.MemoryTests/LearnedValenceReinforceTests.cs`.

**Interfaces:**
- `AtomBag PerceivableRegistry.Signature(EntityId id)` — identity atoms (`Type.Value < PerceivableAtoms.ActivityBase`) of the entity's bag; `AtomBag.Empty` if none.
- `bool MeaningsStore.RecognizedValence(AtomBag signature, out Fixed valence, out Fixed confidence)` — `Recognize`→`TryGetNode`; false + zeros if unrecognized.
- intent `MemoryReinforceIntent { EntityId Perceiver; AtomBag Signature; Fixed Outcome; }`; `AgentMemoryRegistry.Update` applies it: `Recognize(Signature)`; if not `None`, `Reinforce(catId, Signature, Outcome)`.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/LearnedValenceReinforceTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class LearnedValenceReinforceTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Signature_ReturnsIdentityAtomsOnly()
        {
            var e = new EventBus();
            var p = new PerceivableRegistry(e);
            var id = new EntityId(7);
            p.Seed(id, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);   // identity
            p.Seed(id, PerceivableAtoms.Activity(ActivityKind.Beg), Fixed.One);      // state
            var sig = p.Signature(id);
            Assert.DoesNotContain(sig.Atoms, a => a.Type.Value >= PerceivableAtoms.ActivityBase);
            Assert.Contains(sig.Atoms, a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
        }

        [Fact]
        public void RecognizedValence_KnownCategory_ReturnsValenceConfidence()
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            var sig = Bag((1001, 1.0));
            var id = m.AddNode(sig, Fixed.FromDouble(-0.5), Fixed.FromDouble(0.8), false);
            Assert.True(m.RecognizedValence(sig, out var v, out var c));
            Assert.Equal(Fixed.FromDouble(-0.5), v);
            Assert.Equal(Fixed.FromDouble(0.8), c);
        }

        [Fact]
        public void RecognizedValence_Unknown_FalseAndZero()
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            Assert.False(m.RecognizedValence(Bag((9999, 1.0)), out var v, out var c));
            Assert.Equal(Fixed.Zero, v);
            Assert.Equal(Fixed.Zero, c);
        }

        [Fact]
        public void ReinforceIntent_MovesRecognizedCategoryValence()
        {
            var e = new EventBus();
            var r = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            r.Seed(new EntityId(1));
            var sig = Bag((1001, 1.0));
            r.TryGet(new EntityId(1), out var mem);
            mem.Meanings.AddNode(sig, Fixed.Zero, Fixed.FromDouble(0.5), false);   // a known category, neutral

            for (int i = 0; i < 10; i++)
                e.Publish(new MemoryReinforceIntent { Perceiver = new EntityId(1), Signature = sig, Outcome = Fixed.FromDouble(1.0) });
            e.Tick(); r.Update(0);

            r.TryGet(new EntityId(1), out mem);
            mem.Meanings.RecognizedValence(sig, out var v, out _);
            Assert.True(v.ToDouble() > 0.3);   // reinforced positive
        }

        [Fact]
        public void ReinforceIntent_UnrecognizedSignature_NoOp()
        {
            var e = new EventBus();
            var r = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            r.Seed(new EntityId(1));
            e.Publish(new MemoryReinforceIntent { Perceiver = new EntityId(1), Signature = Bag((9999, 1.0)), Outcome = Fixed.One });
            e.Tick(); r.Update(0);
            r.TryGet(new EntityId(1), out var mem);
            Assert.Equal(0, mem.Meanings.Count);   // nothing minted/changed
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement.**
  - In `Assets/Sim/Engine/Units/PerceivableRegistry.cs`, add:

    ```csharp
        /// <summary>The entity's identity atoms only (below the activity range) — its recognition signature.</summary>
        public AtomBag Signature(EntityId id)
        {
            AtomBag full = Bag(id);
            List<Atom> ids = null;
            for (int i = 0; i < full.Count; i++)
            {
                if (full[i].Type.Value >= PerceivableAtoms.ActivityBase) continue;
                if (ids == null) ids = new List<Atom>(full.Count);
                ids.Add(full[i]);
            }
            return ids == null ? AtomBag.Empty : AtomBag.Create(ids);
        }
    ```
  - In `Assets/Sim/Memory/MeaningsStore.cs`, add (after `Recognize`):

    ```csharp
        /// <summary>Recognize a signature and return the matched category's valence + confidence.
        /// False with zeros when nothing is recognized.</summary>
        public bool RecognizedValence(AtomBag signature, out Fixed valence, out Fixed confidence)
        {
            CategoryId id = Recognize(signature);
            if (!id.IsNone && TryGetNode(id, out var node))
            {
                valence = node.Valence; confidence = node.Confidence; return true;
            }
            valence = Fixed.Zero; confidence = Fixed.Zero; return false;
        }
    ```
  - In `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs`, add the intent + handling. Add struct near the others:

    ```csharp
    /// <summary>Reinforce the perceiver's recognized category (of the signature) toward an outcome.</summary>
    public struct MemoryReinforceIntent : IEvent { public EntityId Perceiver; public AtomBag Signature; public Fixed Outcome; }
    ```
    and inside `Update`, after the perceive loop (before consolidate):

    ```csharp
            var reinforce = Events.GetEvents<MemoryReinforceIntent>();
            for (int i = 0; i < reinforce.Length; i++)
            {
                if (!_d.TryGetValue(reinforce[i].Perceiver, out var rm)) continue;
                CategoryId cat = rm.Meanings.Recognize(reinforce[i].Signature);
                if (!cat.IsNone) rm.Meanings.Reinforce(cat, reinforce[i].Signature, reinforce[i].Outcome);
            }
    ```
  - Refactor `MemoryWriteSystem.IdentityOnly` usage to `_perceivable.Signature(other)` and delete the private `IdentityOnly` helper (DRY). (Verify the test `MemoryWriteSystemTests` still passes.)

- [ ] **Step 4: Run, verify PASS** (incl. existing `MemoryWriteSystemTests`).
- [ ] **Step 5: Commit** `feat(memory): reinforce intent + RecognizedValence + Signature helper`.

---

### Task 2: `MemoryReinforceSystem` — social outcomes → learned valence

**Files:** Create `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs`, `Headless/Sim.MemoryTests/MemoryReinforceSystemTests.cs`; modify `SimWorld.cs`.

**Interfaces:** `MemoryReinforceSystem : SimSystem`, ctor `(EventBus, PerceivableRegistry)`. `Update`: for each of `HelpGrantedEvent`(Asker←Giver, +1), `HelpRefusedEvent`(Asker←Refuser, −1), `GreetingEvent`(A←B and B←A, +0.5), `DislikeNearbyEvent`(Who←Whom, −0.5), emit `MemoryReinforceIntent { Perceiver = self, Signature = _perceivable.Signature(other), Outcome }` (skip if signature empty).

- [ ] **Step 1: Write the failing test** — `Headless/Sim.MemoryTests/MemoryReinforceSystemTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryReinforceSystemTests
    {
        [Fact]
        public void HelpGranted_EmitsPositiveReinforce_ForGiversSignature()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryReinforceSystem(e, perceivable);
            var asker = new EntityId(1); var giver = new EntityId(2);
            perceivable.Seed(giver, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);

            e.Publish(new HelpGrantedEvent { Asker = asker, Giver = giver, Amount = 1 });
            e.Tick(); perceivable.Update(0); sys.Update(0);
            e.Tick();

            var emitted = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Single(emitted);
            Assert.Equal(asker, emitted[0].Perceiver);
            Assert.True(emitted[0].Outcome.ToDouble() > 0);   // grant = positive
            Assert.Contains(emitted[0].Signature.Atoms, a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
        }

        [Fact]
        public void Refused_IsNegative_Greeting_IsBothWays()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryReinforceSystem(e, perceivable);
            foreach (var id in new[] { 1, 2, 3, 4 })
                perceivable.Seed(new EntityId(id), PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);

            e.Publish(new HelpRefusedEvent { Asker = new EntityId(1), Refuser = new EntityId(2) });
            e.Publish(new GreetingEvent { A = new EntityId(3), B = new EntityId(4) });
            e.Tick(); perceivable.Update(0); sys.Update(0);
            e.Tick();

            var emitted = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Contains(emitted, x => x.Perceiver.Value == 1 && x.Outcome.ToDouble() < 0);   // refused
            Assert.Contains(emitted, x => x.Perceiver.Value == 3 && x.Outcome.ToDouble() > 0);   // greet A<-B
            Assert.Contains(emitted, x => x.Perceiver.Value == 4 && x.Outcome.ToDouble() > 0);   // greet B<-A
        }
    }
}
```

- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** — `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Folds social-interaction outcomes into the perceiver's learned categories (the new MeaningsStore),
    /// mirroring the old MeaningsSystem's signal but landing it in per-agent atom-categories. Stateless;
    /// reads the 4 outcome events + Perceivable (for the other's signature), emits MemoryReinforceIntents
    /// the AgentMemoryRegistry applies.
    /// </summary>
    public sealed class MemoryReinforceSystem : SimSystem
    {
        const double Grant = 1.0, Refuse = -1.0, Greet = 0.5, Dislike = -0.5;

        readonly PerceivableRegistry _perceivable;

        public MemoryReinforceSystem(EventBus events, PerceivableRegistry perceivable) : base(events) { _perceivable = perceivable; }

        public override void Update(long tick)
        {
            foreach (ref readonly var g in Events.GetEvents<HelpGrantedEvent>()) Emit(g.Asker, g.Giver, Grant);
            foreach (ref readonly var f in Events.GetEvents<HelpRefusedEvent>()) Emit(f.Asker, f.Refuser, Refuse);
            foreach (ref readonly var gr in Events.GetEvents<GreetingEvent>()) { Emit(gr.A, gr.B, Greet); Emit(gr.B, gr.A, Greet); }
            foreach (ref readonly var d in Events.GetEvents<DislikeNearbyEvent>()) Emit(d.Who, d.Whom, Dislike);
        }

        void Emit(EntityId self, EntityId other, double outcome)
        {
            AtomBag sig = _perceivable.Signature(other);
            if (sig.Count == 0) return;
            Events.Publish(new MemoryReinforceIntent { Perceiver = self, Signature = sig, Outcome = Fixed.FromDouble(outcome) });
        }
    }
}
```

- [ ] **Step 4: Wire into `SimWorld`** — add to the systems array after `ConsolidationSystem`:
  `new MemoryReinforceSystem(e, Perceivable),`.
- [ ] **Step 5: Run, verify PASS;** run a 1-day soak — behavior histogram unchanged in shape (additive),
  and the `mem[…]` line's categories still form. (Valence learning is internal; Task 3 adds its metric.)
- [ ] **Step 6: Commit** `feat(memory): MemoryReinforceSystem — social outcomes teach learned categories`.

---

### Task 3: Repoint `Interpret` (the blend) + soak metric + validation

**Files:** Modify `Assets/Sim/Engine/Units/SubjectiveSystem.cs`, `Assets/Sim/Engine/SimWorld.cs`, `Assets/Sim/Engine/EngineSoak.cs`; create `Headless/Sim.MemoryTests/LearnedValenceBlendTests.cs`.

**Interfaces:** `SubjectiveSystem` ctor gains `PerceivableRegistry perceivable, AgentMemoryRegistry agentMem`. In `Interpret`, the stranger branch blends old + new valence by the new category's confidence.

- [ ] **Step 1: Read `SubjectiveSystem`** — confirm `Interpret`'s signature/locals (`self`, `other`,
  `residency`, `meanings`) and the `else { baseValence = MeaningsSystem.CategoryValence(...); }` block,
  and the ctor parameter list + the `SimWorld` construction line for `SubjectiveSystem`.

- [ ] **Step 2: Write the failing test (blend math)** — `Headless/Sim.MemoryTests/LearnedValenceBlendTests.cs`.
  Test the blend as a pure helper. Add a static helper on `SubjectiveSystem`:
  `public static double BlendValence(double oldV, double newV, double confidence)` and test:

```csharp
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class LearnedValenceBlendTests
    {
        [Fact]
        public void Confidence0_IsAllOld()    => Assert.Equal(-0.7, SubjectiveSystem.BlendValence(-0.7, 0.9, 0.0), 6);
        [Fact]
        public void Confidence1_IsAllNew()     => Assert.Equal(0.9, SubjectiveSystem.BlendValence(-0.7, 0.9, 1.0), 6);
        [Fact]
        public void ConfidenceHalf_IsMidpoint() => Assert.Equal(0.1, SubjectiveSystem.BlendValence(-0.7, 0.9, 0.5), 6);
    }
}
```

- [ ] **Step 3: Implement.**
  - Add to `SubjectiveSystem`:

    ```csharp
        /// <summary>Confidence-weighted blend of the old role-scalar valence and the new learned
        /// category valence: confidence 0 = old (today's behavior), 1 = fully learned.</summary>
        public static double BlendValence(double oldV, double newV, double confidence)
            => oldV * (1.0 - confidence) + newV * confidence;
    ```
  - Add `PerceivableRegistry _perceivable; AgentMemoryRegistry _agentMem;` fields + ctor params (assign).
  - Replace the stranger `else` block:

    ```csharp
                else
                {
                    double oldV = MeaningsSystem.CategoryValence(meanings, residency, self, other);
                    double newV = 0, conf = 0;
                    if (_agentMem.TryGet(self, out var mem)
                        && mem.Meanings.RecognizedValence(_perceivable.Signature(other), out var lv, out var lc))
                    { newV = lv.ToDouble(); conf = lc.ToDouble(); }
                    baseValence = BlendValence(oldV, newV, conf);
                }
    ```
    (Add `using DaggerfallWorkshop.Sim.Memory;` if needed for `Fixed`/store types — `_perceivable`/`_agentMem` are Engine types.)
  - In `SimWorld.cs`, update the `new SubjectiveSystem(...)` line to pass `Perceivable, AgentMemory` (append the two args matching the new ctor order).

- [ ] **Step 4: Soak metric.** In `EngineSoak.cs`'s memory line, also compute over `w.AgentMemory.All`:
  mean of `|node.Valence|` and mean `node.Confidence` across all category nodes (iterate
  `mem.Meanings[i]` for `i < Count`). Append `val[meanAbs=… conf=…]` — the "blend lean": as conf rises,
  behavior leans on learned valence. (Add a `MeaningsStore` indexer/`this[int]` + `Count` if not present
  — `MeaningsStore` already exposes `this[int]` and `Count`.)

- [ ] **Step 5: Validate.**
  - Run the full memory suite — green.
  - **1-day soak:** assert (by eye) the activity histogram shape ≈ baseline at 11:29 (conf≈0 day 1 →
    blend ≈ old → no regression), and `val[conf=…]` rises above 0 over the day (reinforcement working).
  - **Multi-day soak (3 days):** `--soak Daggerfall "Gothway Garden" 3` — watch `val[conf]` climb and the
    histogram **drift** from baseline as agents lean on learned valence. Capture the output for the report.

- [ ] **Step 6: Commit** `feat(memory): Interpret blends learned valence by confidence (loop closed)`.

---

## Self-Review

**Spec coverage:** reinforcement intent + registry handling (T1), `MemoryReinforceSystem` from the 4 events
(T2), confidence-weighted `Interpret` blend (T3), soak metric + multi-day validation (T3). Old meanings
kept as fallback; OddSystem occupant path + meanings retirement deferred (spec scope).

**Type consistency:** `MeaningsStore.Recognize`/`TryGetNode`/`Reinforce`/`RecognizedValence`/`this[int]`/
`Count` (A3); `PerceivableRegistry.Signature`/`Bag`; `AgentMemoryRegistry.TryGet`; event field names
(Asker/Giver, Asker/Refuser, A/B, Who/Whom); outcomes +1/−1/+0.5/−0.5. `BlendValence` pure + tested.

**Behavior safety:** the blend starts at the old valence (confidence 0) so day-1 behavior matches
baseline — validated explicitly. Divergence is gradual and observable via `val[conf]`. Reinforcement
(T1/T2) is additive and behavior-neutral until T3 flips the read.

**Risk:** T3 touches `SubjectiveSystem` (behavior-critical) and its `SimWorld` construction; Step 1 reads
both first. Validation is soak-based (the no-regression assertion is the day-1 histogram).
