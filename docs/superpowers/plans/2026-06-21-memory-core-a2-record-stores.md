# Memory Core A2 — MemoryRecord + Bounded Record Stores + Eviction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the delta-record (`MemoryRecord`) and the bounded, key-sorted store that holds it — with `Encode`/`Refresh`/`Decay` and lowest-strength-first eviction (INNATE-immune) — as pure types with no live-engine wiring, plus the three record stores (PLACES/THINGS/EVENTS) wired with their caps.

**Architecture:** A `MemoryRecord` value type over the A1 `AtomBag`, and a concrete `MemoryStore` (a flat fixed-capacity `MemoryRecord[]` kept sorted by key, AoS) with binary-search lookup, sorted insert, decay-with-drop, and eviction. An `AgentMemoryStores` holder names the three stores with their caps. All integer/byte arithmetic — the determinism contract from A1 holds.

**Tech Stack:** C# (.NET 10, C# 7.3-compatible for Unity dual-compile), xUnit 2.9.3. Builds on A1's `Assets/Sim/Memory/` types.

**Spec:** [`docs/what_is_a_memory.md`](../../what_is_a_memory.md) — "The unit of storage — the delta record", and "Bounded storage — the eviction policy". Roadmap: [`docs/superpowers/specs/2026-06-21-memory-core-phaseA-roadmap.md`](../specs/2026-06-21-memory-core-phaseA-roadmap.md), section **A2**.

## Global Constraints

- **Namespace:** new core types in `DaggerfallWorkshop.Sim.Memory`. Test namespace `Sim.MemoryTests`.
- **No floats anywhere** in A2 (no `double`/`float` at all — strength is `byte`, ticks are `long`, rates are `int`).
- **Determinism:** stores iterate in `MemoryKey` order (sorted array, never hash order). Eviction order is fully specified and total (no ties left to chance): lowest `Strength`, then oldest `LastRefresh`, then lowest `Key`.
- **Id-struct convention** (match `Assets/Sim/Core/EntityId.cs`): `readonly struct : IEquatable<T>`, value field, `None`/sentinel where meaningful, `Equals`/`GetHashCode`/`ToString`, `== !=`.
- **C# 7.3:** core files under `Assets/Sim/Memory/` — no switch expressions, records, target-typed `new`, ranges/indices, or nullable-reference annotations. (`readonly struct`, `in` params, `out var`, `is T o`, expression-bodied members are fine.)
- **Pure types only** — no registry/system/`SimWorld` wiring (Phase B).
- **Test runner:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`, green at the end of every task.

## Scope boundary (what A2 does NOT build)

- **MEANINGS store + `CategoryNode`** → A3. MEANINGS holds a differently-shaped node (prototype + per-AtomType `RunningStat` + valence + confidence) with no `strength` byte, so its eviction policy differs from the record stores. `AgentMemoryStores` reserves the cap constant only.
- **`StatFold`** → A3. It mutates a `CategoryNode`'s running stats, which do not exist until A3.
- **The species `MemorySeeds` table and the cap-exceeds-seed-size assert** → A3/Phase-B (seed-load time).
- **SoA layout / snapshot serialization** → Phase B (A4 concern). A2 uses a flat AoS `MemoryRecord[]` ("flat slot array"); `MemoryRecord` is a struct so it stays cache-coherent.

## File Structure

- `Assets/Sim/Memory/MemoryKey.cs` — store-key token (sortable `long`) (Task 1)
- `Assets/Sim/Memory/CategoryId.cs` — nullable ref to a MEANINGS node (Task 1)
- `Assets/Sim/Memory/MemoryRecord.cs` — `MemoryFlags` enum + the delta-record struct (Task 1)
- `Assets/Sim/Memory/MemoryStore.cs` — bounded key-sorted store; Encode/TryGet (Task 2), Refresh/Decay (Task 3), eviction (Task 4)
- `Assets/Sim/Memory/AgentMemoryStores.cs` — the three record stores + caps (Task 5)
- Tests: `MemoryRecordTests.cs`, `MemoryStoreEncodeTests.cs`, `MemoryStoreRefreshDecayTests.cs`, `MemoryStoreEvictionTests.cs`, `AgentMemoryStoresTests.cs`

---

### Task 1: `MemoryKey`, `CategoryId`, `MemoryFlags`, `MemoryRecord`

**Files:**
- Create: `Assets/Sim/Memory/MemoryKey.cs`, `Assets/Sim/Memory/CategoryId.cs`, `Assets/Sim/Memory/MemoryRecord.cs`
- Create: `Headless/Sim.MemoryTests/MemoryRecordTests.cs`

**Interfaces:**
- Produces:
  - `MemoryKey` — `readonly struct : IEquatable<MemoryKey>, IComparable<MemoryKey>`; `long Value`; ctor `MemoryKey(long)`; `Equals`/`GetHashCode`/`CompareTo`/`ToString`/`== !=`. No `None` sentinel — `0` is a valid caller-packed key (e.g. position (0,0)).
  - `CategoryId` — `readonly struct : IEquatable<CategoryId>`; `int Value`; `static readonly CategoryId None = new CategoryId(0)`; `IsNone`; standard equality. `None` = novelty (record stored verbatim, diffs against nothing).
  - `[Flags] enum MemoryFlags : byte { None = 0, Innate = 1, Surprise = 2 }`.
  - `MemoryRecord` — `readonly struct`; fields `MemoryKey Key`, `CategoryId CategoryRef`, `AtomBag DeltaBag`, `byte Strength`, `long WrittenAt`, `long LastRefresh`, `MemoryFlags Flags`; ctor with all seven; props `IsInnate`, `IsSurprise`, `IsNovel` (== `CategoryRef.IsNone`); `MemoryRecord WithStrength(byte strength, long lastRefresh)` (keeps everything else, used by Refresh and Decay).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemoryRecordTests.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryRecordTests
    {
        [Fact]
        public void MemoryKey_OrdersAndEqualsByValue()
        {
            Assert.True(new MemoryKey(1).CompareTo(new MemoryKey(2)) < 0);
            Assert.True(new MemoryKey(5) == new MemoryKey(5));
            Assert.True(new MemoryKey(5) != new MemoryKey(6));
            Assert.Equal(new MemoryKey(5).GetHashCode(), new MemoryKey(5).GetHashCode());

            var list = new List<MemoryKey> { new MemoryKey(3), new MemoryKey(1), new MemoryKey(2) };
            list.Sort();
            Assert.Equal(new long[] { 1, 2, 3 }, list.ConvertAll(k => k.Value));
        }

        [Fact]
        public void CategoryId_None_IsZero_AndNovelty()
        {
            Assert.True(CategoryId.None.IsNone);
            Assert.Equal(0, CategoryId.None.Value);
            Assert.False(new CategoryId(7).IsNone);
            Assert.True(new CategoryId(7) == new CategoryId(7));
        }

        static MemoryRecord Rec(byte strength, MemoryFlags flags, CategoryId cat)
            => new MemoryRecord(new MemoryKey(10), cat, AtomBag.Empty, strength, 100, 100, flags);

        [Fact]
        public void Record_ExposesFlagsAndNovelty()
        {
            var innate = Rec(200, MemoryFlags.Innate, new CategoryId(3));
            Assert.True(innate.IsInnate);
            Assert.False(innate.IsSurprise);
            Assert.False(innate.IsNovel);

            var novelSurprise = Rec(50, MemoryFlags.Surprise, CategoryId.None);
            Assert.True(novelSurprise.IsSurprise);
            Assert.False(novelSurprise.IsInnate);
            Assert.True(novelSurprise.IsNovel);    // CategoryRef.None => stored verbatim
        }

        [Fact]
        public void WithStrength_ReplacesStrengthAndLastRefresh_KeepsRest()
        {
            var bag = AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) });
            var r = new MemoryRecord(new MemoryKey(10), new CategoryId(3), bag, 100, 100, 100, MemoryFlags.Surprise);
            var r2 = r.WithStrength(140, 250);

            Assert.Equal((byte)140, r2.Strength);
            Assert.Equal(250, r2.LastRefresh);
            Assert.Equal(100, r2.WrittenAt);          // unchanged
            Assert.Equal(new MemoryKey(10), r2.Key);  // unchanged
            Assert.Equal(new CategoryId(3), r2.CategoryRef);
            Assert.Same(bag, r2.DeltaBag);
            Assert.True(r2.IsSurprise);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MemoryKey`/`CategoryId`/`MemoryRecord` do not exist.

- [ ] **Step 3: Implement the three files**

Create `Assets/Sim/Memory/MemoryKey.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Store-specific key for a memory record, packed into a long by the caller (position for
    /// PLACES, signature for THINGS, an event tuple for EVENTS). Ordered and equatable so a
    /// store can stay sorted for deterministic iteration and binary-search lookup. No None
    /// sentinel — 0 is a valid key (e.g. tile position (0,0)).
    /// </summary>
    public readonly struct MemoryKey : IEquatable<MemoryKey>, IComparable<MemoryKey>
    {
        public readonly long Value;

        public MemoryKey(long value) { Value = value; }

        public bool Equals(MemoryKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is MemoryKey o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(MemoryKey other) => Value.CompareTo(other.Value);
        public override string ToString() => "MemoryKey(" + Value + ")";

        public static bool operator ==(MemoryKey a, MemoryKey b) => a.Value == b.Value;
        public static bool operator !=(MemoryKey a, MemoryKey b) => a.Value != b.Value;
    }
}
```

Create `Assets/Sim/Memory/CategoryId.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Reference to the MEANINGS category node a record diffs against. None = novelty: the
    /// percept matched no category, so the record stores its atoms verbatim (nothing to diff).
    /// </summary>
    public readonly struct CategoryId : IEquatable<CategoryId>
    {
        public static readonly CategoryId None = new CategoryId(0);

        public readonly int Value;

        public CategoryId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(CategoryId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CategoryId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "CategoryId.None" : "CategoryId(" + Value + ")";

        public static bool operator ==(CategoryId a, CategoryId b) => a.Value == b.Value;
        public static bool operator !=(CategoryId a, CategoryId b) => a.Value != b.Value;
    }
}
```

Create `Assets/Sim/Memory/MemoryRecord.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>INNATE = decay/evict-immune (the species seed); SURPRISE = decay-resistant.</summary>
    [Flags]
    public enum MemoryFlags : byte
    {
        None = 0,
        Innate = 1,
        Surprise = 2,
    }

    /// <summary>
    /// The unit of storage for PLACES/THINGS/EVENTS: a delta against a category's prediction.
    /// deltaBag holds ONLY the atoms that diverge from the prediction (or the full percept when
    /// novel — CategoryRef.None). strength is the one scalar doing write-depth, decay target,
    /// and eviction order. Immutable; mutation produces a new record (WithStrength).
    /// </summary>
    public readonly struct MemoryRecord
    {
        public readonly MemoryKey Key;
        public readonly CategoryId CategoryRef;   // None => novelty (stored verbatim)
        public readonly AtomBag DeltaBag;
        public readonly byte Strength;
        public readonly long WrittenAt;
        public readonly long LastRefresh;
        public readonly MemoryFlags Flags;

        public MemoryRecord(MemoryKey key, CategoryId categoryRef, AtomBag deltaBag,
                            byte strength, long writtenAt, long lastRefresh, MemoryFlags flags)
        {
            Key = key;
            CategoryRef = categoryRef;
            DeltaBag = deltaBag;
            Strength = strength;
            WrittenAt = writtenAt;
            LastRefresh = lastRefresh;
            Flags = flags;
        }

        public bool IsInnate => (Flags & MemoryFlags.Innate) != 0;
        public bool IsSurprise => (Flags & MemoryFlags.Surprise) != 0;
        public bool IsNovel => CategoryRef.IsNone;

        /// <summary>New record with a different strength and lastRefresh; everything else kept.
        /// Refresh passes the refresh tick; Decay passes the existing LastRefresh (decay is not
        /// a refresh).</summary>
        public MemoryRecord WithStrength(byte strength, long lastRefresh)
            => new MemoryRecord(Key, CategoryRef, DeltaBag, strength, WrittenAt, lastRefresh, Flags);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemoryKey.cs Assets/Sim/Memory/CategoryId.cs Assets/Sim/Memory/MemoryRecord.cs Headless/Sim.MemoryTests/MemoryRecordTests.cs
git commit -m "feat(memory): MemoryRecord delta-record + MemoryKey/CategoryId/MemoryFlags"
```

---

### Task 2: `MemoryStore` — sorted insert + `Encode` (not-full) + `TryGet`

**Files:**
- Create: `Assets/Sim/Memory/MemoryStore.cs`
- Create: `Headless/Sim.MemoryTests/MemoryStoreEncodeTests.cs`

**Interfaces:**
- Consumes: `MemoryRecord`, `MemoryKey` (Task 1).
- Produces: `MemoryStore` — `sealed class`; ctor `MemoryStore(int capacity)`; `int Count`, `int Capacity`; `MemoryRecord this[int i]` (records in ascending key order); `bool TryGet(MemoryKey, out MemoryRecord)` (binary search); `bool Encode(in MemoryRecord record)` — replaces in place if the key exists (absolute insert), else inserts keeping key-sorted order while `Count < Capacity`. (Eviction when full is added in Task 4 — for now, an `Encode` into a full store returns `false`.)

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemoryStoreEncodeTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreEncodeTests
    {
        static MemoryRecord Rec(long key, byte strength, long tick = 100, MemoryFlags flags = MemoryFlags.None)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Empty, strength, tick, tick, flags);

        [Fact]
        public void NewStore_IsEmpty_WithCapacity()
        {
            var s = new MemoryStore(4);
            Assert.Equal(0, s.Count);
            Assert.Equal(4, s.Capacity);
            Assert.False(s.TryGet(new MemoryKey(1), out _));
        }

        [Fact]
        public void Encode_KeepsRecordsKeySorted()
        {
            var s = new MemoryStore(8);
            Assert.True(s.Encode(Rec(30, 10)));
            Assert.True(s.Encode(Rec(10, 10)));
            Assert.True(s.Encode(Rec(20, 10)));

            Assert.Equal(3, s.Count);
            Assert.Equal(new long[] { 10, 20, 30 },
                Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void Encode_ExistingKey_ReplacesInPlace_NoGrowth()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 50));
            Assert.True(s.Encode(Rec(10, 200)));   // same key, absolute insert
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)200, r.Strength);
        }

        [Fact]
        public void TryGet_FindsPresent_MissesAbsent()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 1));
            s.Encode(Rec(20, 2));
            s.Encode(Rec(30, 3));

            Assert.True(s.TryGet(new MemoryKey(20), out var mid));
            Assert.Equal((byte)2, mid.Strength);
            Assert.False(s.TryGet(new MemoryKey(5), out _));
            Assert.False(s.TryGet(new MemoryKey(25), out _));
            Assert.False(s.TryGet(new MemoryKey(99), out _));
        }

        [Fact]
        public void Encode_IntoFullStore_NewKey_ReturnsFalse_ForNow()
        {
            var s = new MemoryStore(2);
            Assert.True(s.Encode(Rec(10, 10)));
            Assert.True(s.Encode(Rec(20, 10)));
            Assert.False(s.Encode(Rec(30, 10)));   // full; eviction lands in Task 4
            Assert.Equal(2, s.Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `MemoryStore` does not exist.

- [ ] **Step 3: Implement `MemoryStore` (insert + Encode + TryGet)**

Create `Assets/Sim/Memory/MemoryStore.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// A bounded, key-sorted store of MemoryRecords — one of PLACES/THINGS/EVENTS for one agent.
    /// Backed by a flat fixed-capacity array kept sorted ascending by MemoryKey (deterministic
    /// iteration + binary search). Encode/Refresh/Decay/eviction are plain methods here; they
    /// become CQRS deltas in Phase B. All arithmetic is integer/byte.
    /// </summary>
    public sealed class MemoryStore
    {
        readonly MemoryRecord[] _records;
        int _count;

        public MemoryStore(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _records = new MemoryRecord[capacity];
            _count = 0;
        }

        public int Count => _count;
        public int Capacity => _records.Length;
        public MemoryRecord this[int i] => _records[i];

        /// <summary>Index of the record with this key, or -1. Binary search on the sorted array.</summary>
        int IndexOf(MemoryKey key)
        {
            int lo = 0, hi = _count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = _records[mid].Key.CompareTo(key);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        public bool TryGet(MemoryKey key, out MemoryRecord record)
        {
            int idx = IndexOf(key);
            if (idx < 0) { record = default; return false; }
            record = _records[idx];
            return true;
        }

        /// <summary>
        /// Insert or replace a record. If the key already exists it is replaced in place
        /// (absolute insert — Encode wins). A new key inserts in sorted position while there is
        /// room. When full, eviction decides (Task 4); until then a full store rejects new keys.
        /// Returns true if the record is now stored.
        /// </summary>
        public bool Encode(in MemoryRecord record)
        {
            int idx = IndexOf(record.Key);
            if (idx >= 0) { _records[idx] = record; return true; }   // replace in place
            if (_count < _records.Length) { InsertSorted(record); return true; }
            return false;                                            // full — Task 4 adds eviction
        }

        /// <summary>Insert into the sorted array at the lower-bound position. Caller guarantees
        /// the key is absent and there is room.</summary>
        void InsertSorted(in MemoryRecord record)
        {
            int pos = LowerBound(record.Key);
            Array.Copy(_records, pos, _records, pos + 1, _count - pos);
            _records[pos] = record;
            _count++;
        }

        /// <summary>First index whose key is >= the given key (insertion point).</summary>
        int LowerBound(MemoryKey key)
        {
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_records[mid].Key.CompareTo(key) < 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemoryStore.cs Headless/Sim.MemoryTests/MemoryStoreEncodeTests.cs
git commit -m "feat(memory): MemoryStore sorted insert + Encode + binary-search TryGet"
```

---

### Task 3: `MemoryStore.Refresh` + `MemoryStore.Decay`

**Files:**
- Modify: `Assets/Sim/Memory/MemoryStore.cs` (add `Refresh`, `Decay`)
- Create: `Headless/Sim.MemoryTests/MemoryStoreRefreshDecayTests.cs`

**Interfaces:**
- Produces:
  - `bool Refresh(MemoryKey key, int deltaStrength, long atTick)` — if present, set `Strength = clamp(Strength + deltaStrength, 0, 255)` and `LastRefresh = atTick`; return whether found. (Reconsolidation: recall bumps strength + recency.)
  - `void Decay(int normalRate, int surpriseRate)` — for each non-INNATE record subtract `IsSurprise ? surpriseRate : normalRate` from strength (floored at 0); records reaching 0 are dropped (removed, key order preserved); INNATE records are untouched. `LastRefresh` is NOT changed (decay is not a refresh). Caller passes `surpriseRate <= normalRate` (SURPRISE decays slower).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemoryStoreRefreshDecayTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreRefreshDecayTests
    {
        static MemoryRecord Rec(long key, byte strength, MemoryFlags flags = MemoryFlags.None, long tick = 100)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Empty, strength, tick, tick, flags);

        [Fact]
        public void Refresh_BumpsStrength_AndLastRefresh()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100));
            Assert.True(s.Refresh(new MemoryKey(10), 40, 500));
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)140, r.Strength);
            Assert.Equal(500, r.LastRefresh);
            Assert.Equal(100, r.WrittenAt);
        }

        [Fact]
        public void Refresh_ClampsAt255_AndMissingKeyReturnsFalse()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 250));
            s.Refresh(new MemoryKey(10), 50, 500);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)255, r.Strength);
            Assert.False(s.Refresh(new MemoryKey(99), 10, 500));
        }

        [Fact]
        public void Decay_SubtractsNormalRate_FromOrdinaryRecords()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100));
            s.Decay(30, 10);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)70, r.Strength);
            Assert.Equal(100, r.LastRefresh);   // decay does not refresh
        }

        [Fact]
        public void Decay_SurpriseRecords_DecaySlower()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100, MemoryFlags.None));
            s.Encode(Rec(20, 100, MemoryFlags.Surprise));
            s.Decay(40, 10);

            s.TryGet(new MemoryKey(10), out var ordinary);
            s.TryGet(new MemoryKey(20), out var surprising);
            Assert.Equal((byte)60, ordinary.Strength);    // 100 - 40
            Assert.Equal((byte)90, surprising.Strength);   // 100 - 10
        }

        [Fact]
        public void Decay_DropsRecordsThatReachZero_PreservingKeyOrder()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 20));
            s.Encode(Rec(20, 100));
            s.Encode(Rec(30, 15));
            s.Decay(30, 5);   // 10 -> -10 dropped, 20 -> 70, 30 -> -15 dropped

            Assert.Equal(1, s.Count);
            Assert.Equal(new long[] { 20 }, Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void Decay_InnateRecords_AreImmune()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 5, MemoryFlags.Innate));
            s.Decay(255, 255);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(1, s.Count);
            Assert.Equal((byte)5, r.Strength);   // untouched
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Refresh`/`Decay` do not exist.

- [ ] **Step 3: Implement `Refresh` and `Decay`**

In `Assets/Sim/Memory/MemoryStore.cs`, add inside the class (after `Encode`/before `InsertSorted`):

```csharp
        /// <summary>
        /// Reconsolidation: bump a record's strength (clamped to [0,255]) and set its LastRefresh
        /// to the recall tick. Returns false if the key is absent.
        /// </summary>
        public bool Refresh(MemoryKey key, int deltaStrength, long atTick)
        {
            int idx = IndexOf(key);
            if (idx < 0) return false;
            int ns = _records[idx].Strength + deltaStrength;
            if (ns < 0) ns = 0;
            else if (ns > 255) ns = 255;
            _records[idx] = _records[idx].WithStrength((byte)ns, atTick);
            return true;
        }

        /// <summary>
        /// Sleep decay: subtract (IsSurprise ? surpriseRate : normalRate) from each non-INNATE
        /// record's strength, floored at 0. Records hitting 0 are dropped; key order is preserved
        /// by in-place compaction. INNATE records are immune. LastRefresh is unchanged.
        /// Caller passes surpriseRate &lt;= normalRate (surprising memories resist decay).
        /// </summary>
        public void Decay(int normalRate, int surpriseRate)
        {
            int w = 0;
            for (int r = 0; r < _count; r++)
            {
                MemoryRecord rec = _records[r];
                if (rec.IsInnate) { _records[w++] = rec; continue; }
                int applicable = rec.IsSurprise ? surpriseRate : normalRate;
                int ns = rec.Strength - applicable;
                if (ns <= 0) continue;                                  // dropped
                _records[w++] = rec.WithStrength((byte)ns, rec.LastRefresh);
            }
            for (int i = w; i < _count; i++) _records[i] = default;     // release dropped slots
            _count = w;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemoryStore.cs Headless/Sim.MemoryTests/MemoryStoreRefreshDecayTests.cs
git commit -m "feat(memory): MemoryStore Refresh (reconsolidation) + Decay (drop + INNATE-immune)"
```

---

### Task 4: `MemoryStore` eviction — write into a full store

**Files:**
- Modify: `Assets/Sim/Memory/MemoryStore.cs` (replace the `return false` full-store branch in `Encode` with eviction)
- Create: `Headless/Sim.MemoryTests/MemoryStoreEvictionTests.cs`

**Interfaces:**
- Changes `Encode`'s full-store behavior: a new key into a full store evicts the **weakest evictable (non-INNATE)** record and inserts the new one, **iff the new record is more keepable than that weakest**. Otherwise the write does not take (`Encode` returns false). Keepability order (least keepable first): lower `Strength`, then older `LastRefresh`, then lower `Key`. If every record is INNATE, the write is rejected.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/MemoryStoreEvictionTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreEvictionTests
    {
        static MemoryRecord Rec(long key, byte strength, long lastRefresh = 100, MemoryFlags flags = MemoryFlags.None)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Empty, strength, lastRefresh, lastRefresh, flags);

        [Fact]
        public void Encode_Full_StrongerNewRecord_EvictsWeakest()
        {
            var s = new MemoryStore(3);
            s.Encode(Rec(10, 80));
            s.Encode(Rec(20, 20));   // weakest by strength
            s.Encode(Rec(30, 60));

            Assert.True(s.Encode(Rec(40, 50)));   // 50 > weakest 20 -> evict key 20
            Assert.Equal(3, s.Count);
            Assert.False(s.TryGet(new MemoryKey(20), out _));
            Assert.True(s.TryGet(new MemoryKey(40), out _));
            Assert.Equal(new long[] { 10, 30, 40 },
                Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());   // still sorted
        }

        [Fact]
        public void Encode_Full_WeakerNewRecord_DoesNotTake()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 80));
            s.Encode(Rec(20, 60));
            Assert.False(s.Encode(Rec(30, 30)));   // 30 < weakest 60 -> rejected
            Assert.Equal(2, s.Count);
            Assert.False(s.TryGet(new MemoryKey(30), out _));
        }

        [Fact]
        public void Eviction_TieBreak_PrefersOlderLastRefresh()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 50, lastRefresh: 100));   // same strength, older -> least keepable
            s.Encode(Rec(20, 50, lastRefresh: 300));
            // new record strength 50, lastRefresh 900 (freshest): beats the strength-50/oldest record
            Assert.True(s.Encode(Rec(30, 50, lastRefresh: 900)));
            Assert.False(s.TryGet(new MemoryKey(10), out _));   // the oldest strength-50 evicted
            Assert.True(s.TryGet(new MemoryKey(20), out _));
        }

        [Fact]
        public void Eviction_SkipsInnate_EvictsWeakestNonInnate()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 5, flags: MemoryFlags.Innate));   // weakest by strength but INNATE -> immune
            s.Encode(Rec(20, 90));
            Assert.True(s.Encode(Rec(30, 95)));                // must evict the non-INNATE key 20
            Assert.True(s.TryGet(new MemoryKey(10), out _));   // innate survives
            Assert.False(s.TryGet(new MemoryKey(20), out _));
            Assert.True(s.TryGet(new MemoryKey(30), out _));
        }

        [Fact]
        public void Eviction_AllInnate_RejectsWrite()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 200, flags: MemoryFlags.Innate));
            s.Encode(Rec(20, 200, flags: MemoryFlags.Innate));
            Assert.False(s.Encode(Rec(30, 255)));   // nothing evictable
            Assert.Equal(2, s.Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — full store still returns false where the new tests expect eviction.

- [ ] **Step 3: Implement eviction**

In `Assets/Sim/Memory/MemoryStore.cs`, replace the full-store branch in `Encode`:

```csharp
            if (_count < _records.Length) { InsertSorted(record); return true; }
            return false;                                            // full — Task 4 adds eviction
```

with:

```csharp
            if (_count < _records.Length) { InsertSorted(record); return true; }
            return TryEvictAndInsert(record);                        // full — beat the weakest or bust
```

Then add these members inside the class (after `Decay`):

```csharp
        /// <summary>
        /// Full-store write: evict the weakest evictable (non-INNATE) record and insert the new
        /// one iff the new record is more keepable than that weakest. Otherwise the write does
        /// not take. Returns whether the record was stored.
        /// </summary>
        bool TryEvictAndInsert(in MemoryRecord record)
        {
            int weakest = -1;
            for (int i = 0; i < _count; i++)
            {
                if (_records[i].IsInnate) continue;                 // INNATE is evict-immune
                if (weakest < 0 || LessKeepable(_records[i], _records[weakest])) weakest = i;
            }
            if (weakest < 0) return false;                          // everything INNATE — rejected
            if (!LessKeepable(_records[weakest], record)) return false;  // new doesn't beat the weakest

            // Remove the weakest (preserve order), then insert the new record in sorted position.
            Array.Copy(_records, weakest + 1, _records, weakest, _count - weakest - 1);
            _count--;
            InsertSorted(record);
            return true;
        }

        /// <summary>True if a is less keepable than b: weaker strength, else older LastRefresh,
        /// else lower Key. A total order, so eviction is deterministic.</summary>
        static bool LessKeepable(in MemoryRecord a, in MemoryRecord b)
        {
            if (a.Strength != b.Strength) return a.Strength < b.Strength;
            if (a.LastRefresh != b.LastRefresh) return a.LastRefresh < b.LastRefresh;
            return a.Key.CompareTo(b.Key) < 0;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/MemoryStore.cs Headless/Sim.MemoryTests/MemoryStoreEvictionTests.cs
git commit -m "feat(memory): MemoryStore eviction (weakest-first, INNATE-immune, beat-or-bust)"
```

---

### Task 5: `AgentMemoryStores` — the three record stores + caps

**Files:**
- Create: `Assets/Sim/Memory/AgentMemoryStores.cs`
- Create: `Headless/Sim.MemoryTests/AgentMemoryStoresTests.cs`

**Interfaces:**
- Consumes: `MemoryStore` (Tasks 2–4).
- Produces: `AgentMemoryStores` — `sealed class`; `const int PlacesCap = 128, ThingsCap = 64, EventsCap = 128, MeaningsCap = 128` (MeaningsCap reserved; the MEANINGS store is added in A3); `MemoryStore Places`, `Things`, `Events`, each constructed at its cap.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/AgentMemoryStoresTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AgentMemoryStoresTests
    {
        [Fact]
        public void Stores_HaveSpecCapacities()
        {
            var m = new AgentMemoryStores();
            Assert.Equal(128, m.Places.Capacity);
            Assert.Equal(64, m.Things.Capacity);
            Assert.Equal(128, m.Events.Capacity);

            Assert.Equal(128, AgentMemoryStores.PlacesCap);
            Assert.Equal(64, AgentMemoryStores.ThingsCap);
            Assert.Equal(128, AgentMemoryStores.EventsCap);
            Assert.Equal(128, AgentMemoryStores.MeaningsCap);
        }

        [Fact]
        public void Stores_AreIndependent()
        {
            var m = new AgentMemoryStores();
            m.Places.Encode(new MemoryRecord(new MemoryKey(1), CategoryId.None, AtomBag.Empty, 100, 0, 0, MemoryFlags.None));
            Assert.Equal(1, m.Places.Count);
            Assert.Equal(0, m.Things.Count);
            Assert.Equal(0, m.Events.Count);
        }

        [Fact]
        public void InnateRecord_SurvivesAFullStoreUnderPressure_EndToEnd()
        {
            // Fill a small store; an INNATE record must never be evicted no matter how many
            // stronger writes pour in (the home-burrow guarantee).
            var store = new MemoryStore(4);
            store.Encode(new MemoryRecord(new MemoryKey(0), CategoryId.None, AtomBag.Empty, 10, 0, 0, MemoryFlags.Innate));
            for (long k = 1; k <= 50; k++)
                store.Encode(new MemoryRecord(new MemoryKey(k), CategoryId.None, AtomBag.Empty, 200, k, k, MemoryFlags.None));

            Assert.Equal(4, store.Count);
            Assert.True(store.TryGet(new MemoryKey(0), out var innate));   // still there
            Assert.True(innate.IsInnate);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `AgentMemoryStores` does not exist.

- [ ] **Step 3: Implement `AgentMemoryStores`**

Create `Assets/Sim/Memory/AgentMemoryStores.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// One agent's bounded record stores: PLACES (spatial), THINGS (entity dossiers), EVENTS
    /// (episodic + self). Caps are species config; these order-of-magnitude defaults come from
    /// the spec. The MEANINGS store (category nodes) has a different record shape and lands in
    /// A3 — only its cap is reserved here.
    /// </summary>
    public sealed class AgentMemoryStores
    {
        public const int PlacesCap = 128;
        public const int ThingsCap = 64;
        public const int EventsCap = 128;
        public const int MeaningsCap = 128;   // reserved; MEANINGS store added in A3

        public readonly MemoryStore Places = new MemoryStore(PlacesCap);
        public readonly MemoryStore Things = new MemoryStore(ThingsCap);
        public readonly MemoryStore Events = new MemoryStore(EventsCap);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (all memory test files green).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/AgentMemoryStores.cs Headless/Sim.MemoryTests/AgentMemoryStoresTests.cs
git commit -m "feat(memory): AgentMemoryStores — PLACES/THINGS/EVENTS with spec caps"
```

---

## Self-Review

**Spec coverage (roadmap A2 deliverables):**
- `MemoryRecord { key, categoryRef(nullable), deltaBag, strength byte, writtenAt, lastRefresh, flags INNATE|SURPRISE }` → Task 1 ✓
- Bounded, key-sorted stores, flat slot arrays → Task 2 (`MemoryStore`, AoS array) + Task 5 (the three stores with caps) ✓
- `Encode/Refresh/Decay` as plain methods → Tasks 2/3 ✓ (`StatFold` deferred to A3 with documented reason — it mutates CategoryNode stats that don't exist yet)
- Eviction = lowest-strength-first, tie oldest `lastRefresh` then key order; write-into-full must beat the weakest; INNATE evict-immune → Task 4 ✓
- Tests: encode/refresh/decay/evict, caps, write-into-full, INNATE immunity, key-order determinism → all covered ✓

**Type consistency:** `MemoryKey`/`CategoryId`/`MemoryFlags`/`MemoryRecord` (Task 1) consumed unchanged by `MemoryStore` (Tasks 2–4) and `AgentMemoryStores` (Task 5). `WithStrength(byte, long)` used by both `Refresh` (atTick) and `Decay` (existing LastRefresh). `LessKeepable` total order matches the documented eviction order in both the `Encode` contract and Task 4 tests. Caps (128/64/128/128) identical in spec, `AgentMemoryStores`, and tests.

**Determinism:** all store iteration is over the key-sorted array; eviction's `LessKeepable` is a total order (strength → lastRefresh → key), so the evicted record is unique. No floats, no hash iteration. Decay compaction preserves key order.

**Scope honesty:** MEANINGS store, `StatFold`, `MemorySeeds`, the cap-vs-seed assert, and SoA/serialization are explicitly deferred (Scope boundary section) — not silently dropped.

**Note for the executor:** pure transcription with complete code; cheapest implementer tier or inline execution. No open design decisions remain in A2.
