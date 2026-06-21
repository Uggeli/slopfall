# Memory Core A3 — MEANINGS Store (variance-gated categories) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the MEANINGS store — `CategoryNode`s whose predictions are variance-gated running statistics (the one genuinely new primitive), nearest-prototype recognition, the `StatFold`/reinforce update (valence + confidence, confirming-vs-contradicting), and the innate `MemorySeeds` table — as pure types, no live wiring.

**Architecture:** A `PredictedStats` container (per-`AtomTypeId` `RunningStat` from A1) whose `Prediction()` emits only the low-spread atom types (variance-gated intersection). A `CategoryNode` (prototype `AtomBag` + `PredictedStats` + valence + confidence + INNATE). A `MeaningsStore` that adds/recognizes nodes (integer L1 distance over atom bags vs a configurable threshold) and reinforces them. A static `MemorySeeds` installer. All integer/fixed-point — learning rates are bit-shifts, so no floats and no `Fixed` multiply.

**Tech Stack:** C# (.NET 10, C# 7.3-compatible), xUnit 2.9.3. Builds on A1 (`AtomBag`, `Fixed`, `AtomTypeId`, `RunningStat`) and A2 (`CategoryId`, `AgentMemoryStores.MeaningsCap`).

**Spec:** [`docs/what_is_a_memory.md`](../../what_is_a_memory.md) — "The MEANINGS store — categories as running statistics" and "The innate seed". Roadmap: [`docs/superpowers/specs/2026-06-21-memory-core-phaseA-roadmap.md`](../specs/2026-06-21-memory-core-phaseA-roadmap.md), section **A3**.

## Global Constraints

- **Namespace:** `DaggerfallWorkshop.Sim.Memory`. Test namespace `Sim.MemoryTests`.
- **No floats anywhere.** Learning rates are right-shifts (`delta >> shift`), thresholds are integer raw values, variance is the A1 `RunningStat.VarianceRaw()` (Q16 long). `double` may appear ONLY in test-only `Fixed.FromDouble` calls to author sample values.
- **Determinism:** every iteration that produces output (the `Prediction` bag, recognition scan) is in `AtomTypeId` / `CategoryId` order; recognition ties break to the lowest `CategoryId`. No `Math.Random`/`Date` — tests use fixed sample arrays for noise.
- **Open knobs are config, not architecture** (roadmap): the similarity metric (default integer L1 over atom bags), match threshold, variance threshold, min sample count, learn-rate shift, confidence gain, and the neutral band all live in `MeaningsConfig` with documented scaffolding defaults. They are tuned by p6, not pinned here.
- **C# 7.3** in core files (no switch expressions, records, target-typed `new`, ranges/indices, nullable-reference annotations).
- **Pure types only** — no registry/system/`SimWorld` wiring.
- **Test runner:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`, green at the end of every task.

## Scope boundary (what A3 does NOT build)

- **MINT / split / consolidation** (clustering novel records into new nodes, splitting on contradiction) → A5 (sleep job). A3 nodes are seeded or explicitly added; recognition + reinforce operate on existing nodes.
- **Prototype centroid drift** → deferred. The prototype is fixed at node creation in A3 (recognition needs a stable key); drift is a later refinement.
- **MEANINGS eviction policy** → deferred (categories are few; seeds are INNATE; minting pressure is A5). `MeaningsStore.AddNode` throws when full; `MemorySeeds.Install` asserts the cap exceeds the seed count.
- **The surprise operators / encode gate** (consuming recognition + prediction) → A4.
- **Wiring the real Daggerfall percept atom-types / species seed content** → Phase B. A3's `MemorySeeds` ships a small, clearly-scaffolding default seed set with named atom-type constants.

## File Structure

- `Assets/Sim/Memory/PredictedStats.cs` — per-AtomType running stats + variance-gated `Prediction` (Task 1)
- `Assets/Sim/Memory/MeaningsConfig.cs` — the tunable knobs (Task 2)
- `Assets/Sim/Memory/CategoryNode.cs` — the category node (Task 2)
- `Assets/Sim/Memory/MeaningsStore.cs` — node store: Add/Get/Recognize (Task 3), Reinforce (Task 4)
- `Assets/Sim/Memory/MemorySeeds.cs` — innate seed table + installer (Task 5)
- Tests: `PredictedStatsTests.cs`, `CategoryNodeTests.cs`, `MeaningsStoreRecognizeTests.cs`, `MeaningsStoreReinforceTests.cs`, `MemorySeedsTests.cs`

---

### Task 1: `PredictedStats` — per-AtomType running stats + variance-gated prediction

**Files:**
- Create: `Assets/Sim/Memory/PredictedStats.cs`
- Create: `Headless/Sim.MemoryTests/PredictedStatsTests.cs`

**Interfaces:**
- Consumes: `AtomBag`, `Atom`, `AtomTypeId`, `Fixed`, `RunningStat` (A1).
- Produces: `PredictedStats` — `sealed class`; `void Fold(AtomBag percept)` (integer count/sum/sumsq adds per atom type); `bool TryGetStat(AtomTypeId, out RunningStat)`; `AtomBag Prediction(long varianceThresholdRaw, int minCount)` — an `AtomBag` of `(type, stat.Mean())` for every type with `Count >= minCount` AND `VarianceRaw() <= varianceThresholdRaw`, in `AtomTypeId` order (the variance-gated intersection). `int TypeCount` (distinct atom types seen).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/PredictedStatsTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PredictedStatsTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Fold_AccumulatesPerType()
        {
            var ps = new PredictedStats();
            ps.Fold(Bag((1, 1.0), (2, 0.0)));
            ps.Fold(Bag((1, 1.0), (2, 1.0)));

            Assert.Equal(2, ps.TypeCount);
            Assert.True(ps.TryGetStat(new AtomTypeId(1), out var s1));
            Assert.Equal(2, s1.Count);
            Assert.Equal(Fixed.FromDouble(1.0), s1.Mean());
        }

        [Fact]
        public void Prediction_IncludesLowSpread_ExcludesHighSpread()
        {
            // Type 1 (the "fox"): always ~1.0 -> low spread -> predicted.
            // Type 2 (the "spot"): swings across [0,1] -> high spread -> NOT predicted.
            var ps = new PredictedStats();
            double[] foxes = { 1.00, 0.99, 1.00, 0.98, 1.00, 0.99 };
            double[] spots = { 0.10, 0.90, 0.20, 0.80, 0.05, 0.95 };
            for (int i = 0; i < foxes.Length; i++)
                ps.Fold(Bag((1, foxes[i]), (2, spots[i])));

            long varThreshold = 655;   // ~ std 0.1, Q16
            var pred = ps.Prediction(varThreshold, minCount: 3);

            Assert.Equal(new[] { 1 }, pred.Atoms.Select(a => a.Type.Value).ToArray());   // only the fox
            pred.TryGet(new AtomTypeId(1), out var mean);
            Assert.True(System.Math.Abs(mean.ToDouble() - 0.99) < 0.05);
        }

        [Fact]
        public void Prediction_RespectsMinCount()
        {
            var ps = new PredictedStats();
            ps.Fold(Bag((1, 1.0)));
            ps.Fold(Bag((1, 1.0)));   // only 2 samples
            Assert.Equal(0, ps.Prediction(655, minCount: 3).Count);   // below minCount -> not predicted
            ps.Fold(Bag((1, 1.0)));
            Assert.Equal(1, ps.Prediction(655, minCount: 3).Count);   // now 3 -> predicted
        }

        [Fact]
        public void Prediction_IsEmpty_ForFreshStats()
        {
            Assert.Same(AtomBag.Empty, new PredictedStats().Prediction(655, 1));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `PredictedStats` does not exist.

- [ ] **Step 3: Implement `PredictedStats`**

Create `Assets/Sim/Memory/PredictedStats.cs`:

```csharp
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Per-AtomType running statistics for one category — the mechanical form of "the semantic
    /// fact is the intersection of the episodes." Fold() adds a percept's atoms (integer
    /// count/sum/sumsq, commutative). Prediction() emits only the LOW-SPREAD atom types: a fox
    /// is always 'fox' and 'chase' (low variance -> predicted), but each fox sits in a different
    /// spot on a different day (high variance -> never predicted). The intersection is
    /// variance-gated running statistics, not set-intersection.
    /// </summary>
    public sealed class PredictedStats
    {
        readonly Dictionary<int, RunningStat> _byType = new Dictionary<int, RunningStat>();

        public int TypeCount => _byType.Count;

        /// <summary>Add a percept's atoms to the running stats. Order-independent (adds commute).</summary>
        public void Fold(AtomBag percept)
        {
            for (int i = 0; i < percept.Count; i++)
            {
                Atom a = percept[i];
                RunningStat stat;
                _byType.TryGetValue(a.Type.Value, out stat);   // default(RunningStat) if absent
                stat.Add(a.Value);
                _byType[a.Type.Value] = stat;
            }
        }

        public bool TryGetStat(AtomTypeId type, out RunningStat stat)
            => _byType.TryGetValue(type.Value, out stat);

        /// <summary>
        /// The variance-gated intersection: an AtomBag of (type, mean) for every atom type with
        /// at least minCount samples AND spread (VarianceRaw, Q16) at or below varianceThresholdRaw.
        /// Built in AtomTypeId order (deterministic).
        /// </summary>
        public AtomBag Prediction(long varianceThresholdRaw, int minCount)
        {
            // Collect qualifying type ids, then sort for deterministic output.
            List<int> types = new List<int>();
            foreach (KeyValuePair<int, RunningStat> kv in _byType)
            {
                RunningStat s = kv.Value;
                if (s.Count >= minCount && s.VarianceRaw() <= varianceThresholdRaw)
                    types.Add(kv.Key);
            }
            if (types.Count == 0) return AtomBag.Empty;
            types.Sort();

            List<Atom> atoms = new List<Atom>(types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                RunningStat s = _byType[types[i]];
                atoms.Add(new Atom(new AtomTypeId(types[i]), s.Mean()));
            }
            return AtomBag.Create(atoms);
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/PredictedStats.cs Headless/Sim.MemoryTests/PredictedStatsTests.cs
git commit -m "feat(memory): PredictedStats — variance-gated running statistics"
```

---

### Task 2: `MeaningsConfig` + `CategoryNode`

**Files:**
- Create: `Assets/Sim/Memory/MeaningsConfig.cs`, `Assets/Sim/Memory/CategoryNode.cs`
- Create: `Headless/Sim.MemoryTests/CategoryNodeTests.cs`

**Interfaces:**
- Produces:
  - `MeaningsConfig` — `readonly struct`; fields `long MatchThresholdRaw`, `long VarianceThresholdRaw`, `int MinPredictCount`, `int LearnShift`, `int ConfidenceGainRaw`, `int NeutralBandRaw`; ctor with all six; `static readonly MeaningsConfig Default`.
  - `CategoryNode` — `sealed class`; `CategoryId Id` (set at construction); `AtomBag Prototype` (recognition centroid; fixed in A3); `PredictedStats Predicted`; `Fixed Valence`; `Fixed Confidence`; `bool Innate`; ctor `CategoryNode(CategoryId id, AtomBag prototype, Fixed valence, Fixed confidence, bool innate)` (Predicted starts empty). `Prediction(in MeaningsConfig)` convenience that calls `Predicted.Prediction(cfg.VarianceThresholdRaw, cfg.MinPredictCount)`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/CategoryNodeTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class CategoryNodeTests
    {
        [Fact]
        public void Default_Config_HasScaffoldingKnobs()
        {
            var c = MeaningsConfig.Default;
            Assert.True(c.MatchThresholdRaw > 0);
            Assert.True(c.VarianceThresholdRaw > 0);
            Assert.True(c.MinPredictCount >= 1);
            Assert.True(c.LearnShift >= 1);
            Assert.True(c.ConfidenceGainRaw > 0);
            Assert.True(c.NeutralBandRaw >= 0);
        }

        [Fact]
        public void Node_HoldsIdentityValenceConfidence_AndEmptyPredictedStart()
        {
            var proto = AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) });
            var node = new CategoryNode(new CategoryId(5), proto, Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), innate: true);

            Assert.Equal(new CategoryId(5), node.Id);
            Assert.Same(proto, node.Prototype);
            Assert.Equal(Fixed.FromDouble(-1.0), node.Valence);
            Assert.Equal(Fixed.FromDouble(0.9), node.Confidence);
            Assert.True(node.Innate);
            Assert.Equal(0, node.Predicted.TypeCount);
        }

        [Fact]
        public void Node_Prediction_UsesConfigThresholds()
        {
            var node = new CategoryNode(new CategoryId(1), AtomBag.Empty, Fixed.Zero, Fixed.Zero, false);
            for (int i = 0; i < 4; i++)
                node.Predicted.Fold(AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) }));
            var pred = node.Prediction(MeaningsConfig.Default);
            Assert.Equal(1, pred.Count);   // constant low-spread type predicted under default knobs
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MeaningsConfig`/`CategoryNode` do not exist.

- [ ] **Step 3: Implement `MeaningsConfig` and `CategoryNode`**

Create `Assets/Sim/Memory/MeaningsConfig.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Tunable knobs for the MEANINGS dynamics. All are p6 dynamics-tuning parameters, not
    /// architecture (roadmap "open knobs"); the defaults are scaffolding order-of-magnitude
    /// values. Raw units: distances/variance/valence are Fixed raw (Q8) or VarianceRaw (Q16) as
    /// noted on each field.
    /// </summary>
    public readonly struct MeaningsConfig
    {
        /// <summary>Recognition: a signature matches a prototype when their L1 distance (sum of
        /// |Δ| over atom raw values, Q8) is &lt;= this. Larger = looser recognition.</summary>
        public readonly long MatchThresholdRaw;

        /// <summary>Prediction: an atom type predicts only when its VarianceRaw (Q16) is &lt;= this
        /// (the variance gate). Smaller = stricter "stable feature" requirement.</summary>
        public readonly long VarianceThresholdRaw;

        /// <summary>Prediction: minimum samples before an atom type can predict.</summary>
        public readonly int MinPredictCount;

        /// <summary>Valence running mean rate: valence moves by (outcome - valence) >> LearnShift
        /// each reinforce. Larger = slower learning.</summary>
        public readonly int LearnShift;

        /// <summary>Confidence step per reinforce (Fixed raw, /256): up on confirming, down on
        /// contradicting.</summary>
        public readonly int ConfidenceGainRaw;

        /// <summary>Valence band (Fixed raw) around zero treated as "still learning the sign", so
        /// a fresh node's first outcomes count as confirming regardless of sign.</summary>
        public readonly int NeutralBandRaw;

        public MeaningsConfig(long matchThresholdRaw, long varianceThresholdRaw, int minPredictCount,
                              int learnShift, int confidenceGainRaw, int neutralBandRaw)
        {
            MatchThresholdRaw = matchThresholdRaw;
            VarianceThresholdRaw = varianceThresholdRaw;
            MinPredictCount = minPredictCount;
            LearnShift = learnShift;
            ConfidenceGainRaw = confidenceGainRaw;
            NeutralBandRaw = neutralBandRaw;
        }

        // Scaffolding defaults (spec "order-of-magnitude"): match within ~0.5 total L1 deviation;
        // predict at std <= ~0.1 (variance 0.01 -> Q16 ~655); 3-sample floor; learn rate 1/16;
        // confidence step ~0.05 (13/256); neutral band ~0.06 (16/256).
        public static readonly MeaningsConfig Default =
            new MeaningsConfig(128, 655, 3, 4, 13, 16);
    }
}
```

Create `Assets/Sim/Memory/CategoryNode.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// A category in the MEANINGS store: a fact and an expectation in one node. Prototype is the
    /// recognition centroid (fixed in A3). Predicted is the variance-gated running statistics
    /// surprise is measured against. Valence is the appetitive(+)/aversive(-) charge; Confidence
    /// weights how much it colors a read and rises/falls with confirming/contradicting evidence.
    /// INNATE nodes are the species seed (decay/evict-immune).
    /// </summary>
    public sealed class CategoryNode
    {
        public readonly CategoryId Id;
        public AtomBag Prototype;
        public readonly PredictedStats Predicted;
        public Fixed Valence;
        public Fixed Confidence;
        public readonly bool Innate;

        public CategoryNode(CategoryId id, AtomBag prototype, Fixed valence, Fixed confidence, bool innate)
        {
            Id = id;
            Prototype = prototype;
            Predicted = new PredictedStats();
            Valence = valence;
            Confidence = confidence;
            Innate = innate;
        }

        /// <summary>The current prediction (variance-gated intersection) under the given knobs.</summary>
        public AtomBag Prediction(in MeaningsConfig cfg)
            => Predicted.Prediction(cfg.VarianceThresholdRaw, cfg.MinPredictCount);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MeaningsConfig.cs Assets/Sim/Memory/CategoryNode.cs Headless/Sim.MemoryTests/CategoryNodeTests.cs
git commit -m "feat(memory): CategoryNode + MeaningsConfig (tunable knobs)"
```

---

### Task 3: `MeaningsStore` — Add/Get nodes + nearest-prototype recognition

**Files:**
- Create: `Assets/Sim/Memory/MeaningsStore.cs`
- Create: `Headless/Sim.MemoryTests/MeaningsStoreRecognizeTests.cs`

**Interfaces:**
- Consumes: `CategoryNode`, `MeaningsConfig`, `CategoryId`, `AtomBag` (Tasks 1–2, A2).
- Produces: `MeaningsStore` — `sealed class`; ctor `MeaningsStore(int capacity, MeaningsConfig config)`; `int Count`, `int Capacity`; `MeaningsConfig Config`; `CategoryId AddNode(AtomBag prototype, Fixed valence, Fixed confidence, bool innate)` (assigns the next `CategoryId` 1,2,3…, appends in id order, throws `InvalidOperationException` if full); `bool TryGetNode(CategoryId, out CategoryNode)`; `CategoryNode this[int i]` (nodes in `CategoryId` order); `CategoryId Recognize(AtomBag signature)` — the lowest-distance prototype within `Config.MatchThresholdRaw`, ties to the lowest `CategoryId`; `CategoryId.None` when nothing matches (novelty → maximal surprise).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MeaningsStoreRecognizeTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MeaningsStoreRecognizeTests
    {
        static AtomBag Sig(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MeaningsStore Store() => new MeaningsStore(16, MeaningsConfig.Default);

        [Fact]
        public void AddNode_AssignsAscendingIds()
        {
            var s = Store();
            var a = s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            var b = s.AddNode(Sig((2, 1.0)), Fixed.Zero, Fixed.Zero, false);
            Assert.Equal(new CategoryId(1), a);
            Assert.Equal(new CategoryId(2), b);
            Assert.Equal(2, s.Count);
            Assert.True(s.TryGetNode(a, out var na));
            Assert.Equal(new CategoryId(1), na.Id);
        }

        [Fact]
        public void Recognize_ReturnsNearestPrototypeWithinThreshold()
        {
            var s = Store();
            var fox = s.AddNode(Sig((1, 1.0)), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), true);
            var rabbit = s.AddNode(Sig((2, 1.0)), Fixed.FromDouble(0.0), Fixed.FromDouble(0.9), true);

            // A near-fox signature (type 1 ~ 0.95) recognizes the fox.
            Assert.Equal(fox, s.Recognize(Sig((1, 0.95))));
            // A near-rabbit signature recognizes the rabbit.
            Assert.Equal(rabbit, s.Recognize(Sig((2, 0.98))));
        }

        [Fact]
        public void Recognize_NoMatch_IsNovel()
        {
            var s = Store();
            s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, true);
            // A signature far from every prototype (different type entirely) is novel.
            Assert.True(s.Recognize(Sig((9, 1.0))).IsNone);
        }

        [Fact]
        public void Recognize_TieBreaksToLowestId()
        {
            // Two identical prototypes -> equal distance -> the lower id wins (deterministic).
            var s = Store();
            var first = s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            Assert.Equal(first, s.Recognize(Sig((1, 1.0))));
        }

        [Fact]
        public void AddNode_Full_Throws()
        {
            var s = new MeaningsStore(1, MeaningsConfig.Default);
            s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            Assert.Throws<System.InvalidOperationException>(
                () => s.AddNode(Sig((2, 1.0)), Fixed.Zero, Fixed.Zero, false));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MeaningsStore` does not exist.

- [ ] **Step 3: Implement `MeaningsStore` (Add/Get/Recognize)**

Create `Assets/Sim/Memory/MeaningsStore.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// One agent's MEANINGS store: the bounded set of CategoryNodes that is BOTH the recognition
    /// substrate and the prediction source. Recognition is nearest-prototype (integer L1 distance
    /// over atom bags) within a configurable match threshold; below it, a percept is novel
    /// (maximal surprise). Nodes are held in ascending CategoryId order for deterministic scans.
    /// Eviction/minting are deferred (A5); AddNode throws when full.
    /// </summary>
    public sealed class MeaningsStore
    {
        readonly List<CategoryNode> _nodes;   // ascending CategoryId
        readonly int _capacity;
        int _nextId;

        public MeaningsStore(int capacity, MeaningsConfig config)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            Config = config;
            _nodes = new List<CategoryNode>(capacity);
            _nextId = 1;
        }

        public MeaningsConfig Config { get; }
        public int Count => _nodes.Count;
        public int Capacity => _capacity;
        public CategoryNode this[int i] => _nodes[i];

        /// <summary>Add a category with the next id. Throws when the store is full.</summary>
        public CategoryId AddNode(AtomBag prototype, Fixed valence, Fixed confidence, bool innate)
        {
            if (_nodes.Count >= _capacity)
                throw new InvalidOperationException("MeaningsStore is full (capacity " + _capacity + ")");
            CategoryId id = new CategoryId(_nextId++);
            _nodes.Add(new CategoryNode(id, prototype, valence, confidence, innate));   // ids ascend -> stays sorted
            return id;
        }

        public bool TryGetNode(CategoryId id, out CategoryNode node)
        {
            // Nodes are in ascending id order — binary search.
            int lo = 0, hi = _nodes.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = _nodes[mid].Id.Value.CompareTo(id.Value);
                if (cmp == 0) { node = _nodes[mid]; return true; }
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Recognition: the nearest prototype within MatchThresholdRaw. Scans in ascending id
        /// order and keeps the strictly-smaller distance, so equal-distance ties resolve to the
        /// lowest CategoryId. Returns CategoryId.None when nothing is within threshold (novelty).
        /// </summary>
        public CategoryId Recognize(AtomBag signature)
        {
            CategoryId best = CategoryId.None;
            long bestDist = long.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                long dist = SignatureDistance(signature, _nodes[i].Prototype);
                if (dist <= Config.MatchThresholdRaw && dist < bestDist)
                {
                    bestDist = dist;
                    best = _nodes[i].Id;
                }
            }
            return best;
        }

        /// <summary>Integer L1 (Manhattan) distance between two atom bags: sum of |Δ| over the
        /// union of atom types, a missing type counting as 0 on that side. Deterministic merge
        /// walk over the sorted bags.</summary>
        internal static long SignatureDistance(AtomBag a, AtomBag b)
        {
            long d = 0;
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                int cmp = a[i].Type.CompareTo(b[j].Type);
                if (cmp < 0) { d += Abs(a[i].Value.Raw); i++; }
                else if (cmp > 0) { d += Abs(b[j].Value.Raw); j++; }
                else { d += Abs(a[i].Value.Raw - b[j].Value.Raw); i++; j++; }
            }
            while (i < a.Count) { d += Abs(a[i].Value.Raw); i++; }
            while (j < b.Count) { d += Abs(b[j].Value.Raw); j++; }
            return d;
        }

        static long Abs(int x) { long v = x; return v < 0 ? -v : v; }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MeaningsStore.cs Headless/Sim.MemoryTests/MeaningsStoreRecognizeTests.cs
git commit -m "feat(memory): MeaningsStore — nodes + nearest-prototype recognition (L1)"
```

---

### Task 4: `MeaningsStore.Reinforce` — StatFold + valence/confidence update

**Files:**
- Modify: `Assets/Sim/Memory/MeaningsStore.cs` (add `Reinforce`)
- Create: `Headless/Sim.MemoryTests/MeaningsStoreReinforceTests.cs`

**Interfaces:**
- Produces: `bool Reinforce(CategoryId id, AtomBag percept, Fixed outcome)` — for the node with `id`: (1) `StatFold` the percept atoms into `Predicted`; (2) move `Valence` toward `outcome` by `(outcome - Valence) >> Config.LearnShift` (with a ±1 raw minimum step when the shift rounds to 0 but the gap is non-zero, so it converges); (3) adjust `Confidence` by `±Config.ConfidenceGainRaw` clamped to `[0, Fixed.One]` — UP when the outcome confirms (same sign as the pre-update valence, or the valence is within `NeutralBandRaw` of zero), DOWN when it contradicts. INNATE nodes update like any other (the seed learns too). Returns false if `id` is absent.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MeaningsStoreReinforceTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MeaningsStoreReinforceTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MeaningsStore Store() => new MeaningsStore(16, MeaningsConfig.Default);

        [Fact]
        public void Reinforce_FoldsPerceptIntoPredictedStats()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            for (int i = 0; i < 4; i++) s.Reinforce(id, Bag((1, 1.0), (2, 1.0)), Fixed.One);

            s.TryGetNode(id, out var node);
            var pred = node.Prediction(MeaningsConfig.Default);
            Assert.Equal(new[] { 1, 2 }, pred.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Reinforce_ConfirmingStream_RaisesConfidence_ConvergesValence()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            for (int i = 0; i < 40; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(1.0));

            s.TryGetNode(id, out var node);
            Assert.True(node.Confidence.ToDouble() > 0.5);                 // confidence climbed
            Assert.True(node.Valence.ToDouble() > 0.8);                    // valence converged toward +1
        }

        [Fact]
        public void Reinforce_ContradictingStream_LowersConfidence_WithoutCorrupting()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(1.0), Fixed.FromDouble(0.9), false);
            // First confirm to a high confidence, then contradict.
            for (int i = 0; i < 10; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(1.0));
            s.TryGetNode(id, out var node);
            double confAfterConfirm = node.Confidence.ToDouble();

            for (int i = 0; i < 10; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(-1.0));
            s.TryGetNode(id, out node);
            Assert.True(node.Confidence.ToDouble() < confAfterConfirm);    // weakened
            Assert.True(node.Confidence.ToDouble() >= 0.0);                // not corrupted (bounded)
            Assert.True(node.Valence.ToDouble() <= 1.0 && node.Valence.ToDouble() >= -1.0);
        }

        [Fact]
        public void Reinforce_ConfidenceClampedToOne()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(1.0), Fixed.FromDouble(1.0), false);
            for (int i = 0; i < 50; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(1.0));
            s.TryGetNode(id, out var node);
            Assert.True(node.Confidence.ToDouble() <= 1.0);
        }

        [Fact]
        public void Reinforce_MissingId_ReturnsFalse()
        {
            var s = Store();
            Assert.False(s.Reinforce(new CategoryId(99), Bag((1, 1.0)), Fixed.One));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Reinforce` does not exist.

- [ ] **Step 3: Implement `Reinforce`**

In `Assets/Sim/Memory/MeaningsStore.cs`, add inside the class (after `Recognize`):

```csharp
        /// <summary>
        /// StatFold + valence/confidence update for one category. Folds the percept into the
        /// node's running stats, nudges valence toward the outcome (with a ±1 raw floor so it
        /// converges), and raises confidence on a confirming outcome / lowers it on a
        /// contradicting one (clamped to [0,1]). INNATE nodes learn too. Returns false if absent.
        /// </summary>
        public bool Reinforce(CategoryId id, AtomBag percept, Fixed outcome)
        {
            CategoryNode node;
            if (!TryGetNode(id, out node)) return false;

            node.Predicted.Fold(percept);

            // Valence: integer running mean toward the outcome; ±1 raw minimum step on a non-zero
            // gap so quantization never stalls convergence short of the target.
            int gap = outcome.Raw - node.Valence.Raw;
            int step = gap >> Config.LearnShift;
            if (step == 0 && gap != 0) step = gap > 0 ? 1 : -1;
            int valenceBefore = node.Valence.Raw;
            node.Valence = new Fixed(valenceBefore + step);

            // Confidence: confirming (same sign as the prior valence, or prior valence within the
            // neutral band) raises; contradicting lowers. Clamp to [0, Fixed.One].
            bool neutral = valenceBefore <= Config.NeutralBandRaw && valenceBefore >= -Config.NeutralBandRaw;
            bool confirming = neutral || ((outcome.Raw >= 0) == (valenceBefore >= 0));
            int conf = node.Confidence.Raw + (confirming ? Config.ConfidenceGainRaw : -Config.ConfidenceGainRaw);
            if (conf < 0) conf = 0;
            else if (conf > Fixed.Scale) conf = Fixed.Scale;
            node.Confidence = new Fixed(conf);

            return true;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MeaningsStore.cs Headless/Sim.MemoryTests/MeaningsStoreReinforceTests.cs
git commit -m "feat(memory): MeaningsStore.Reinforce — StatFold + valence/confidence dynamics"
```

---

### Task 5: `MemorySeeds` — innate seed table + installer

**Files:**
- Create: `Assets/Sim/Memory/MemorySeeds.cs`
- Create: `Headless/Sim.MemoryTests/MemorySeedsTests.cs`

**Interfaces:**
- Consumes: `MeaningsStore`, `AtomBag`, `Fixed`.
- Produces: `MemorySeeds` — `static class`; nested `enum SeedAtom : int { Predator = 1, Food = 2, Water = 3, Conspecific = 4 }` (scaffolding atom-type ids); `static int Count` (number of default seed nodes); `static void Install(MeaningsStore store)` — adds the default innate nodes (predator aversive, food/water appetitive, conspecific neutral), each `INNATE`, asserting `store.Capacity >= Count` (throws `InvalidOperationException` otherwise — the cap-exceeds-seed assert deferred from A2).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemorySeedsTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemorySeedsTests
    {
        static AtomBag Sig(int type, double v)
            => AtomBag.Create(new[] { new Atom(new AtomTypeId(type), Fixed.FromDouble(v)) });

        [Fact]
        public void Install_AddsInnateSeedNodes()
        {
            var store = new MeaningsStore(MemorySeeds.Count + 4, MeaningsConfig.Default);
            MemorySeeds.Install(store);

            Assert.Equal(MemorySeeds.Count, store.Count);
            for (int i = 0; i < store.Count; i++)
                Assert.True(store[i].Innate);   // every seed is INNATE
        }

        [Fact]
        public void Install_SeedsAreRecognizable_AndCarryValence()
        {
            var store = new MeaningsStore(MemorySeeds.Count + 4, MeaningsConfig.Default);
            MemorySeeds.Install(store);

            // A predator-scented percept recognizes the predator seed, which is aversive.
            CategoryId pred = store.Recognize(Sig((int)MemorySeeds.SeedAtom.Predator, 1.0));
            Assert.False(pred.IsNone);
            store.TryGetNode(pred, out var node);
            Assert.True(node.Valence.ToDouble() < 0.0);   // predator = aversive

            CategoryId food = store.Recognize(Sig((int)MemorySeeds.SeedAtom.Food, 1.0));
            Assert.False(food.IsNone);
            store.TryGetNode(food, out var foodNode);
            Assert.True(foodNode.Valence.ToDouble() > 0.0);   // food = appetitive
        }

        [Fact]
        public void Install_TooSmallStore_Throws()
        {
            var store = new MeaningsStore(1, MeaningsConfig.Default);
            Assert.Throws<System.InvalidOperationException>(() => MemorySeeds.Install(store));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MemorySeeds` does not exist.

- [ ] **Step 3: Implement `MemorySeeds`**

Create `Assets/Sim/Memory/MemorySeeds.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The species seed: a small set of INNATE category nodes the dictionary ships with, so tick
    /// one works — threat recognition exists before any learning and surprise has predictions to
    /// violate. Learning accretes beside the seed in the same store. The atom-type ids and
    /// valences here are scaffolding; the real Daggerfall percept vocabulary and per-species
    /// seeds are Phase-B content.
    /// </summary>
    public static class MemorySeeds
    {
        public enum SeedAtom
        {
            Predator = 1,
            Food = 2,
            Water = 3,
            Conspecific = 4,
        }

        /// <summary>Number of default seed nodes Install adds.</summary>
        public const int Count = 4;

        /// <summary>Install the default innate nodes into a store. Asserts the cap exceeds the
        /// seed size (the deferred A2 invariant) — INNATE nodes must always fit.</summary>
        public static void Install(MeaningsStore store)
        {
            if (store.Capacity < Count)
                throw new InvalidOperationException(
                    "MeaningsStore capacity " + store.Capacity + " < seed count " + Count);

            store.AddNode(Proto(SeedAtom.Predator), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), true);
            store.AddNode(Proto(SeedAtom.Food), Fixed.FromDouble(1.0), Fixed.FromDouble(0.9), true);
            store.AddNode(Proto(SeedAtom.Water), Fixed.FromDouble(0.6), Fixed.FromDouble(0.9), true);
            store.AddNode(Proto(SeedAtom.Conspecific), Fixed.FromDouble(0.1), Fixed.FromDouble(0.9), true);
        }

        static AtomBag Proto(SeedAtom atom)
            => AtomBag.Create(new[] { new Atom(new AtomTypeId((int)atom), Fixed.One) });
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (all memory test files green).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemorySeeds.cs Headless/Sim.MemoryTests/MemorySeedsTests.cs
git commit -m "feat(memory): MemorySeeds — innate category seed + cap-exceeds-seed assert"
```

---

## Self-Review

**Spec coverage (roadmap A3 deliverables):**
- `CategoryNode { prototype, predicted per-AtomType RunningStat, valence, confidence, flags }` → Task 2 ✓
- Recognition = nearest-prototype above match threshold; below = novel → Task 3 (`Recognize`, L1 distance, `None` on no match) ✓
- `StatFold` = integer adds per atom type; only low-spread enter predicted (variance-gated intersection) → Task 1 (`PredictedStats.Prediction`) + Task 4 (`Reinforce` folds) ✓
- `MemorySeeds` static table (predator/food/water/conspecific, INNATE) → Task 5 ✓
- Tests: variance-gated convergence under noise (T1 low-vs-high spread), recognition/novelty threshold (T3), seed bootstrap (T5), contradicting stream weakens not corrupts (T4) → all covered ✓

**Type consistency:** `PredictedStats` (T1) is held by `CategoryNode` (T2), folded by `Reinforce` (T4), gated by `MeaningsConfig` (T2) thresholds throughout. `CategoryId` (A2) is the node id, assigned by `MeaningsStore.AddNode` and returned by `Recognize`. `Fixed.Scale` (A1) is the confidence clamp ceiling. `SignatureDistance` and `Recognize` both honor `Config.MatchThresholdRaw`.

**Determinism / float-freeness:** no floats on any runtime path — learning is `>> LearnShift` with a ±1 raw floor, variance is `RunningStat.VarianceRaw()`, distances/clamps are integer. `Prediction` sorts atom-type ids before building its bag; `Recognize` scans nodes in ascending id order with strict `<` so ties resolve to the lowest id. `Dictionary` in `PredictedStats` is used only for accumulation (commutative) and never for ordered output.

**Scope honesty:** MINT/split/consolidation, prototype drift, MEANINGS eviction, the surprise/encode path, and real seed content are explicitly deferred (Scope boundary) — not silently dropped. The match metric/threshold and all rates are configurable knobs (`MeaningsConfig`), per the roadmap's "open knobs are dynamics tuning, not architecture."

**Note for the executor:** Tasks are complete code; cheapest implementer tier or inline. The only judgment calls (L1 metric, scaffolding defaults) are made and documented; no open decisions remain in A3.
