# Memory Core A5 — Sleep Consolidation + Recall/Reconsolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the memory loop — recall as reconstruct (`predicted ⊕ deltaBag`, the confident-false-memory line), reconsolidation (recall refreshes), and the sleep consolidation pass (RE-DIFF → MINT → DECAY) that makes confirming episodes dissolve fastest and high-spread specifics erode while the fact stays clean.

**Architecture:** A pure `MemoryRecall` (reconstruct + a reconsolidating `Recall`), a `MemoryStore.Remove` (consolidation drops absorbed records), and a `Consolidation` orchestrator composing existing pieces: RE-DIFF via A1 `Diff`, MINT via A3 clustering + `PredictedStats`, DECAY via A2 `MemoryStore.Decay`. No live wiring — everything is a pure function over the stores.

**Tech Stack:** C# (.NET 10, C# 7.3-compatible), xUnit 2.9.3. Builds on A1 (`AtomBag.Merge/Diff`), A2 (`MemoryStore`, `MemoryRecord`), A3 (`MeaningsStore`, `PredictedStats`, `SignatureDistance`), A4 (the records `Perceive` produces).

**Spec:** [`docs/what_is_a_memory.md`](../../what_is_a_memory.md) — "The recall path — two doors" and "Consolidation — the sleep job, mechanically". Roadmap: [`docs/superpowers/specs/2026-06-21-memory-core-phaseA-roadmap.md`](../specs/2026-06-21-memory-core-phaseA-roadmap.md), section **A5**.

## Global Constraints

- **Namespace:** `DaggerfallWorkshop.Sim.Memory`. Test namespace `Sim.MemoryTests`.
- **No floats anywhere** in A5 (reconstruction is `AtomBag` ops, clustering is integer L1, decay is integer). `double` only in test-only `Fixed.FromDouble`.
- **Determinism:** consolidation iterates records in `MemoryKey` order (the store is key-sorted); clusters form greedily in record order; minted nodes get ascending `CategoryId`. No hash-order dependence, no RNG.
- **Recall reads the CURRENT node** — reconstruct against the live prediction, which is what produces confident false memory by construction. Novel records (`CategoryRef.None`) reconstruct to their verbatim deltaBag.
- **RE-DIFF drop rule:** a recognized, non-INNATE record whose deltaBag re-diffs to empty is dropped (its information migrated into the fact). INNATE and novel records are never dropped by RE-DIFF.
- **C# 7.3** in core files. **Pure types only** — no registry/system/`SimWorld` wiring.
- **Test runner:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`, green at the end of every task.

## Scope boundary (what A5 does NOT build)

- **SETTLE / category split** (step 4) → deferred. The spec marks split "the one consolidation piece left deliberately under-pinned — p6's job to inform"; none of A5's four named tests exercise it. Awake `Reinforce` (A3) already lowers confidence on contradiction; the sleep-side contradiction-tracking + split is the documented open knob. The pass runs steps 1–3 (RE-DIFF, MINT, DECAY).
- **Recall Door 1 (cue-keyed replay into top-K attention) + the rumination spiral** → Phase B (those are perception-pipeline dynamics needing the live attention buffer). A5 builds the reconstruct/reconsolidate primitive the door will call.
- **Prototype centroid drift on existing nodes** → still deferred (A3 boundary); MINT sets a fresh node's prototype once, from the cluster intersection.
- **Population-staggered sleep scheduling / the awake-vs-asleep writer split** → Phase B wiring. A5's `Consolidation.Pass` is the per-agent sleep job as a pure function; when/who runs it is Phase B.

## File Structure

- `Assets/Sim/Memory/MemoryRecall.cs` — reconstruct + reconsolidating recall (Task 1)
- `Assets/Sim/Memory/MemoryStore.cs` — add `Remove` (Task 2)
- `Assets/Sim/Memory/Consolidation.cs` — `ReDiff` (Task 2), `Mint` (Task 3), `Pass` (Task 4)
- `Assets/Sim/Memory/ConsolidationConfig.cs` — the pass knobs (Task 3)
- Tests: `MemoryRecallTests.cs`, `ConsolidationReDiffTests.cs`, `ConsolidationMintTests.cs`, `ConsolidationPassTests.cs`

---

### Task 1: `MemoryRecall` — reconstruct + reconsolidation

**Files:**
- Create: `Assets/Sim/Memory/MemoryRecall.cs`
- Create: `Headless/Sim.MemoryTests/MemoryRecallTests.cs`

**Interfaces:**
- Consumes: `MemoryRecord`, `MeaningsStore`, `MemoryStore`, `AtomBag.Merge` (A1/A2/A3).
- Produces: `MemoryRecall` — `static class`;
  - `static AtomBag Reconstruct(in MemoryRecord record, MeaningsStore meanings)` — novel (`IsNovel`) or dangling category-ref → returns `record.DeltaBag` (verbatim); else `AtomBag.Merge(node.Prediction(meanings.Config), record.DeltaBag)` (delta wins). Reads the current node.
  - `static bool Recall(MemoryStore store, MeaningsStore meanings, MemoryKey key, int refreshDelta, long atTick, out AtomBag reconstructed)` — reconstruct the record at `key` then `store.Refresh(key, refreshDelta, atTick)` (reconsolidation). Returns false (and `reconstructed = AtomBag.Empty`) if the key is absent.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemoryRecallTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryRecallTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        // A meanings store with one node whose prediction is the folded constant features.
        static MeaningsStore Meanings(out CategoryId id, params (int type, double v)[] features)
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            for (int i = 0; i < 4; i++) m.Fold(id, Bag(features));
            return m;
        }

        [Fact]
        public void Reconstruct_Recognized_MergesPredictionAndDelta()
        {
            var m = Meanings(out var id, (1, 1.0), (2, 1.0));
            var rec = new MemoryRecord(new MemoryKey(10), id, Bag((3, 0.5)), 100, 100, 100, MemoryFlags.None);

            var recon = MemoryRecall.Reconstruct(rec, m);
            Assert.Equal(new[] { 1, 2, 3 }, recon.Atoms.Select(a => a.Type.Value).ToArray());
            recon.TryGet(new AtomTypeId(1), out var v1);
            recon.TryGet(new AtomTypeId(3), out var v3);
            Assert.Equal(Fixed.FromDouble(1.0), v1);   // from the prediction
            Assert.Equal(Fixed.FromDouble(0.5), v3);   // from the delta
        }

        [Fact]
        public void Reconstruct_Novel_IsVerbatimDelta()
        {
            var m = Meanings(out _, (1, 1.0));
            var bag = Bag((5, 0.7));
            var rec = new MemoryRecord(new MemoryKey(10), CategoryId.None, bag, 100, 100, 100, MemoryFlags.None);
            Assert.Same(bag, MemoryRecall.Reconstruct(rec, m));   // nothing to merge against
        }

        [Fact]
        public void Reconstruct_DriftedCategory_ProducesConfidentFalseMemory()
        {
            var m = Meanings(out var id, (1, 1.0));   // prediction {1: 1.0}
            var rec = new MemoryRecord(new MemoryKey(10), id, Bag((2, 0.3)), 100, 100, 100, MemoryFlags.None);

            var before = MemoryRecall.Reconstruct(rec, m);
            before.TryGet(new AtomTypeId(1), out var v1Before);
            Assert.Equal(Fixed.FromDouble(1.0), v1Before);   // recalled "1" == 1.0 at encode time

            // Drift the category toward 0.9 (stays low-variance, so type 1 keeps predicting).
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 0.9)));

            var after = MemoryRecall.Reconstruct(rec, m);
            after.TryGet(new AtomTypeId(1), out var v1After);
            after.TryGet(new AtomTypeId(2), out var v2After);
            Assert.True(v1After.ToDouble() < 1.0 && v1After.ToDouble() > 0.8);   // confidently WRONG: the drifted prediction
            Assert.Equal(Fixed.FromDouble(0.3), v2After);                        // the stored specific is unchanged
        }

        [Fact]
        public void Recall_Reconsolidates_BumpsStrengthAndRecency()
        {
            var m = Meanings(out var id, (1, 1.0));
            var store = new MemoryStore(8);
            store.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((2, 0.3)), 100, 100, 100, MemoryFlags.None));

            Assert.True(MemoryRecall.Recall(store, m, new MemoryKey(10), 20, 500, out var recon));
            Assert.True(recon.Count > 0);
            store.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)120, r.Strength);     // reconsolidation bumped strength
            Assert.Equal(500, r.LastRefresh);
        }

        [Fact]
        public void Recall_MissingKey_ReturnsFalse()
        {
            var m = Meanings(out _, (1, 1.0));
            var store = new MemoryStore(8);
            Assert.False(MemoryRecall.Recall(store, m, new MemoryKey(99), 20, 500, out var recon));
            Assert.Same(AtomBag.Empty, recon);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MemoryRecall` does not exist.

- [ ] **Step 3: Implement `MemoryRecall`**

Create `Assets/Sim/Memory/MemoryRecall.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Recall is reconstruction, never replay: the full picture is rebuilt by re-applying the
    /// stored delta to the CURRENT category prediction. Because it reads the current node, a
    /// category that has drifted since encoding yields recalled "specifics" that are the new
    /// prediction wearing the old episode's costume — confident false memory is this one line, not
    /// a separate mechanism. Recall is also a write: it refreshes the record (reconsolidation).
    /// </summary>
    public static class MemoryRecall
    {
        /// <summary>predicted ⊕ deltaBag (delta wins). Novel or dangling-ref records have no
        /// prediction to merge against, so they reconstruct to their verbatim delta.</summary>
        public static AtomBag Reconstruct(in MemoryRecord record, MeaningsStore meanings)
        {
            if (record.IsNovel) return record.DeltaBag;
            CategoryNode node;
            if (!meanings.TryGetNode(record.CategoryRef, out node)) return record.DeltaBag;
            return AtomBag.Merge(node.Prediction(meanings.Config), record.DeltaBag);
        }

        /// <summary>Reconstruct the record at <paramref name="key"/> and refresh it (reconsolidation:
        /// recall renders the trace labile and re-stores it stronger). False if the key is absent.</summary>
        public static bool Recall(MemoryStore store, MeaningsStore meanings, MemoryKey key,
                                  int refreshDelta, long atTick, out AtomBag reconstructed)
        {
            MemoryRecord rec;
            if (!store.TryGet(key, out rec)) { reconstructed = AtomBag.Empty; return false; }
            reconstructed = Reconstruct(rec, meanings);
            store.Refresh(key, refreshDelta, atTick);
            return true;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemoryRecall.cs Headless/Sim.MemoryTests/MemoryRecallTests.cs
git commit -m "feat(memory): MemoryRecall — reconstruct (confident false memory) + reconsolidation"
```

---

### Task 2: `MemoryStore.Remove` + `Consolidation.ReDiff`

**Files:**
- Modify: `Assets/Sim/Memory/MemoryStore.cs` (add `Remove`)
- Create: `Assets/Sim/Memory/Consolidation.cs`
- Create: `Headless/Sim.MemoryTests/ConsolidationReDiffTests.cs`

**Interfaces:**
- Produces:
  - `bool MemoryStore.Remove(MemoryKey key)` — remove the record at `key` (preserving key order), return whether found.
  - `Consolidation` — `static class`; `static void ReDiff(MemoryStore store, MeaningsStore meanings)` — for each recognized record, `newDelta = AtomBag.Diff(record.DeltaBag, node.Prediction(meanings.Config))`; if `newDelta` is empty and the record is not INNATE, `Remove` it (absorbed into the fact); else re-`Encode` it with the shrunk delta. Novel records and records with a dangling category-ref are left untouched.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/ConsolidationReDiffTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationReDiffTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        // Meanings store whose single node predicts {1: 1.0}.
        static MeaningsStore FoxPredicts1(out CategoryId id)
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 1.0)));
            return m;
        }

        static MemoryRecord Rec(long key, CategoryId cat, AtomBag delta, MemoryFlags flags = MemoryFlags.None)
            => new MemoryRecord(new MemoryKey(key), cat, delta, 100, 100, 100, flags);

        [Fact]
        public void Remove_DropsRecord_PreservingOrder()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, CategoryId.None, AtomBag.Empty));
            s.Encode(Rec(20, CategoryId.None, AtomBag.Empty));
            s.Encode(Rec(30, CategoryId.None, AtomBag.Empty));
            Assert.True(s.Remove(new MemoryKey(20)));
            Assert.False(s.Remove(new MemoryKey(99)));
            Assert.Equal(new long[] { 10, 30 }, Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void ReDiff_AbsorbedRecord_IsDropped()
        {
            var m = FoxPredicts1(out var id);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, id, Bag((1, 1.0))));   // delta now exactly matches the prediction
            Consolidation.ReDiff(s, m);
            Assert.Equal(0, s.Count);               // information migrated into the fact -> gone
        }

        [Fact]
        public void ReDiff_RefusedRecord_KeepsItsDelta()
        {
            var m = FoxPredicts1(out var id);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, id, Bag((1, 0.0))));   // contradicts the prediction
            Consolidation.ReDiff(s, m);
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 1 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
            r.DeltaBag.TryGet(new AtomTypeId(1), out var v);
            Assert.Equal(Fixed.FromDouble(0.0), v);
        }

        [Fact]
        public void ReDiff_PartiallyAbsorbed_ShrinksDelta()
        {
            var m = FoxPredicts1(out var id);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, id, Bag((1, 1.0), (2, 0.0))));   // type 1 absorbed, type 2 not predicted
            Consolidation.ReDiff(s, m);
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 2 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // only the un-absorbed atom
        }

        [Fact]
        public void ReDiff_NovelRecord_IsUntouched()
        {
            var m = FoxPredicts1(out _);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, CategoryId.None, Bag((5, 0.5))));
            Consolidation.ReDiff(s, m);
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 5 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MemoryStore.Remove`/`Consolidation` do not exist.

- [ ] **Step 3: Implement `Remove` and `Consolidation.ReDiff`**

In `Assets/Sim/Memory/MemoryStore.cs`, add inside the class (after `TryGet`):

```csharp
        /// <summary>Remove the record with this key (preserving key order). Returns false if absent.</summary>
        public bool Remove(MemoryKey key)
        {
            int idx = IndexOf(key);
            if (idx < 0) return false;
            System.Array.Copy(_records, idx + 1, _records, idx, _count - idx - 1);
            _count--;
            _records[_count] = default(MemoryRecord);
            return true;
        }
```

Create `Assets/Sim/Memory/Consolidation.cs`:

```csharp
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The sleep job (row 7), over one agent's own stores. Per pass: RE-DIFF (drop what the fact
    /// now covers) → MINT (compress clustered novelty into new facts) → DECAY (erode by strength).
    /// SETTLE/split is the spec's deliberately under-pinned step and is deferred. Everything is a
    /// pure mutation of the passed stores, deterministic in key/creation order.
    /// </summary>
    public static partial class Consolidation
    {
        /// <summary>
        /// RE-DIFF: re-diff each recognized record's deltaBag against its category's UPDATED
        /// prediction, dropping atoms the fact now predicts. A record that re-diffs to empty has
        /// fully migrated into the fact and is removed (pure redundancy); INNATE and novel records
        /// are left alone.
        /// </summary>
        public static void ReDiff(MemoryStore store, MeaningsStore meanings)
        {
            List<MemoryRecord> snapshot = Snapshot(store);
            for (int i = 0; i < snapshot.Count; i++)
            {
                MemoryRecord rec = snapshot[i];
                if (rec.IsNovel) continue;
                CategoryNode node;
                if (!meanings.TryGetNode(rec.CategoryRef, out node)) continue;   // dangling ref — leave

                AtomBag newDelta = AtomBag.Diff(rec.DeltaBag, node.Prediction(meanings.Config));
                if (newDelta.Count == 0 && !rec.IsInnate)
                {
                    store.Remove(rec.Key);
                }
                else
                {
                    store.Encode(new MemoryRecord(rec.Key, rec.CategoryRef, newDelta,
                        rec.Strength, rec.WrittenAt, rec.LastRefresh, rec.Flags));
                }
            }
        }

        static List<MemoryRecord> Snapshot(MemoryStore store)
        {
            List<MemoryRecord> list = new List<MemoryRecord>(store.Count);
            for (int i = 0; i < store.Count; i++) list.Add(store[i]);
            return list;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemoryStore.cs Assets/Sim/Memory/Consolidation.cs Headless/Sim.MemoryTests/ConsolidationReDiffTests.cs
git commit -m "feat(memory): MemoryStore.Remove + Consolidation.ReDiff (drop absorbed records)"
```

---

### Task 3: `ConsolidationConfig` + `Consolidation.Mint`

**Files:**
- Create: `Assets/Sim/Memory/ConsolidationConfig.cs`
- Modify: `Assets/Sim/Memory/Consolidation.cs` (add `Mint`)
- Create: `Headless/Sim.MemoryTests/ConsolidationMintTests.cs`

**Interfaces:**
- Produces:
  - `ConsolidationConfig` — `readonly struct`; `int DecayNormalRate`, `int DecaySurpriseRate`, `long ClusterThresholdRaw`, `int MinClusterSupport`, `int MintConfidenceRaw`; ctor; `static readonly ConsolidationConfig Default`.
  - `static void Consolidation.Mint(MemoryStore store, MeaningsStore meanings, in ConsolidationConfig cfg)` — greedily cluster novel records by deltaBag L1 distance (`MeaningsStore.SignatureDistance`) within `ClusterThresholdRaw`; for each cluster of at least `MinClusterSupport` records, fold the members into a temp `PredictedStats`, take its variance-gated intersection as the prototype; if non-empty and the meanings store has room, mint an `INNATE`-free `CategoryNode` (valence 0, confidence `MintConfidenceRaw`), fold the members into it, and re-key each member record to it with `deltaBag = Diff(member.DeltaBag, prototype)`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/ConsolidationMintTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationMintTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MemoryRecord Novel(long key, AtomBag delta)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, delta, 150, 100, 100, MemoryFlags.Surprise);

        [Fact]
        public void Mint_ClusterOfSimilarNovels_MintsCategory_AndRekeys()
        {
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = new MemoryStore(16);
            // Three novel records sharing {1,2} at 1.0, each with one tiny unique atom.
            store.Encode(Novel(10, Bag((1, 1.0), (2, 1.0), (10, 0.05))));
            store.Encode(Novel(20, Bag((1, 1.0), (2, 1.0), (11, 0.05))));
            store.Encode(Novel(30, Bag((1, 1.0), (2, 1.0), (12, 0.05))));

            int meaningsBefore = meanings.Count;
            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            Assert.Equal(meaningsBefore + 1, meanings.Count);          // one fact minted
            // Each record is now recognized and re-diffed down to just its unique atom.
            for (long k = 10; k <= 30; k += 10)
            {
                store.TryGet(new MemoryKey(k), out var r);
                Assert.False(r.CategoryRef.IsNone);                   // no longer novel
                Assert.Equal(3, r.DeltaBag.Count == 1 ? 3 : 0);       // exactly one unique atom remains (see below)
            }
        }

        [Fact]
        public void Mint_Rekeyed_DeltaIsOnlyTheUniqueAtom()
        {
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = new MemoryStore(16);
            store.Encode(Novel(10, Bag((1, 1.0), (2, 1.0), (10, 0.05))));
            store.Encode(Novel(20, Bag((1, 1.0), (2, 1.0), (11, 0.05))));
            store.Encode(Novel(30, Bag((1, 1.0), (2, 1.0), (12, 0.05))));

            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            store.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 10 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // shared {1,2} migrated into the fact
        }

        [Fact]
        public void Mint_TooFewMembers_DoesNotMint()
        {
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = new MemoryStore(16);
            store.Encode(Novel(10, Bag((1, 1.0), (2, 1.0))));     // a single novel record
            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            Assert.Equal(0, meanings.Count);                     // below MinClusterSupport -> nothing minted
            store.TryGet(new MemoryKey(10), out var r);
            Assert.True(r.CategoryRef.IsNone);                   // still novel
        }
    }
}
```

Note: the first test's `DeltaBag.Count == 1 ? 3 : 0` idiom asserts each re-keyed record has exactly one atom left (the unique one); the second test pins which atom.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `ConsolidationConfig`/`Consolidation.Mint` do not exist.

- [ ] **Step 3: Implement `ConsolidationConfig` and `Mint`**

Create `Assets/Sim/Memory/ConsolidationConfig.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Knobs for the sleep consolidation pass. Decay rates feed MemoryStore.Decay (surprise resists);
    /// the cluster threshold + min support govern MINT; mint confidence seeds a freshly minted node.
    /// All are p6 tuning knobs, not architecture; defaults are scaffolding order-of-magnitude values.
    /// </summary>
    public readonly struct ConsolidationConfig
    {
        public readonly int DecayNormalRate;       // strength lost per pass by ordinary records
        public readonly int DecaySurpriseRate;     // (smaller) loss for SURPRISE-flagged records
        public readonly long ClusterThresholdRaw;  // MINT: max L1 distance to join a cluster (Q8 sum)
        public readonly int MinClusterSupport;     // MINT: members needed to mint a node
        public readonly int MintConfidenceRaw;     // confidence (Fixed raw) of a freshly minted node

        public ConsolidationConfig(int decayNormalRate, int decaySurpriseRate, long clusterThresholdRaw,
                                   int minClusterSupport, int mintConfidenceRaw)
        {
            DecayNormalRate = decayNormalRate;
            DecaySurpriseRate = decaySurpriseRate;
            ClusterThresholdRaw = clusterThresholdRaw;
            MinClusterSupport = minClusterSupport;
            MintConfidenceRaw = mintConfidenceRaw;
        }

        // Scaffolding: decay 20/pass (5 for surprising); cluster within ~0.25 L1; 3-member support;
        // minted nodes start at confidence ~0.25 (64/256).
        public static readonly ConsolidationConfig Default = new ConsolidationConfig(20, 5, 64, 3, 64);
    }
}
```

In `Assets/Sim/Memory/Consolidation.cs`, add inside the class (after `ReDiff`):

```csharp
        /// <summary>
        /// MINT: cluster the store's novel records by deltaBag proximity; a cluster with enough
        /// support mints a new category from the members' variance-gated intersection (their shared
        /// stable atoms) and re-keys each member to it, re-diffing away the now-predicted shared
        /// atoms. Novelty stops being verbatim. Clusters too small, or with no stable intersection,
        /// are left novel.
        /// </summary>
        public static void Mint(MemoryStore store, MeaningsStore meanings, in ConsolidationConfig cfg)
        {
            List<MemoryRecord> novel = new List<MemoryRecord>();
            for (int i = 0; i < store.Count; i++)
                if (store[i].IsNovel) novel.Add(store[i]);
            if (novel.Count == 0) return;

            // Greedy clustering: each record joins the first cluster whose seed is within threshold.
            List<List<MemoryRecord>> clusters = new List<List<MemoryRecord>>();
            for (int i = 0; i < novel.Count; i++)
            {
                bool placed = false;
                for (int c = 0; c < clusters.Count; c++)
                {
                    if (MeaningsStore.SignatureDistance(novel[i].DeltaBag, clusters[c][0].DeltaBag) <= cfg.ClusterThresholdRaw)
                    {
                        clusters[c].Add(novel[i]); placed = true; break;
                    }
                }
                if (!placed) { List<MemoryRecord> nc = new List<MemoryRecord>(); nc.Add(novel[i]); clusters.Add(nc); }
            }

            for (int c = 0; c < clusters.Count; c++)
            {
                List<MemoryRecord> members = clusters[c];
                if (members.Count < cfg.MinClusterSupport) continue;

                // Prototype = the cluster's variance-gated intersection (its shared stable atoms).
                PredictedStats stats = new PredictedStats();
                for (int k = 0; k < members.Count; k++) stats.Fold(members[k].DeltaBag);
                AtomBag prototype = stats.Prediction(meanings.Config.VarianceThresholdRaw, meanings.Config.MinPredictCount);
                if (prototype.Count == 0) continue;                 // no stable shared core — leave novel
                if (meanings.Count >= meanings.Capacity) continue;  // no room to mint

                CategoryId catId = meanings.AddNode(prototype, Fixed.Zero, new Fixed(cfg.MintConfidenceRaw), false);
                for (int k = 0; k < members.Count; k++) meanings.Fold(catId, members[k].DeltaBag);

                for (int k = 0; k < members.Count; k++)
                {
                    MemoryRecord m = members[k];
                    AtomBag newDelta = AtomBag.Diff(m.DeltaBag, prototype);
                    store.Encode(new MemoryRecord(m.Key, catId, newDelta, m.Strength, m.WrittenAt, m.LastRefresh, m.Flags));
                }
            }
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/ConsolidationConfig.cs Assets/Sim/Memory/Consolidation.cs Headless/Sim.MemoryTests/ConsolidationMintTests.cs
git commit -m "feat(memory): Consolidation.Mint — cluster novels into facts + re-key"
```

---

### Task 4: `Consolidation.Pass` + lifetime asymmetry / erosion

**Files:**
- Modify: `Assets/Sim/Memory/Consolidation.cs` (add `Pass`)
- Create: `Headless/Sim.MemoryTests/ConsolidationPassTests.cs`

**Interfaces:**
- Produces: `static void Consolidation.Pass(MemoryStore store, MeaningsStore meanings, in ConsolidationConfig cfg)` — runs the sleep steps in order: `ReDiff(store, meanings)`; `Mint(store, meanings, cfg)`; `store.Decay(cfg.DecayNormalRate, cfg.DecaySurpriseRate)`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/ConsolidationPassTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationPassTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MeaningsStore Predicts1(out CategoryId id)
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 1.0)));
            return m;
        }

        [Fact]
        public void ConfirmingRecord_DissolvesFasterThanSurprising()
        {
            var m = Predicts1(out var id);
            var s = new MemoryStore(8);
            // Confirming: delta matches the prediction -> re-diffs to empty -> dropped in the pass.
            s.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((1, 1.0)), 100, 0, 0, MemoryFlags.None));
            // Surprising: delta contradicts -> survives re-diff, SURPRISE resists decay.
            s.Encode(new MemoryRecord(new MemoryKey(20), id, Bag((1, 0.0)), 100, 0, 0, MemoryFlags.Surprise));

            Consolidation.Pass(s, m, ConsolidationConfig.Default);

            Assert.False(s.TryGet(new MemoryKey(10), out _));     // confirming dissolved
            Assert.True(s.TryGet(new MemoryKey(20), out var surviving));
            Assert.Equal((byte)95, surviving.Strength);           // 100 - surprise-rate 5
        }

        [Fact]
        public void HighSpreadAtom_NeverEntersTheFact_RidesInTheRecord()
        {
            // Type 1 is stable (always 1.0 -> predicted); type 99 (the "Tuesday") swings -> never predicted.
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            var id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            double[] tuesdays = { 0.1, 0.9, 0.2, 0.8 };
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 1.0), (99, tuesdays[i])));

            m.TryGetNode(id, out var node);
            var prediction = node.Prediction(MeaningsConfig.Default);
            Assert.True(prediction.Contains(new AtomTypeId(1)));    // the fact keeps the stable feature
            Assert.False(prediction.Contains(new AtomTypeId(99)));  // the Tuesday never enters the fact

            var s = new MemoryStore(8);
            s.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((99, 0.5)), 100, 0, 0, MemoryFlags.Surprise));
            Consolidation.Pass(s, m, ConsolidationConfig.Default);

            s.TryGet(new MemoryKey(10), out var r);                // re-diff can't absorb 99 (not predicted)
            Assert.Equal(new[] { 99 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void RepeatedPasses_KeepStoreBounded_AndDecayDropsWeakRecords()
        {
            var m = Predicts1(out var id);
            var s = new MemoryStore(8);
            // A weak refused record should decay to nothing over a few passes; an INNATE one persists.
            s.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((1, 0.0)), 30, 0, 0, MemoryFlags.None));
            s.Encode(new MemoryRecord(new MemoryKey(20), id, Bag((1, 0.0)), 50, 0, 0, MemoryFlags.Innate));

            for (int p = 0; p < 3; p++) Consolidation.Pass(s, m, ConsolidationConfig.Default);

            Assert.True(s.Count <= s.Capacity);
            Assert.False(s.TryGet(new MemoryKey(10), out _));      // 30 - 3*20 -> dropped
            Assert.True(s.TryGet(new MemoryKey(20), out var innate));
            Assert.Equal((byte)50, innate.Strength);              // INNATE immune to decay
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Consolidation.Pass` does not exist.

- [ ] **Step 3: Implement `Pass`**

In `Assets/Sim/Memory/Consolidation.cs`, add inside the class (after `Mint`):

```csharp
        /// <summary>
        /// One full consolidation pass over an agent's record store: RE-DIFF (shed what the facts
        /// now cover) → MINT (compress clustered novelty into new facts) → DECAY (erode by strength,
        /// surprising records resisting). The composition is why confirming episodes dissolve
        /// fastest (their bags re-diff empty, then decay) while a surprising memory stays vivid.
        /// </summary>
        public static void Pass(MemoryStore store, MeaningsStore meanings, in ConsolidationConfig cfg)
        {
            ReDiff(store, meanings);
            Mint(store, meanings, cfg);
            store.Decay(cfg.DecayNormalRate, cfg.DecaySurpriseRate);
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (all memory test files green).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/Consolidation.cs Headless/Sim.MemoryTests/ConsolidationPassTests.cs
git commit -m "feat(memory): Consolidation.Pass — RE-DIFF -> MINT -> DECAY sleep job"
```

---

## Self-Review

**Spec coverage (roadmap A5 deliverables):**
- Consolidation steps 1–3 (RE-DIFF, MINT, DECAY) → Tasks 2/3/4; step 4 (SETTLE/split) explicitly deferred per the spec's "under-pinned, p6 informs" (Scope boundary) ✓
- Recall = reconstruct (`predicted ⊕ deltaBag`) → Task 1 ✓; reconsolidation (recall → Refresh) → Task 1 ✓
- Tests: confirming-dissolves-fastest (T4), Oak/Tuesday high-spread erosion (T4), false-memory rate via drifted-category reconstruct (T1), bounded-store compression (T4) ✓

**Type consistency:** `MemoryRecall` consumes `MeaningsStore`/`MemoryStore`/`AtomBag.Merge`; `Consolidation` consumes `AtomBag.Diff` (RE-DIFF), `MeaningsStore.SignatureDistance` + `PredictedStats` + `AddNode`/`Fold` (MINT), `MemoryStore.Decay`/`Remove`/`Encode` (DECAY/RE-DIFF). `ConsolidationConfig` rates feed `Decay`; thresholds feed MINT. `Consolidation` is `static partial class` so ReDiff/Mint/Pass + the shared `Snapshot` helper live across the task edits in one file.

**Determinism / float-freeness:** RE-DIFF and MINT iterate records in key order (the snapshot copies the sorted store); clusters form greedily in record order; minted ids ascend. No floats — reconstruction is `AtomBag` merges, clustering is integer L1, decay/strength are integer/byte.

**Scope honesty:** SETTLE/split, recall Door 1 + rumination spiral, prototype drift, and sleep scheduling are explicitly deferred (Scope boundary) with the spec's own under-pinned/Phase-B justifications — nothing silently dropped. The four named A5 tests are all present and map to spec claims.

**Note for the executor:** complete code; cheapest implementer tier or inline. The one judgment call — RE-DIFF dropping recognized empty-delta records (the mechanical "empty-bag is pure redundancy, decay takes it first") — is made and documented; SETTLE is deferred, not stubbed.
