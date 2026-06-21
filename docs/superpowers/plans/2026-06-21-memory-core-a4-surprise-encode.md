# Memory Core A4 — Surprise Operators + Encode Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the write path's front half — per-atom prediction-error surprise (MAX for attention, MEAN for encoding), the ungated per-recognition `StatFold`, and the encode gate that turns a percept into a `MemoryRecord` (delta vs the category, verbatim when novel, strength from `max(surprise, arousal)`).

**Architecture:** A pure `Surprise` value (two aggregations of per-atom error vs a category's `Prediction`), a fold-only `MeaningsStore.Fold` (the ungated StatFold the A3 review flagged), and a `MemoryEncoder.Perceive` that composes recognition (A3) + surprise (A4) + `Diff` (A1) + the gate into an `EncodeResult` carrying the would-be `MemoryRecord` (A2). No live wiring — `Perceive` is a pure function over a store.

**Tech Stack:** C# (.NET 10, C# 7.3-compatible), xUnit 2.9.3. Builds on A1 (`AtomBag.Diff`, `Fixed`), A2 (`MemoryRecord`, `MemoryKey`, `MemoryFlags`, `CategoryId`), A3 (`MeaningsStore`, `CategoryNode`, `PredictedStats`, `MeaningsConfig`).

**Spec:** [`docs/what_is_a_memory.md`](../../what_is_a_memory.md) — the surprise bullet under "The MEANINGS store", and "The write path — detect at 2, write at 5". Roadmap: [`docs/superpowers/specs/2026-06-21-memory-core-phaseA-roadmap.md`](../specs/2026-06-21-memory-core-phaseA-roadmap.md), section **A4**.

## Global Constraints

- **Namespace:** `DaggerfallWorkshop.Sim.Memory`. Test namespace `Sim.MemoryTests`.
- **No floats anywhere.** Surprise aggregation is integer (MAX, and MEAN via integer division); the gate compares raw ints; strength scales by clamping a raw int to a byte. `double` only in test-only `Fixed.FromDouble`.
- **Determinism:** surprise walks the two sorted atom bags in type order; all comparisons are integer.
- **The two operators:** AttentionSurprise = **MAX** per-atom error; EncodeSurprise = **MEAN** (sum/count) per-atom error. Per-atom error over the **union** of percept and prediction atom types: both present → `|percept - predicted|`; one side only → that side's `|value|`.
- **Novelty rule:** recognition `None` ⇒ `Surprise.Maximal` (both = 1.0) and the deltaBag is the **full percept** (verbatim); no `Fold` (nothing to fold into — MINT is A5).
- **Gate:** write iff `EncodeSurprise.Raw > cfg.SurpriseThresholdRaw` OR `arousal.Raw > cfg.ArousalThresholdRaw`. `strength = Scale(max(EncodeSurprise.Raw, arousal.Raw))` where `Scale` clamps to `[0,255]`. `SURPRISE` flag iff the surprise arm fired. `INNATE` is never set by encoding (seeds only).
- **C# 7.3** in core files. **Pure types only** — no registry/system/`SimWorld` wiring.
- **Test runner:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`, green at the end of every task.

## Scope boundary (what A4 does NOT build)

- **Recall / Door 1 / reconsolidation `Refresh`-on-recall** → A5/Phase B (A4 is the write-out path only).
- **Plan-surprise** (the ad-promised-vs-landed second predictor) → Phase B wiring (A4 handles perceptual surprise).
- **The attention pipeline that consumes `AttentionSurprise`** (top-K, rumination) → Phase B. A4 computes and returns it; nothing downstream of it is built here.
- **Actually inserting the record into a `MemoryStore`** → Phase B wiring. `Perceive` returns the would-be `MemoryRecord`; the caller (Phase B) routes it to the right store via `Encode`. (A4 keeps the encoder a pure decision so it stays testable in isolation.)

## File Structure

- `Assets/Sim/Memory/Surprise.cs` — the two-operator surprise value (Task 1)
- `Assets/Sim/Memory/MeaningsStore.cs` — add `Fold` (Task 2)
- `Assets/Sim/Memory/EncodeConfig.cs` — gate thresholds (Task 3)
- `Assets/Sim/Memory/MemoryEncoder.cs` — `EncodeResult` + `Perceive` (Task 3)
- Tests: `SurpriseTests.cs`, `MeaningsStoreFoldTests.cs`, `MemoryEncoderTests.cs`

---

### Task 1: `Surprise` — per-atom prediction error, two aggregations

**Files:**
- Create: `Assets/Sim/Memory/Surprise.cs`
- Create: `Headless/Sim.MemoryTests/SurpriseTests.cs`

**Interfaces:**
- Consumes: `AtomBag`, `Atom`, `Fixed` (A1).
- Produces: `Surprise` — `readonly struct`; fields `Fixed Attention`, `Fixed Encode`; ctor `Surprise(Fixed attention, Fixed encode)`; `static readonly Surprise Maximal` (= `(One, One)`); `static Surprise Against(AtomBag percept, AtomBag prediction)` — per-atom error over the union (both present → `|Δ|`; one side → `|value|`), `Attention` = MAX, `Encode` = MEAN (sum/count, integer); both `Fixed.Zero` when both bags empty.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/SurpriseTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SurpriseTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Maximal_IsOneOne()
        {
            Assert.Equal(Fixed.One, Surprise.Maximal.Attention);
            Assert.Equal(Fixed.One, Surprise.Maximal.Encode);
        }

        [Fact]
        public void PerfectMatch_IsZeroSurprise()
        {
            var bag = Bag((1, 1.0), (2, 0.5));
            var s = Surprise.Against(bag, bag);
            Assert.Equal(Fixed.Zero, s.Attention);
            Assert.Equal(Fixed.Zero, s.Encode);
        }

        [Fact]
        public void BothEmpty_IsZero()
        {
            var s = Surprise.Against(AtomBag.Empty, AtomBag.Empty);
            Assert.Equal(Fixed.Zero, s.Attention);
            Assert.Equal(Fixed.Zero, s.Encode);
        }

        [Fact]
        public void NotchedEar_SpikesAttention_ButLowEncode()
        {
            // Prediction: 5 stable features at 1.0. Percept: those 5 (matching) + 1 new atom (the
            // notched ear) at 1.0. MAX over {0,0,0,0,0,1.0} = 1.0 (attention spikes); MEAN = 1/6.
            var prediction = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (99, 1.0));
            var s = Surprise.Against(percept, prediction);

            Assert.Equal(Fixed.One, s.Attention);                  // MAX = the new atom, full spike
            Assert.True(s.Encode.ToDouble() < 0.2);                // MEAN diluted by the 5 matches
            Assert.True(s.Encode.ToDouble() > 0.0);                // but non-zero (something WAS new)
        }

        [Fact]
        public void TotallyWrong_IsHighOnBothOperators()
        {
            // Every predicted feature (1.0) is contradicted (percept 0.0): error 1.0 on all 5.
            var prediction = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            var percept = Bag((1, 0.0), (2, 0.0), (3, 0.0), (4, 0.0), (5, 0.0));
            var s = Surprise.Against(percept, prediction);
            Assert.Equal(Fixed.One, s.Attention);
            Assert.True(s.Encode.ToDouble() > 0.9);                // MEAN of all-1.0 errors ~ 1.0
        }

        [Fact]
        public void MissingExpectedFeature_CountsAsError()
        {
            // Prediction expects type 2 at 1.0; percept lacks it -> error = the expected magnitude.
            var prediction = Bag((1, 1.0), (2, 1.0));
            var percept = Bag((1, 1.0));
            var s = Surprise.Against(percept, prediction);
            Assert.Equal(Fixed.One, s.Attention);                  // the absent feature is a full violation
            Assert.True(s.Encode.ToDouble() > 0.4);                // MEAN over {0 (type1), 1.0 (type2)} = 0.5
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Surprise` does not exist.

- [ ] **Step 3: Implement `Surprise`**

Create `Assets/Sim/Memory/Surprise.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Per-atom prediction error, aggregated two ways for its two consumers (the p2 lesson: same
    /// operator family, two sites, chosen per site). ATTENTION = MAX — the worst single violation,
    /// so one alarming feature spikes attention and is never averaged away. ENCODE = MEAN
    /// (sum/count) — an encoding-volume question, so one trivial new atom among many matches scores
    /// low and does NOT etch trivia at full strength (the notched-ear test). Error per atom type
    /// over the union of percept and prediction: both present -> |Δ|; one side only -> |value|.
    /// </summary>
    public readonly struct Surprise
    {
        public readonly Fixed Attention;
        public readonly Fixed Encode;

        public Surprise(Fixed attention, Fixed encode) { Attention = attention; Encode = encode; }

        /// <summary>Recognition found no category — nothing to predict against, maximal surprise.</summary>
        public static readonly Surprise Maximal = new Surprise(Fixed.One, Fixed.One);

        public static Surprise Against(AtomBag percept, AtomBag prediction)
        {
            long max = 0;
            long sum = 0;
            int n = 0;
            int i = 0, j = 0;
            while (i < percept.Count && j < prediction.Count)
            {
                int cmp = percept[i].Type.CompareTo(prediction[j].Type);
                long e;
                if (cmp < 0) { e = Abs(percept[i].Value.Raw); i++; }
                else if (cmp > 0) { e = Abs(prediction[j].Value.Raw); j++; }
                else { e = Abs((long)percept[i].Value.Raw - prediction[j].Value.Raw); i++; j++; }
                sum += e; if (e > max) max = e; n++;
            }
            while (i < percept.Count) { long e = Abs(percept[i].Value.Raw); sum += e; if (e > max) max = e; n++; i++; }
            while (j < prediction.Count) { long e = Abs(prediction[j].Value.Raw); sum += e; if (e > max) max = e; n++; j++; }

            if (n == 0) return new Surprise(Fixed.Zero, Fixed.Zero);
            return new Surprise(new Fixed((int)max), new Fixed((int)(sum / n)));
        }

        static long Abs(long x) { return x < 0 ? -x : x; }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/Surprise.cs Headless/Sim.MemoryTests/SurpriseTests.cs
git commit -m "feat(memory): Surprise — per-atom error, MAX attention vs MEAN encode"
```

---

### Task 2: `MeaningsStore.Fold` — the ungated per-recognition StatFold

**Files:**
- Modify: `Assets/Sim/Memory/MeaningsStore.cs` (add `Fold`)
- Create: `Headless/Sim.MemoryTests/MeaningsStoreFoldTests.cs`

**Interfaces:**
- Produces: `bool Fold(CategoryId id, AtomBag percept)` — folds the percept atoms into the node's `Predicted` running stats ONLY (no valence/confidence change). Returns false if the id is absent. This is the ungated StatFold the spec emits on every recognition, distinct from the outcome-bearing `Reinforce`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MeaningsStoreFoldTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MeaningsStoreFoldTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Fold_UpdatesPredictedStats_OnlyLeavingValenceAndConfidence()
        {
            var s = new MeaningsStore(8, MeaningsConfig.Default);
            var id = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), false);

            for (int i = 0; i < 4; i++) Assert.True(s.Fold(id, Bag((1, 1.0), (2, 1.0))));

            s.TryGetNode(id, out var node);
            Assert.Equal(2, node.Predicted.TypeCount);
            Assert.Equal(1, node.Prediction(MeaningsConfig.Default).Count == 2 ? 1 : 0);   // both predicted
            Assert.Equal(Fixed.FromDouble(-1.0), node.Valence);       // untouched
            Assert.Equal(Fixed.FromDouble(0.9), node.Confidence);     // untouched
        }

        [Fact]
        public void Fold_MissingId_ReturnsFalse()
        {
            var s = new MeaningsStore(8, MeaningsConfig.Default);
            Assert.False(s.Fold(new CategoryId(99), Bag((1, 1.0))));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Fold` does not exist.

- [ ] **Step 3: Implement `Fold`**

In `Assets/Sim/Memory/MeaningsStore.cs`, add inside the class (immediately before `Reinforce`):

```csharp
        /// <summary>
        /// The ungated StatFold: fold a recognized percept into the node's running statistics
        /// only — no valence/confidence change. Emitted on every recognition (consolidation step
        /// 0), so the predictions converge to the typical even though the encode gate stores only
        /// the exceptions. Returns false if the id is absent.
        /// </summary>
        public bool Fold(CategoryId id, AtomBag percept)
        {
            CategoryNode node;
            if (!TryGetNode(id, out node)) return false;
            node.Predicted.Fold(percept);
            return true;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MeaningsStore.cs Headless/Sim.MemoryTests/MeaningsStoreFoldTests.cs
git commit -m "feat(memory): MeaningsStore.Fold — ungated per-recognition StatFold"
```

---

### Task 3: `EncodeConfig` + `MemoryEncoder.Perceive` — the encode gate

**Files:**
- Create: `Assets/Sim/Memory/EncodeConfig.cs`, `Assets/Sim/Memory/MemoryEncoder.cs`
- Create: `Headless/Sim.MemoryTests/MemoryEncoderTests.cs`

**Interfaces:**
- Consumes: `Surprise` (T1), `MeaningsStore.Fold`/`Recognize`/`Prediction` (T2, A3), `AtomBag.Diff` (A1), `MemoryRecord`/`MemoryKey`/`MemoryFlags`/`CategoryId` (A2), `Fixed` (A1).
- Produces:
  - `EncodeConfig` — `readonly struct`; `int SurpriseThresholdRaw`, `int ArousalThresholdRaw`; ctor; `static readonly EncodeConfig Default`.
  - `EncodeResult` — `readonly struct`; `bool Written`; `MemoryRecord Record` (valid iff `Written`); `Surprise Surprise` (always present — attention spikes even when nothing is written); `CategoryId Category` (None = novel); ctor.
  - `MemoryEncoder` — `static class`; `static EncodeResult Perceive(MeaningsStore store, AtomBag signature, AtomBag percept, Fixed arousal, MemoryKey key, long tick, in EncodeConfig cfg)`. Steps: recognize `signature`; if None → `Surprise.Maximal`, deltaBag = `percept` (verbatim), no fold; else compute `prediction = node.Prediction(store.Config)`, `surprise = Surprise.Against(percept, prediction)`, `deltaBag = AtomBag.Diff(percept, prediction)`, then `store.Fold(cat, percept)` (ungated). Gate: write iff `surprise.Encode.Raw > cfg.SurpriseThresholdRaw` OR `arousal.Raw > cfg.ArousalThresholdRaw`. On write: `strength = Scale(max(surprise.Encode.Raw, arousal.Raw))`, `flags = surpriseFired ? Surprise : None`, build the record. Always return the computed `surprise` and `cat`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemoryEncoderTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryEncoderTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        // A store with one fox category whose prediction is {1..5 at 1.0}.
        static MeaningsStore FoxStore(out CategoryId fox)
        {
            var s = new MeaningsStore(16, MeaningsConfig.Default);
            fox = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), true);
            var features = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            for (int i = 0; i < 4; i++) s.Fold(fox, features);   // build the prediction
            return s;
        }

        static readonly AtomBag FoxSignature = Bag((1, 1.0));

        [Fact]
        public void CalmFamiliarFox_LeavesNoTrace()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.False(r.Written);                     // matches prediction, no arousal -> nothing stored
            Assert.Equal(Fixed.Zero, r.Surprise.Encode);
        }

        [Fact]
        public void NotchedEar_SpikesAttention_DoesNotFloodTheStore()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (99, 1.0));
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.Equal(Fixed.One, r.Surprise.Attention);   // attention spikes on the new feature
            Assert.False(r.Written);                         // but MEAN-encode is below threshold -> no flood
        }

        [Fact]
        public void NotchedEar_WhenWritten_StoresOnlyTheDelta_Weakly()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (99, 1.0));
            // Threshold low enough to write: proves delta-only storage + low strength (no flood).
            var cfg = new EncodeConfig(0, 154);
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, cfg);

            Assert.True(r.Written);
            Assert.Equal(new[] { 99 }, r.Record.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // ONLY the new atom
            Assert.True(r.Record.Strength < 64);             // weak (scale of ~1/6), not a full etch
            Assert.True(r.Record.IsSurprise);                // surprise-driven
        }

        [Fact]
        public void TotallyWrongFox_WritesStrongly()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 0.0), (2, 0.0), (3, 0.0), (4, 0.0), (5, 0.0));
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.True(r.Written);
            Assert.True(r.Record.Strength > 200);            // big divergence -> deep etch
            Assert.True(r.Record.IsSurprise);
            Assert.Equal(5, r.Record.DeltaBag.Count);        // all five contradicted features stored
        }

        [Fact]
        public void HighArousal_WritesEvenWhenUnsurprising()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));   // zero surprise
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.FromDouble(0.9), new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.True(r.Written);                          // arousal arm fired
            Assert.False(r.Record.IsSurprise);               // not surprise-driven -> no SURPRISE flag
            Assert.True(r.Record.Strength > 200);            // strength from arousal
        }

        [Fact]
        public void NovelPercept_StoresVerbatim_AtMaximalSurprise()
        {
            var s = FoxStore(out _);
            var novelSig = Bag((50, 1.0));                    // matches no prototype
            var percept = Bag((50, 1.0), (51, 0.7));
            var r = MemoryEncoder.Perceive(s, novelSig, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.True(r.Category.IsNone);                  // novelty
            Assert.Equal(Surprise.Maximal.Encode, r.Surprise.Encode);
            Assert.True(r.Written);
            Assert.True(r.Record.IsNovel);                   // categoryRef None
            Assert.Equal(new[] { 50, 51 }, r.Record.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // full percept verbatim
            Assert.Equal((byte)255, r.Record.Strength);
        }

        [Fact]
        public void Perceive_FoldsOnRecognition()
        {
            var s = FoxStore(out var fox);
            s.TryGetNode(fox, out var before);
            int typesBefore = before.Predicted.TypeCount;
            MemoryEncoder.Perceive(s, FoxSignature,
                Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (7, 1.0)),
                Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);
            s.TryGetNode(fox, out var after);
            Assert.True(after.Predicted.TypeCount > typesBefore);   // the new type 7 was folded in
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `EncodeConfig`/`MemoryEncoder` do not exist.

- [ ] **Step 3: Implement `EncodeConfig` and `MemoryEncoder`**

Create `Assets/Sim/Memory/EncodeConfig.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Encode-gate thresholds (raw Fixed units, /256). A percept is written when its MEAN-encode
    /// surprise exceeds SurpriseThresholdRaw OR arousal exceeds ArousalThresholdRaw. Both are p6
    /// tuning knobs, not architecture; the defaults are scaffolding (θ_s ~ 0.3, θ_a ~ 0.6) chosen
    /// so a single trivial new atom (the notched ear, MEAN ~ 1/6) does NOT cross the surprise arm.
    /// </summary>
    public readonly struct EncodeConfig
    {
        public readonly int SurpriseThresholdRaw;   // θ_s
        public readonly int ArousalThresholdRaw;    // θ_a

        public EncodeConfig(int surpriseThresholdRaw, int arousalThresholdRaw)
        {
            SurpriseThresholdRaw = surpriseThresholdRaw;
            ArousalThresholdRaw = arousalThresholdRaw;
        }

        public static readonly EncodeConfig Default = new EncodeConfig(77, 154);   // ~0.3, ~0.6
    }
}
```

Create `Assets/Sim/Memory/MemoryEncoder.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>The outcome of perceiving one percept: whether it was written, the would-be record,
    /// the surprise (attention spikes even when nothing is stored), and the recognized category
    /// (None = novel).</summary>
    public readonly struct EncodeResult
    {
        public readonly bool Written;
        public readonly MemoryRecord Record;   // valid only when Written
        public readonly Surprise Surprise;
        public readonly CategoryId Category;

        public EncodeResult(bool written, MemoryRecord record, Surprise surprise, CategoryId category)
        {
            Written = written;
            Record = record;
            Surprise = surprise;
            Category = category;
        }
    }

    /// <summary>
    /// The write path's front half (row 2 detect + row 5 encode, fused into a pure decision). Given
    /// a percept it recognizes a category, folds the percept's stats (ungated StatFold), measures
    /// surprise against the category's prediction, and — if surprise or arousal crosses the gate —
    /// builds a MemoryRecord whose deltaBag is only the divergence (the full percept when novel) at
    /// a strength set by max(encode-surprise, arousal). It does NOT insert the record (Phase B routes
    /// it to a store); keeping it a pure function makes the dynamics testable in isolation.
    /// </summary>
    public static class MemoryEncoder
    {
        public static EncodeResult Perceive(MeaningsStore store, AtomBag signature, AtomBag percept,
                                            Fixed arousal, MemoryKey key, long tick, in EncodeConfig cfg)
        {
            CategoryId cat = store.Recognize(signature);

            Surprise surprise;
            AtomBag deltaBag;
            if (cat.IsNone)
            {
                surprise = Surprise.Maximal;
                deltaBag = percept;                 // novelty -> store verbatim
            }
            else
            {
                CategoryNode node;
                store.TryGetNode(cat, out node);
                AtomBag prediction = node.Prediction(store.Config);
                surprise = Surprise.Against(percept, prediction);
                deltaBag = AtomBag.Diff(percept, prediction);
                store.Fold(cat, percept);           // ungated StatFold on every recognition
            }

            bool surpriseFired = surprise.Encode.Raw > cfg.SurpriseThresholdRaw;
            bool arousalFired = arousal.Raw > cfg.ArousalThresholdRaw;
            if (!surpriseFired && !arousalFired)
                return new EncodeResult(false, default(MemoryRecord), surprise, cat);

            int peak = surprise.Encode.Raw > arousal.Raw ? surprise.Encode.Raw : arousal.Raw;
            byte strength = Scale(peak);
            MemoryFlags flags = surpriseFired ? MemoryFlags.Surprise : MemoryFlags.None;
            MemoryRecord record = new MemoryRecord(key, cat, deltaBag, strength, tick, tick, flags);
            return new EncodeResult(true, record, surprise, cat);
        }

        /// <summary>Map a raw Fixed surprise/arousal (Q8, ~[0,1]) to a strength byte [0,255].</summary>
        static byte Scale(int raw)
        {
            if (raw < 0) raw = 0;
            else if (raw > 255) raw = 255;
            return (byte)raw;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (all memory test files green).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/EncodeConfig.cs Assets/Sim/Memory/MemoryEncoder.cs Headless/Sim.MemoryTests/MemoryEncoderTests.cs
git commit -m "feat(memory): MemoryEncoder.Perceive — surprise/arousal encode gate"
```

---

## Self-Review

**Spec coverage (roadmap A4 deliverables):**
- Surprise = per-atom prediction error; two operators (MAX attention, SUM/count encode) → Task 1 ✓
- Encode gate: `surprise > θ_s OR arousal > θ_a` → write with `deltaBag = Diff(percept, prediction)`, `strength = scale(max(surprise, arousal))` → Task 3 ✓
- Novelty (`categoryRef = null`) stores verbatim → Task 3 (`cat.IsNone` branch, deltaBag = percept, Maximal) ✓
- Tests: surprise gating (calm-fox/high-arousal), the notched-ear test (attention spikes, no flood), delta-only storage (`NotchedEar_WhenWritten_StoresOnlyTheDelta_Weakly`) → all covered ✓
- A3-review follow-up: fold-only `StatFold` entry point → Task 2, wired into `Perceive` ✓

**Type consistency:** `Surprise` (T1) consumed by `MemoryEncoder` (T3); `MeaningsStore.Fold` (T2) called by `Perceive`; `store.Config` (A3) supplies the prediction thresholds; `AtomBag.Diff` (A1) builds the deltaBag; `MemoryRecord`/`MemoryFlags`/`MemoryKey`/`CategoryId` (A2) build the record. `EncodeConfig` raw thresholds and `Fixed.Raw` units are consistent (Q8, /256). `Scale` clamps to the `byte Strength` of `MemoryRecord`.

**Determinism / float-freeness:** surprise is an integer merge-walk (MAX + sum/count); the gate compares raw ints; `Scale` clamps an int. No floats on any runtime path. `Surprise.Against` widens to `long` before the same-type subtraction (overflow-safe, matching the A3 fix).

**Scope honesty:** recall/reconsolidation, plan-surprise, the attention-consumer pipeline, and the actual store insertion are explicitly deferred (Scope boundary). `Perceive` returns the would-be record rather than mutating a `MemoryStore`, keeping the encoder a pure, testable decision — the Phase-B router does the insert.

**Note for the executor:** complete code, no open decisions; cheapest implementer tier or inline. The MEAN-vs-MAX split and scaffolding thresholds are made and documented; the notched-ear numbers are worked in the test comments.
