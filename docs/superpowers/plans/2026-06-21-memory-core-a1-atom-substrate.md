# Memory Core A1 — Atom Substrate + AtomBag Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the missing stored-atom-bag substrate the memory architecture assumes — a deterministic fixed-point scalar, an ordered atom key, the `Atom` pair, a sorted binary-searchable `AtomBag` with `Merge`/`Diff`, and integer-only `RunningStat` — as pure types with no live-engine wiring.

**Architecture:** Five pure value types under a new `Assets/Sim/Memory/` directory (namespace `DaggerfallWorkshop.Sim.Memory`), compiled into `Sim.Core`, tested in a new `Headless/Sim.MemoryTests` xUnit project. No registry, no system, no `SimWorld` wiring — that is Phase B. Every numeric is fixed-point or integer so the entire Memory subsystem is replay-exact and machine-portable (the spec's Determinism section).

**Tech Stack:** C# (.NET 10), xUnit 2.9.3, SDK-style csproj. Same test pattern as `Headless/Sim.Tests`.

**Spec:** [`docs/what_is_a_memory.md`](../../what_is_a_memory.md) — see "The unit of storage", "MEANINGS as running statistics", and "Determinism (A4/A5 compliance)". Roadmap: [`docs/superpowers/specs/2026-06-21-memory-core-phaseA-roadmap.md`](../specs/2026-06-21-memory-core-phaseA-roadmap.md), section **A1**.

## Global Constraints

- **Namespace:** all new core types in `DaggerfallWorkshop.Sim.Memory`. Test namespace `Sim.MemoryTests`.
- **No floats at runtime.** `double`/`float` may appear ONLY in `Fixed.FromDouble`/`Fixed.ToDouble` (authoring/test boundary helpers) and in test assertions. No other runtime path may use floating point. Integer/fixed-point arithmetic only.
- **Determinism:** atom bags iterate in `AtomTypeId` order (sorted arrays, never hash-iteration order). Running stats use integer accumulators only.
- **Id-struct convention** (match `Assets/Sim/Core/EntityId.cs`): `public readonly struct X : IEquatable<X>`, `int Value`, `static readonly X None = new X(0)`, `IsNone => Value == 0`, `Equals`/`GetHashCode`/`ToString`, `operator ==`/`!=`.
- **Fixed-point format:** 8 fractional bits (`Scale = 256`), int32 backing. The 8 fractional bits match the spec's "8.8" mean storage; the int32 backing gives integer-part headroom beyond a literal 16-bit 8.8.
- **Test runner:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`. All tests green at the end of every task.
- Pure types only — do NOT add anything to `SimWorld`'s systems/registries arrays, do NOT create a registry or system.

## File Structure

- `Assets/Sim/Memory/Fixed.cs` — fixed-point scalar (Task 1)
- `Assets/Sim/Memory/AtomTypeId.cs` — ordered atom key (Task 2)
- `Assets/Sim/Memory/Atom.cs` — (type, value) pair (Task 3)
- `Assets/Sim/Memory/AtomBag.cs` — sorted bag + `TryGet` (Task 4); `Merge`/`Diff` added (Task 5)
- `Assets/Sim/Memory/RunningStat.cs` — integer running stats (Task 6)
- `Headless/Sim.MemoryTests/Sim.MemoryTests.csproj` — test project (Task 1)
- `Headless/Sim.MemoryTests/FixedTests.cs`, `AtomTypeIdTests.cs`, `AtomTests.cs`, `AtomBagTests.cs`, `AtomBagMergeDiffTests.cs`, `RunningStatTests.cs`
- `Headless/Sim.Core/Sim.Core.csproj` — add the Memory glob line (Task 1)
- `Headless/Sim.slnx` — add the test project (Task 1)

---

### Task 1: `Fixed` fixed-point scalar + test-project scaffolding

**Files:**
- Create: `Assets/Sim/Memory/Fixed.cs`
- Create: `Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
- Create: `Headless/Sim.MemoryTests/FixedTests.cs`
- Modify: `Headless/Sim.Core/Sim.Core.csproj` (add Memory compile glob)
- Modify: `Headless/Sim.slnx` (register the test project)

**Interfaces:**
- Produces: `DaggerfallWorkshop.Sim.Memory.Fixed` — `readonly struct`; fields `int Raw`; `const int FractionalBits=8`, `const int Scale=256`; `static readonly Fixed Zero`, `Fixed One`; ctor `Fixed(int raw)`; `static Fixed FromInt(int)`, `static Fixed FromDouble(double)`, `double ToDouble()`; operators `+ - (unary -) == !=`; `Equals`/`GetHashCode`/`ToString`.

- [ ] **Step 1: Add the Memory compile glob to `Sim.Core.csproj`**

Open `Headless/Sim.Core/Sim.Core.csproj`. In the `<ItemGroup>` that lists the `<Compile Include="../../Assets/Sim/.../**/*.cs" />` lines, add this line directly after the `Assets/Sim/Core` line:

```xml
    <Compile Include="../../Assets/Sim/Memory/**/*.cs" />
```

- [ ] **Step 2: Create the test project `Sim.MemoryTests.csproj`**

Create `Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Sim.Core/Sim.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Register the test project in `Sim.slnx`**

Open `Headless/Sim.slnx`. Add this line in alphabetical position (after the `Sim.Host` line, before `Sim.Net`):

```xml
  <Project Path="Sim.MemoryTests/Sim.MemoryTests.csproj" />
```

- [ ] **Step 4: Write the failing tests**

Create `Headless/Sim.MemoryTests/FixedTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class FixedTests
    {
        [Fact]
        public void Scale_Is256_EightFractionalBits()
        {
            Assert.Equal(8, Fixed.FractionalBits);
            Assert.Equal(256, Fixed.Scale);
        }

        [Fact]
        public void Zero_And_One_HaveExpectedRaw()
        {
            Assert.Equal(0, Fixed.Zero.Raw);
            Assert.Equal(256, Fixed.One.Raw);
        }

        [Fact]
        public void FromInt_ScalesByWholeUnits()
        {
            Assert.Equal(256, Fixed.FromInt(1).Raw);
            Assert.Equal(768, Fixed.FromInt(3).Raw);
            Assert.Equal(-512, Fixed.FromInt(-2).Raw);
        }

        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(1.0, 256)]
        [InlineData(0.5, 128)]
        [InlineData(0.9, 230)]      // 0.9*256 = 230.4 -> 230 (round to nearest)
        [InlineData(-0.5, -128)]
        [InlineData(-0.9, -230)]    // ties-away-from-zero on the negative side
        public void FromDouble_RoundsToNearest(double v, int expectedRaw)
        {
            Assert.Equal(expectedRaw, Fixed.FromDouble(v).Raw);
        }

        [Fact]
        public void ToDouble_RoundTripsWithinResolution()
        {
            var f = Fixed.FromDouble(0.9);
            Assert.True(System.Math.Abs(f.ToDouble() - 0.9) <= 1.0 / 256);
        }

        [Fact]
        public void Addition_And_Subtraction_AreRawInteger()
        {
            var a = Fixed.FromDouble(0.5);
            var b = Fixed.FromDouble(0.25);
            Assert.Equal(Fixed.FromDouble(0.75), a + b);
            Assert.Equal(Fixed.FromDouble(0.25), a - b);
            Assert.Equal(Fixed.FromDouble(-0.5), -a);
        }

        [Fact]
        public void Equality_ByRaw()
        {
            Assert.True(Fixed.FromInt(2) == new Fixed(512));
            Assert.True(Fixed.FromInt(2) != Fixed.FromInt(3));
            Assert.Equal(Fixed.FromInt(2), new Fixed(512));
            Assert.Equal(Fixed.FromInt(2).GetHashCode(), new Fixed(512).GetHashCode());
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Fixed` does not exist (compile error).

- [ ] **Step 6: Implement `Fixed`**

Create `Assets/Sim/Memory/Fixed.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Deterministic fixed-point scalar: int32 backing, 8 fractional bits (Scale = 256).
    /// The 8 fractional bits match the memory spec's "8.8" mean storage; the int32 backing
    /// gives integer-part headroom. All runtime arithmetic is integer — no float ever enters
    /// the Memory subsystem (spec: Determinism). FromDouble/ToDouble exist only for authoring
    /// and tests at the boundary.
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>
    {
        public const int FractionalBits = 8;
        public const int Scale = 1 << FractionalBits;   // 256

        public static readonly Fixed Zero = new Fixed(0);
        public static readonly Fixed One = new Fixed(Scale);

        public readonly int Raw;

        public Fixed(int raw) { Raw = raw; }

        public static Fixed FromInt(int whole) => new Fixed(whole * Scale);

        /// <summary>Round-to-nearest, ties away from zero — identical on every machine.</summary>
        public static Fixed FromDouble(double v)
        {
            double scaled = v * Scale;
            int r = (int)(scaled >= 0 ? scaled + 0.5 : scaled - 0.5);
            return new Fixed(r);
        }

        public double ToDouble() => (double)Raw / Scale;

        public static Fixed operator +(Fixed a, Fixed b) => new Fixed(a.Raw + b.Raw);
        public static Fixed operator -(Fixed a, Fixed b) => new Fixed(a.Raw - b.Raw);
        public static Fixed operator -(Fixed a) => new Fixed(-a.Raw);

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fixed o && Equals(o);
        public override int GetHashCode() => Raw;
        public override string ToString() => ToDouble().ToString("0.###");

        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (all FixedTests green).

- [ ] **Step 8: Commit**

```bash
git add Assets/Sim/Memory/Fixed.cs Headless/Sim.MemoryTests Headless/Sim.Core/Sim.Core.csproj Headless/Sim.slnx
git commit -m "feat(memory): Fixed fixed-point scalar + Sim.MemoryTests scaffold"
```

---

### Task 2: `AtomTypeId` ordered key

**Files:**
- Create: `Assets/Sim/Memory/AtomTypeId.cs`
- Create: `Headless/Sim.MemoryTests/AtomTypeIdTests.cs`

**Interfaces:**
- Produces: `DaggerfallWorkshop.Sim.Memory.AtomTypeId` — `readonly struct : IEquatable<AtomTypeId>, IComparable<AtomTypeId>`; `int Value`; `static readonly AtomTypeId None`; `IsNone`; `CompareTo` orders by `Value`; `== != Equals GetHashCode ToString`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/AtomTypeIdTests.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomTypeIdTests
    {
        [Fact]
        public void None_IsZero()
        {
            Assert.True(AtomTypeId.None.IsNone);
            Assert.Equal(0, AtomTypeId.None.Value);
            Assert.False(new AtomTypeId(1).IsNone);
        }

        [Fact]
        public void Equality_ByValue()
        {
            Assert.True(new AtomTypeId(5) == new AtomTypeId(5));
            Assert.True(new AtomTypeId(5) != new AtomTypeId(6));
            Assert.Equal(new AtomTypeId(5).GetHashCode(), new AtomTypeId(5).GetHashCode());
        }

        [Fact]
        public void CompareTo_OrdersByValue()
        {
            Assert.True(new AtomTypeId(1).CompareTo(new AtomTypeId(2)) < 0);
            Assert.True(new AtomTypeId(2).CompareTo(new AtomTypeId(2)) == 0);
            Assert.True(new AtomTypeId(3).CompareTo(new AtomTypeId(2)) > 0);
        }

        [Fact]
        public void Sorts_Ascending_ByValue()
        {
            var list = new List<AtomTypeId> { new AtomTypeId(3), new AtomTypeId(1), new AtomTypeId(2) };
            list.Sort();
            Assert.Equal(new[] { 1, 2, 3 }, list.ConvertAll(a => a.Value));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `AtomTypeId` does not exist.

- [ ] **Step 3: Implement `AtomTypeId`**

Create `Assets/Sim/Memory/AtomTypeId.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Stable identifier for a kind of atom — the key in an AtomBag's type→value map.
    /// Ordered so bags can be kept sorted for deterministic, binary-searchable storage.
    /// </summary>
    public readonly struct AtomTypeId : IEquatable<AtomTypeId>, IComparable<AtomTypeId>
    {
        public static readonly AtomTypeId None = new AtomTypeId(0);

        public readonly int Value;

        public AtomTypeId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(AtomTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AtomTypeId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(AtomTypeId other) => Value.CompareTo(other.Value);
        public override string ToString() => IsNone ? "AtomTypeId.None" : "AtomTypeId(" + Value + ")";

        public static bool operator ==(AtomTypeId a, AtomTypeId b) => a.Value == b.Value;
        public static bool operator !=(AtomTypeId a, AtomTypeId b) => a.Value != b.Value;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/AtomTypeId.cs Headless/Sim.MemoryTests/AtomTypeIdTests.cs
git commit -m "feat(memory): AtomTypeId ordered atom key"
```

---

### Task 3: `Atom` (type, value) pair

**Files:**
- Create: `Assets/Sim/Memory/Atom.cs`
- Create: `Headless/Sim.MemoryTests/AtomTests.cs`

**Interfaces:**
- Consumes: `AtomTypeId` (Task 2), `Fixed` (Task 1).
- Produces: `DaggerfallWorkshop.Sim.Memory.Atom` — `readonly struct : IEquatable<Atom>`; fields `AtomTypeId Type`, `Fixed Value`; ctor `Atom(AtomTypeId, Fixed)`; `Equals`/`GetHashCode`/`ToString`.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/AtomTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomTests
    {
        [Fact]
        public void StoresTypeAndValue()
        {
            var a = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.5));
            Assert.Equal(new AtomTypeId(7), a.Type);
            Assert.Equal(Fixed.FromDouble(0.5), a.Value);
        }

        [Fact]
        public void Equality_ByTypeAndValue()
        {
            var a = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.5));
            var same = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.5));
            var diffType = new Atom(new AtomTypeId(8), Fixed.FromDouble(0.5));
            var diffValue = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.6));

            Assert.Equal(same, a);
            Assert.Equal(same.GetHashCode(), a.GetHashCode());
            Assert.NotEqual(diffType, a);
            Assert.NotEqual(diffValue, a);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Atom` does not exist.

- [ ] **Step 3: Implement `Atom`**

Create `Assets/Sim/Memory/Atom.cs`:

```csharp
using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// One (type, value) pair — the unit stored in an AtomBag. Value is fixed-point so the
    /// whole Memory subsystem stays float-free and replay-exact.
    /// </summary>
    public readonly struct Atom : IEquatable<Atom>
    {
        public readonly AtomTypeId Type;
        public readonly Fixed Value;

        public Atom(AtomTypeId type, Fixed value) { Type = type; Value = value; }

        public bool Equals(Atom other) => Type == other.Type && Value == other.Value;
        public override bool Equals(object obj) => obj is Atom o && Equals(o);
        public override int GetHashCode() => unchecked((Type.GetHashCode() * 397) ^ Value.GetHashCode());
        public override string ToString() => Type + "=" + Value;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/Atom.cs Headless/Sim.MemoryTests/AtomTests.cs
git commit -m "feat(memory): Atom (type, value) pair"
```

---

### Task 4: `AtomBag` — sorted bag + binary-search `TryGet`

**Files:**
- Create: `Assets/Sim/Memory/AtomBag.cs`
- Create: `Headless/Sim.MemoryTests/AtomBagTests.cs`

**Interfaces:**
- Consumes: `Atom` (Task 3), `AtomTypeId` (Task 2), `Fixed` (Task 1).
- Produces: `DaggerfallWorkshop.Sim.Memory.AtomBag` — `sealed class`; `static readonly AtomBag Empty`; `static AtomBag Create(IEnumerable<Atom>)` (sorts; throws `ArgumentException` on duplicate type); `int Count`; `Atom this[int]`; `IReadOnlyList<Atom> Atoms`; `bool TryGet(AtomTypeId, out Fixed)`; `bool Contains(AtomTypeId)`. **Note:** `Create` and `TryGet` are added here; `Merge`/`Diff` come in Task 5 — leave room (do not seal the class to additions).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/AtomBagTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomBagTests
    {
        static Atom A(int type, double v) => new Atom(new AtomTypeId(type), Fixed.FromDouble(v));

        [Fact]
        public void Empty_HasZeroCount()
        {
            Assert.Equal(0, AtomBag.Empty.Count);
            Assert.False(AtomBag.Empty.TryGet(new AtomTypeId(1), out _));
        }

        [Fact]
        public void Create_SortsByType()
        {
            var bag = AtomBag.Create(new[] { A(3, 0.3), A(1, 0.1), A(2, 0.2) });
            Assert.Equal(new[] { 1, 2, 3 }, bag.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Create_EmptyInput_ReturnsEmptySingleton()
        {
            Assert.Same(AtomBag.Empty, AtomBag.Create(Array.Empty<Atom>()));
        }

        [Fact]
        public void Create_DuplicateType_Throws()
        {
            Assert.Throws<ArgumentException>(() => AtomBag.Create(new[] { A(1, 0.1), A(1, 0.2) }));
        }

        [Fact]
        public void TryGet_FindsPresent_AndMissesAbsent()
        {
            var bag = AtomBag.Create(new[] { A(1, 0.1), A(5, 0.5), A(9, 0.9) });

            Assert.True(bag.TryGet(new AtomTypeId(5), out var mid));
            Assert.Equal(Fixed.FromDouble(0.5), mid);
            Assert.True(bag.TryGet(new AtomTypeId(1), out _));   // first
            Assert.True(bag.TryGet(new AtomTypeId(9), out _));   // last

            Assert.False(bag.TryGet(new AtomTypeId(0), out _));  // below min
            Assert.False(bag.TryGet(new AtomTypeId(3), out _));  // between
            Assert.False(bag.TryGet(new AtomTypeId(99), out _)); // above max
        }

        [Fact]
        public void Contains_MatchesTryGet()
        {
            var bag = AtomBag.Create(new[] { A(2, 0.2) });
            Assert.True(bag.Contains(new AtomTypeId(2)));
            Assert.False(bag.Contains(new AtomTypeId(1)));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `AtomBag` does not exist.

- [ ] **Step 3: Implement `AtomBag` (Create + TryGet)**

Create `Assets/Sim/Memory/AtomBag.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Immutable map from AtomTypeId to Fixed value, stored as an array sorted ascending by
    /// AtomTypeId for deterministic iteration and binary-search lookup. The shared substrate
    /// behind percepts, predictions, and memory delta-bags.
    /// </summary>
    public sealed partial class AtomBag
    {
        public static readonly AtomBag Empty = new AtomBag(Array.Empty<Atom>());

        readonly Atom[] _atoms;   // sorted ascending by Type.Value, unique types

        AtomBag(Atom[] sortedUnique) { _atoms = sortedUnique; }

        public int Count => _atoms.Length;
        public Atom this[int i] => _atoms[i];
        public IReadOnlyList<Atom> Atoms => _atoms;

        /// <summary>Build a bag from atoms in any order. Throws on a duplicate AtomTypeId
        /// (a bag is a map: each type appears at most once).</summary>
        public static AtomBag Create(IEnumerable<Atom> atoms)
        {
            var list = new List<Atom>(atoms);
            list.Sort((a, b) => a.Type.CompareTo(b.Type));
            for (int i = 1; i < list.Count; i++)
                if (list[i].Type == list[i - 1].Type)
                    throw new ArgumentException("Duplicate AtomTypeId in bag: " + list[i].Type);
            return list.Count == 0 ? Empty : new AtomBag(list.ToArray());
        }

        /// <summary>Binary-search lookup by type. O(log n).</summary>
        public bool TryGet(AtomTypeId type, out Fixed value)
        {
            int lo = 0, hi = _atoms.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = _atoms[mid].Type.CompareTo(type);
                if (cmp == 0) { value = _atoms[mid].Value; return true; }
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            value = Fixed.Zero;
            return false;
        }

        public bool Contains(AtomTypeId type) => TryGet(type, out _);
    }
}
```

Note: declared `partial` so Task 5 can add `Merge`/`Diff` in the same file or a sibling; keep both in this file for cohesion.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/AtomBag.cs Headless/Sim.MemoryTests/AtomBagTests.cs
git commit -m "feat(memory): AtomBag sorted bag + binary-search TryGet"
```

---

### Task 5: `AtomBag.Merge` and `AtomBag.Diff`

**Files:**
- Modify: `Assets/Sim/Memory/AtomBag.cs` (add `Merge`, `Diff`)
- Create: `Headless/Sim.MemoryTests/AtomBagMergeDiffTests.cs`

**Interfaces:**
- Produces: `static AtomBag AtomBag.Merge(AtomBag predicted, AtomBag delta)` — union; delta value wins where both contain a type (recall reconstruction `predicted ⊕ delta`). `static AtomBag AtomBag.Diff(AtomBag percept, AtomBag prediction)` — every percept atom whose type is absent from `prediction` or whose value differs; atoms only in `prediction` are excluded.

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/AtomBagMergeDiffTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomBagMergeDiffTests
    {
        static Atom A(int type, double v) => new Atom(new AtomTypeId(type), Fixed.FromDouble(v));

        [Fact]
        public void Merge_Disjoint_IsSortedUnion()
        {
            var p = AtomBag.Create(new[] { A(1, 0.1), A(3, 0.3) });
            var d = AtomBag.Create(new[] { A(2, 0.2), A(4, 0.4) });
            var m = AtomBag.Merge(p, d);
            Assert.Equal(new[] { 1, 2, 3, 4 }, m.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Merge_Overlap_DeltaWins()
        {
            var p = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            var d = AtomBag.Create(new[] { A(2, 0.9) });
            var m = AtomBag.Merge(p, d);

            Assert.Equal(2, m.Count);
            m.TryGet(new AtomTypeId(2), out var v);
            Assert.Equal(Fixed.FromDouble(0.9), v);          // delta won
            m.TryGet(new AtomTypeId(1), out var v1);
            Assert.Equal(Fixed.FromDouble(0.1), v1);         // prediction kept
        }

        [Fact]
        public void Merge_WithEmpty_ReturnsOther()
        {
            var p = AtomBag.Create(new[] { A(1, 0.1) });
            Assert.Equal(1, AtomBag.Merge(p, AtomBag.Empty).Count);
            Assert.Equal(1, AtomBag.Merge(AtomBag.Empty, p).Count);
            Assert.Same(AtomBag.Empty, AtomBag.Merge(AtomBag.Empty, AtomBag.Empty));
        }

        [Fact]
        public void Diff_IdenticalBags_IsEmpty()
        {
            var bag = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            Assert.Same(AtomBag.Empty, AtomBag.Diff(bag, bag));
        }

        [Fact]
        public void Diff_KeepsNewAndChanged_DropsMatched()
        {
            var percept    = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.9), A(3, 0.3) });
            var prediction = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            var diff = AtomBag.Diff(percept, prediction);

            // type 1 matched -> dropped; type 2 changed -> kept; type 3 new -> kept
            Assert.Equal(new[] { 2, 3 }, diff.Atoms.Select(a => a.Type.Value).ToArray());
            diff.TryGet(new AtomTypeId(2), out var v2);
            Assert.Equal(Fixed.FromDouble(0.9), v2);
        }

        [Fact]
        public void Diff_AtomsOnlyInPrediction_AreExcluded()
        {
            var percept    = AtomBag.Create(new[] { A(1, 0.1) });
            var prediction = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            Assert.Same(AtomBag.Empty, AtomBag.Diff(percept, prediction));
        }

        [Fact]
        public void MergePredictionWithDiff_ReconstructsPercept()
        {
            var percept    = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.9), A(3, 0.3) });
            var prediction = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            var delta = AtomBag.Diff(percept, prediction);
            var recon = AtomBag.Merge(prediction, delta);

            Assert.Equal(percept.Atoms.Select(a => a.Type.Value).ToArray(),
                         recon.Atoms.Select(a => a.Type.Value).ToArray());
            foreach (var a in percept.Atoms)
            {
                Assert.True(recon.TryGet(a.Type, out var rv));
                Assert.Equal(a.Value, rv);
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `Merge`/`Diff` do not exist.

- [ ] **Step 3: Implement `Merge` and `Diff`**

In `Assets/Sim/Memory/AtomBag.cs`, add these methods inside the class (after `Contains`):

```csharp
        /// <summary>
        /// Reconstruct: predicted ⊕ delta. Sorted union of both bags; where both contain a
        /// type, the delta's value wins (recall = category prediction overlaid with the
        /// episode's stored divergences). Inputs are sorted, so the merge output is too.
        /// </summary>
        public static AtomBag Merge(AtomBag predicted, AtomBag delta)
        {
            var p = predicted._atoms;
            var d = delta._atoms;
            var result = new List<Atom>(p.Length + d.Length);
            int i = 0, j = 0;
            while (i < p.Length && j < d.Length)
            {
                int cmp = p[i].Type.CompareTo(d[j].Type);
                if (cmp < 0) result.Add(p[i++]);
                else if (cmp > 0) result.Add(d[j++]);
                else { result.Add(d[j]); i++; j++; }   // both speak -> delta wins
            }
            while (i < p.Length) result.Add(p[i++]);
            while (j < d.Length) result.Add(d[j++]);
            return result.Count == 0 ? Empty : new AtomBag(result.ToArray());
        }

        /// <summary>
        /// The delta of a percept against a prediction: every percept atom whose type is
        /// absent from the prediction, or whose value differs from the prediction's. Atoms
        /// present only in the prediction are excluded — the diff is the percept's divergence,
        /// the minimal content a memory record must store. Percept is sorted, so output is too.
        /// </summary>
        public static AtomBag Diff(AtomBag percept, AtomBag prediction)
        {
            var result = new List<Atom>(percept._atoms.Length);
            foreach (var a in percept._atoms)
            {
                if (!prediction.TryGet(a.Type, out var pv) || pv != a.Value)
                    result.Add(a);
            }
            return result.Count == 0 ? Empty : new AtomBag(result.ToArray());
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/AtomBag.cs Headless/Sim.MemoryTests/AtomBagMergeDiffTests.cs
git commit -m "feat(memory): AtomBag Merge (delta-wins) + Diff (percept divergence)"
```

---

### Task 6: `RunningStat` — integer running statistics

**Files:**
- Create: `Assets/Sim/Memory/RunningStat.cs`
- Create: `Headless/Sim.MemoryTests/RunningStatTests.cs`

**Interfaces:**
- Consumes: `Fixed` (Task 1).
- Produces: `DaggerfallWorkshop.Sim.Memory.RunningStat` — mutable `struct`; fields `int Count`, `long Sum` (Σ raw, Q8), `long SumSq` (Σ raw², Q16); `void Add(Fixed)`; `Fixed Mean()` (Q8, zero when empty); `long VarianceRaw()` (population variance in Q16, zero when empty/single, never negative).

- [ ] **Step 1: Write the failing tests**

Create `Headless/Sim.MemoryTests/RunningStatTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class RunningStatTests
    {
        [Fact]
        public void Empty_HasZeroCount_MeanZero_VarianceZero()
        {
            var s = new RunningStat();
            Assert.Equal(0, s.Count);
            Assert.Equal(Fixed.Zero, s.Mean());
            Assert.Equal(0, s.VarianceRaw());
        }

        [Fact]
        public void Add_AccumulatesCountSumSumSq()
        {
            var s = new RunningStat();
            s.Add(Fixed.FromDouble(1.0));   // raw 256
            s.Add(Fixed.FromInt(0));        // raw 0
            Assert.Equal(2, s.Count);
            Assert.Equal(256, s.Sum);
            Assert.Equal(65536, s.SumSq);   // 256^2 + 0
        }

        [Fact]
        public void Mean_OfConstantStream_IsThatValue()
        {
            var s = new RunningStat();
            for (int i = 0; i < 5; i++) s.Add(Fixed.FromDouble(0.5));
            Assert.Equal(Fixed.FromDouble(0.5), s.Mean());
        }

        [Fact]
        public void Variance_OfConstantStream_IsZero()
        {
            var s = new RunningStat();
            for (int i = 0; i < 5; i++) s.Add(Fixed.FromDouble(0.5));
            Assert.Equal(0, s.VarianceRaw());
        }

        [Fact]
        public void Variance_OfSingleSample_IsZero()
        {
            var s = new RunningStat();
            s.Add(Fixed.FromDouble(0.9));
            Assert.Equal(0, s.VarianceRaw());
        }

        [Fact]
        public void Variance_OfZeroAndOne_IsQuarter()
        {
            // samples {0.0, 1.0}: population variance = 0.25 -> Q16 raw = 0.25 * 65536 = 16384
            var s = new RunningStat();
            s.Add(Fixed.FromInt(0));
            s.Add(Fixed.FromInt(1));
            Assert.Equal(Fixed.FromDouble(0.5), s.Mean());
            Assert.Equal(16384, s.VarianceRaw());
        }

        [Fact]
        public void LowSpread_StreamHasSmallVariance_HighSpread_HasLarge()
        {
            var low = new RunningStat();
            foreach (var v in new[] { 0.50, 0.51, 0.49, 0.50 }) low.Add(Fixed.FromDouble(v));

            var high = new RunningStat();
            foreach (var v in new[] { 0.05, 0.95, 0.10, 0.90 }) high.Add(Fixed.FromDouble(v));

            Assert.True(low.VarianceRaw() < high.VarianceRaw());
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: FAIL — `RunningStat` does not exist.

- [ ] **Step 3: Implement `RunningStat`**

Create `Assets/Sim/Memory/RunningStat.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Variance-gated running statistics for one atom type — the mechanical form of "the
    /// semantic fact is the intersection of the episodes." Integer accumulators only (no float
    /// sum), so it is replay-exact and machine-portable (spec: Determinism). Sum is in raw Q8
    /// units; SumSq in raw Q16 units (value.Raw squared). VarianceRaw is the spread² the A3
    /// variance gate compares — no sqrt needed at this layer.
    /// </summary>
    public struct RunningStat
    {
        public int Count;
        public long Sum;     // Σ value.Raw    (Q8)
        public long SumSq;   // Σ value.Raw^2  (Q16)

        public void Add(Fixed value)
        {
            Count++;
            Sum += value.Raw;
            SumSq += (long)value.Raw * value.Raw;
        }

        /// <summary>Mean as a Fixed (Q8). Zero when empty. Integer division truncates toward zero.</summary>
        public Fixed Mean()
        {
            if (Count == 0) return Fixed.Zero;
            return new Fixed((int)(Sum / Count));
        }

        /// <summary>Population variance in raw Q16 units (spread² without the sqrt). Zero when
        /// empty or for a single sample; never negative.</summary>
        public long VarianceRaw()
        {
            if (Count == 0) return 0;
            long meanRaw = Sum / Count;        // Q8
            long meanSq = meanRaw * meanRaw;   // Q16
            long v = SumSq / Count - meanSq;   // Q16
            return v < 0 ? 0 : v;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (all six test files green).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Memory/RunningStat.cs Headless/Sim.MemoryTests/RunningStatTests.cs
git commit -m "feat(memory): RunningStat integer running statistics (mean + variance)"
```

---

## Self-Review

**Spec coverage (roadmap A1 deliverables):**
- `AtomTypeId` (readonly struct, IComparable) → Task 2 ✓
- `Atom { AtomTypeId Type; <fixed-point> Value }` → Task 3 ✓ (value = `Fixed`, Task 1)
- `AtomBag` sorted, binary-search `TryGet`, `Merge` (delta wins), `Diff` (percept divergence) → Tasks 4–5 ✓
- Fixed-point running-stats `RunningStat { Count; Sum, SumSq }` → mean/spread, integer adds only → Task 6 ✓
- Tests: sorted invariant (T2/T4), binary search (T4), Merge/Diff correctness (T5), fixed-point arithmetic (T1), key-order determinism (sorted-array iteration throughout) ✓

**Type consistency:** `Fixed` (FractionalBits/Scale/Zero/One/Raw/FromInt/FromDouble/ToDouble/+/-/==/!=) used identically across Tasks 1, 3, 5, 6. `AtomTypeId.CompareTo` used by `AtomBag.Create`/`TryGet`/`Merge`. `AtomBag` private ctor + `Empty` reused by `Create`/`Merge`/`Diff`. `RunningStat` raw Q8/Q16 unit contract stated in fields and asserted in tests.

**Determinism check:** no floats on any runtime path (only `Fixed.FromDouble`/`ToDouble` + test assertions); all bag iteration is over sorted arrays; all stat math is integer. Satisfies the Global Constraints.

**Note for the executor:** these are pure transcription tasks with complete code — the cheapest implementer tier suffices, or inline execution. No design decisions remain open in A1; the open knobs (similarity threshold, surprise aggregation, caps/rates) belong to A3–A5.
