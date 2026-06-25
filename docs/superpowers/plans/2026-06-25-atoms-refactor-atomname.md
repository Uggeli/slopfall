# Atoms Refactor — `AtomName` + Catalog (Phase A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the id-band int scheme for perceivable/memory atoms with one flat `AtomName` enum and one frozen `AtomCatalog`, so atom identity is compiler-enforced, recognition can match seeds to percepts, and there is structurally no place to write a "verdict."

**Architecture:** Add `AtomName` (a dense, auto-numbered enum; `(int)AtomName` *is* the `AtomTypeId.Value`) and `AtomCatalog` (per-atom `Category` / `Salience` / `Shareable` / reserved `Tone`). The existing stamp helpers (`PerceivableAtoms`/`PlaceAtoms`/`SomaticAtoms`) keep their signatures but re-route their bodies through `AtomName`, so the seven call sites never change. The four places that used id **bands as logic** migrate to catalog queries via `AtomCatalog.For(AtomTypeId)`, which resolves through a reverse `int→AtomName` map built over the *current* helpers — this is the spec's "From maps let old ids and new names coexist," and it lets every consumer migrate as its own green commit *before* the helper bodies flip to dense ids. The refactor is behaviour-preserving; the deterministic soak is the final gate.

**Tech Stack:** C# on **.NET 10** (`net10.0`, `LangVersion latest`) — the sim is no longer Unity/Godot-coupled, so modern C# (12+) is fair game (switch expressions, pattern matching, target-typed `new`, generic `Enum.GetValues<T>()`). xUnit 2.9.3, fixed-point `Fixed` math (the Memory subsystem is float-free).

## Global Constraints

- **Nullable is `disable`** in every project — write no `?`/`!` nullable annotations.
- **.NET 10 / modern C#** — no Unity/Godot dual-compile constraint; target `net10.0`, `LangVersion latest`. Switch expressions, pattern matching, and `Enum.GetValues<T>()` are all fine. Match each file's surrounding style (the Memory files use block-scoped namespaces and `readonly struct`s — keep that).
- **Float-free Memory** — atom values and tone use `Fixed` (`Fixed.Zero`/`Fixed.One`/`Fixed.FromDouble`), never `double`/`float` in stored/compared atom data.
- **New atom types live in `DaggerfallWorkshop.Sim.Memory`** (alongside `AtomTypeId`, `PerceivableAtoms`, …), in files under `Assets/Sim/Memory/`. Source enums (`EntityKind`, `ResidentRole`, `ActivityKind`, `BuildingKind`) live in the parent `DaggerfallWorkshop.Sim` and are visible without a `using` (namespace nesting).
- **Behaviour-preserving** — no behavioural delta in the deterministic soak (seed 12345). Atom *integers* change (old bands → dense); nothing persists them, so that is allowed. A soak *behaviour* delta is a bug.
- **Build/test from `/home/uggeli/slopfall/Headless`.** Affected tests: `Sim.MemoryTests` (225 pass baseline). Keep `Sim.SpatialTests` (13 pass) green too.
- **Commit style:** conventional commits, scope `atoms` (e.g. `feat(atoms): …`, `test(atoms): …`, `refactor(atoms): …`). End every commit message body with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## File Structure

**New files (all in `/home/uggeli/slopfall/Assets/Sim/Memory/`, namespace `DaggerfallWorkshop.Sim.Memory`):**
- `AtomName.cs` — the flat enum (one member per perceivable/memory atom; `None = 0`).
- `AtomNames.cs` — `From(EntityKind|ResidentRole|ActivityKind|BuildingKind)` + `FromRace(int)` + `RaceRoster` + `ToId(this AtomName)` extension. The single source of "which source value is which atom."
- `AtomCatalog.cs` — `AtomCategory` enum, `AtomTone` struct, `AtomEntry` struct, and the frozen `AtomCatalog` (entries keyed by `AtomName`, reverse `int→AtomName` map, `For(AtomName)`, `For(AtomTypeId)`, `NameOf(AtomTypeId)`, `IsDefined`).

**Modified files:**
- `Assets/Sim/Memory/PerceivableAtoms.cs` — bodies re-routed through `AtomName` (Task 8); band consts deleted (Task 10).
- `Assets/Sim/Memory/PlaceAtoms.cs` — same.
- `Assets/Sim/Memory/SomaticAtoms.cs` — same.
- `Assets/Sim/Memory/MemorySalience.cs` — `For` delegates to the catalog (Task 3).
- `Assets/Sim/Engine/Units/PerceivableRegistry.cs` — `Signature` uses `IsIdentity` (Task 4).
- `Assets/Sim/Engine/Units/PerceivableActivitySystem.cs` — activity extraction uses `Category==Activity` (Task 5).
- `Assets/Sim/Engine/Units/Communication.cs` — gossip gate uses `Shareable` (Task 6).
- `Assets/Sim/Memory/MemorySeeds.cs` — **deleted** (Task 9).

**Modified tests (all in `/home/uggeli/slopfall/Headless/Sim.MemoryTests/`):**
- `PerceivableAtomsTests.cs`, `PlaceMemoryWriteTests.cs` (Task 8) — direct band-int asserts → catalog semantics.
- `LearnedValenceReinforceTests.cs`, `MemoryWriteSystemTests.cs` (Task 4) — `>= ActivityBase` → catalog.
- `PerceivableActivityTests.cs` (Task 5) — activity-range filter → catalog.
- `PerceivableSeedingTests.cs`, `PlaceSeedingTests.cs` (Task 7) — band-range checks → catalog category.
- `UtteranceWireTests.cs` (Task 8) — stale `// 6001` comment.
- `MemorySeedsTests.cs` — **deleted** (Task 9); a new recognition payoff test replaces it.

**New tests:** `AtomNamesTests.cs` (Task 1), `AtomCatalogTests.cs` (Task 2), `RecognitionPayoffTests.cs` (Task 9).

---

## Task 0: Capture the behavioural baseline

**Files:**
- Create: `/tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/baseline-gallotale.txt`
- Create: `/tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/baseline-gothway.txt`

The soak is deterministic (seed 12345). Capture HEAD's behaviour *before* any code change so Task 11 can diff against it.

- [ ] **Step 1: Confirm the green baseline builds and tests pass**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Passed!  - Failed: 0, Passed: 225` (or skips if ARENA2 absent — see note).

> **ARENA2 note:** Several tests and the soak need `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2`. If that data is absent the ARENA2-gated tests pass *vacuously* (early-return). The soak gate (Task 11) **requires** the data; if it is unavailable, stop and tell the user — the behaviour-preserving gate cannot run without it.

- [ ] **Step 2: Capture both baseline soaks**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall Gallotale 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/baseline-gallotale.txt 2>&1
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/baseline-gothway.txt 2>&1
```
Expected: each file ends with a `SOAK DONE — pop … -> …, 864000 ticks` line (Gallotale `349 -> 352`, Gothway `337 -> 340` on current master; the exact numbers are whatever HEAD produces — that is the baseline). No commit (scratchpad only).

---

## Task 1: `AtomName` enum + `AtomNames` From-maps

**Files:**
- Create: `/home/uggeli/slopfall/Assets/Sim/Memory/AtomName.cs`
- Create: `/home/uggeli/slopfall/Assets/Sim/Memory/AtomNames.cs`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/AtomNamesTests.cs`

**Interfaces:**
- Produces: `enum AtomName` (member `None = 0`); `static class AtomNames` with `AtomName From(EntityKind)`, `AtomName From(ResidentRole)`, `AtomName From(ActivityKind)`, `AtomName From(BuildingKind)`, `AtomName FromRace(int)`, `int[] RaceRoster`, and extension `AtomTypeId ToId(this AtomName)`. Sentinels (`EntityKind.Unknown`, `ActivityKind.None`, `BuildingKind.None`, race `-1`/unknown) map to `AtomName.None`.

- [ ] **Step 1: Write the failing test**

Create `/home/uggeli/slopfall/Headless/Sim.MemoryTests/AtomNamesTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Xunit;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;

namespace Sim.MemoryTests
{
    public class AtomNamesTests
    {
        // Every real (non-sentinel) source value maps to a distinct, non-None AtomName.
        [Fact]
        public void From_IsTotalAndInjective_OverEverySourceEnum()
        {
            var seen = new HashSet<AtomName>();

            void Check(AtomName n, string what)
            {
                Assert.True(n != AtomName.None, what + " mapped to AtomName.None");
                Assert.True(seen.Add(n), what + " collided with another atom (" + n + ")");
            }

            foreach (EntityKind k in Enum.GetValues(typeof(EntityKind)))
                if (k != EntityKind.Unknown) Check(AtomNames.From(k), "EntityKind." + k);

            foreach (ResidentRole r in Enum.GetValues(typeof(ResidentRole)))
                Check(AtomNames.From(r), "ResidentRole." + r);

            foreach (int race in AtomNames.RaceRoster)
                Check(AtomNames.FromRace(race), "Race " + race);

            foreach (ActivityKind a in Enum.GetValues(typeof(ActivityKind)))
                if (a != ActivityKind.None) Check(AtomNames.From(a), "ActivityKind." + a);

            foreach (BuildingKind b in Enum.GetValues(typeof(BuildingKind)))
                if (b != BuildingKind.None) Check(AtomNames.From(b), "BuildingKind." + b);
        }

        // Sentinels are non-perceivable: they map to None, not a real atom.
        [Fact]
        public void From_Sentinels_MapToNone()
        {
            Assert.Equal(AtomName.None, AtomNames.From(EntityKind.Unknown));
            Assert.Equal(AtomName.None, AtomNames.From(ActivityKind.None));
            Assert.Equal(AtomName.None, AtomNames.From(BuildingKind.None));
            Assert.Equal(AtomName.None, AtomNames.FromRace(-1));
            Assert.Equal(AtomName.None, AtomNames.FromRace(999));
        }

        // ToId is the free conversion: (int)AtomName IS the AtomTypeId payload.
        [Fact]
        public void ToId_RoundTrips_ThroughAtomTypeIdValue()
        {
            AtomTypeId id = AtomName.Civilian.ToId();
            Assert.Equal((int)AtomName.Civilian, id.Value);
            Assert.Equal(AtomName.Civilian, (AtomName)id.Value);
            Assert.True(AtomName.None.ToId().IsNone);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~AtomNamesTests"`
Expected: FAIL — compile error, `AtomName`/`AtomNames` do not exist.

- [ ] **Step 3: Create the `AtomName` enum**

Create `/home/uggeli/slopfall/Assets/Sim/Memory/AtomName.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The single, flat identity vocabulary for every perceivable / memory atom. The enum member
    /// IS the identity — auto-numbered, so the compiler guarantees uniqueness (no hand-allocated
    /// bands to collide). The underlying int is the AtomTypeId.Value (see AtomNames.ToId): nothing
    /// persists it across runs, so the numbering is free and chosen dense/clean. Identity atoms
    /// (Civilian, PlaceTavern) and — later — property atoms share this one enum.
    ///
    /// There is, by construction, NO verdict member (no Predator/Threat/Danger): a thing's atoms
    /// describe; the perceiver concludes. The affective TONE a Phase-B reading consumes lives in
    /// AtomCatalog, never here.
    /// </summary>
    public enum AtomName
    {
        None = 0,

        // ── Entity kinds (EntityKind) ──
        Player, EnemyClass, EnemyMonster, Civilian, StaticNpc,

        // ── Resident roles (ResidentRole) ──
        Resident, Keeper,

        // ── Races (Daggerfall Races roster) ──
        RaceBreton, RaceRedguard, RaceNord, RaceDarkElf, RaceHighElf, RaceWoodElf,
        RaceKhajiit, RaceArgonian, RaceVampire, RaceWerewolf, RaceWereboar,

        // ── Activities (ActivityKind, minus None) ──
        ActIdle, ActWander, ActSleep, ActWork, ActLabor, ActFarm, ActFish, ActMine,
        ActEatHome, ActEatTavern, ActSocialize, ActVisit, ActBuy, ActChat, ActSeekHelp,
        ActBeg, ActSteal, ActFlee, ActAttack, ActUseItem, ActStoreItem, ActWeave,
        ActPatrol, ActStandWatch, ActGossip, ActNegotiate,

        // ── Somatic (an agent's perception of its own body) ──
        SomaticHunger, SomaticEnergy, SomaticFear,

        // ── Place: building kinds (BuildingKind, minus None) ──
        PlaceAlchemist, PlaceHouseForSale, PlaceArmorer, PlaceBank, PlaceTown4,
        PlaceBookseller, PlaceClothingStore, PlaceFurnitureStore, PlaceGemStore,
        PlaceGeneralStore, PlaceLibrary, PlaceGuildHall, PlacePawnShop, PlaceWeaponSmith,
        PlaceTemple, PlaceTavern, PlacePalace, PlaceHouse1, PlaceHouse2, PlaceHouse3,
        PlaceHouse4, PlaceHouse5, PlaceHouse6, PlaceTown23, PlaceShip,
        PlaceSpecial1, PlaceSpecial2, PlaceSpecial3, PlaceSpecial4,
        PlaceFarm, PlaceFishery, PlaceMine, PlacePasture, PlaceWeaver,

        // ── Place: graded facts ──
        PlaceProvisions, PlaceDanger,
    }
}
```

- [ ] **Step 4: Create the `AtomNames` From-maps**

Create `/home/uggeli/slopfall/Assets/Sim/Memory/AtomNames.cs`:

```csharp
namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Maps each owning source enum to its AtomName — the atom doc's "two indexes of one relation."
    /// The source enums stay (behaviour/employment/spawning use them); this is the perceivable
    /// identity index. Sentinel source values (Unknown / None / unknown race id) are non-perceivable
    /// and map to AtomName.None. ToId() exposes the free AtomName→AtomTypeId conversion.
    /// </summary>
    public static class AtomNames
    {
        /// <summary>The closed Daggerfall race roster (Races enum, 1..11). -1/None is the sentinel.</summary>
        public static readonly int[] RaceRoster = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        public static AtomTypeId ToId(this AtomName name) => new AtomTypeId((int)name);

        public static AtomName From(EntityKind kind) => kind switch
        {
            EntityKind.Player       => AtomName.Player,
            EntityKind.EnemyClass   => AtomName.EnemyClass,
            EntityKind.EnemyMonster => AtomName.EnemyMonster,
            EntityKind.CivilianNPC  => AtomName.Civilian,
            EntityKind.StaticNPC    => AtomName.StaticNpc,
            _                       => AtomName.None,   // Unknown — never stamped
        };

        public static AtomName From(ResidentRole role) => role switch
        {
            ResidentRole.Resident => AtomName.Resident,
            ResidentRole.Keeper   => AtomName.Keeper,
            _                     => AtomName.None,
        };

        public static AtomName FromRace(int raceId) => raceId switch
        {
            1  => AtomName.RaceBreton,
            2  => AtomName.RaceRedguard,
            3  => AtomName.RaceNord,
            4  => AtomName.RaceDarkElf,
            5  => AtomName.RaceHighElf,
            6  => AtomName.RaceWoodElf,
            7  => AtomName.RaceKhajiit,
            8  => AtomName.RaceArgonian,
            9  => AtomName.RaceVampire,
            10 => AtomName.RaceWerewolf,
            11 => AtomName.RaceWereboar,
            _  => AtomName.None,   // -1/None or any unknown id
        };

        public static AtomName From(ActivityKind kind) => kind switch
        {
            ActivityKind.Idle       => AtomName.ActIdle,
            ActivityKind.Wander     => AtomName.ActWander,
            ActivityKind.Sleep      => AtomName.ActSleep,
            ActivityKind.Work       => AtomName.ActWork,
            ActivityKind.Labor      => AtomName.ActLabor,
            ActivityKind.Farm       => AtomName.ActFarm,
            ActivityKind.Fish       => AtomName.ActFish,
            ActivityKind.Mine       => AtomName.ActMine,
            ActivityKind.EatHome    => AtomName.ActEatHome,
            ActivityKind.EatTavern  => AtomName.ActEatTavern,
            ActivityKind.Socialize  => AtomName.ActSocialize,
            ActivityKind.Visit      => AtomName.ActVisit,
            ActivityKind.Buy        => AtomName.ActBuy,
            ActivityKind.Chat       => AtomName.ActChat,
            ActivityKind.SeekHelp   => AtomName.ActSeekHelp,
            ActivityKind.Beg        => AtomName.ActBeg,
            ActivityKind.Steal      => AtomName.ActSteal,
            ActivityKind.Flee       => AtomName.ActFlee,
            ActivityKind.Attack     => AtomName.ActAttack,
            ActivityKind.UseItem    => AtomName.ActUseItem,
            ActivityKind.StoreItem  => AtomName.ActStoreItem,
            ActivityKind.Weave      => AtomName.ActWeave,
            ActivityKind.Patrol     => AtomName.ActPatrol,
            ActivityKind.StandWatch => AtomName.ActStandWatch,
            ActivityKind.Gossip     => AtomName.ActGossip,
            ActivityKind.Negotiate  => AtomName.ActNegotiate,
            _                       => AtomName.None,   // None — no activity atom
        };

        public static AtomName From(BuildingKind kind) => kind switch
        {
            BuildingKind.Alchemist      => AtomName.PlaceAlchemist,
            BuildingKind.HouseForSale   => AtomName.PlaceHouseForSale,
            BuildingKind.Armorer        => AtomName.PlaceArmorer,
            BuildingKind.Bank           => AtomName.PlaceBank,
            BuildingKind.Town4          => AtomName.PlaceTown4,
            BuildingKind.Bookseller     => AtomName.PlaceBookseller,
            BuildingKind.ClothingStore  => AtomName.PlaceClothingStore,
            BuildingKind.FurnitureStore => AtomName.PlaceFurnitureStore,
            BuildingKind.GemStore       => AtomName.PlaceGemStore,
            BuildingKind.GeneralStore   => AtomName.PlaceGeneralStore,
            BuildingKind.Library        => AtomName.PlaceLibrary,
            BuildingKind.GuildHall      => AtomName.PlaceGuildHall,
            BuildingKind.PawnShop       => AtomName.PlacePawnShop,
            BuildingKind.WeaponSmith    => AtomName.PlaceWeaponSmith,
            BuildingKind.Temple         => AtomName.PlaceTemple,
            BuildingKind.Tavern         => AtomName.PlaceTavern,
            BuildingKind.Palace         => AtomName.PlacePalace,
            BuildingKind.House1         => AtomName.PlaceHouse1,
            BuildingKind.House2         => AtomName.PlaceHouse2,
            BuildingKind.House3         => AtomName.PlaceHouse3,
            BuildingKind.House4         => AtomName.PlaceHouse4,
            BuildingKind.House5         => AtomName.PlaceHouse5,
            BuildingKind.House6         => AtomName.PlaceHouse6,
            BuildingKind.Town23         => AtomName.PlaceTown23,
            BuildingKind.Ship           => AtomName.PlaceShip,
            BuildingKind.Special1       => AtomName.PlaceSpecial1,
            BuildingKind.Special2       => AtomName.PlaceSpecial2,
            BuildingKind.Special3       => AtomName.PlaceSpecial3,
            BuildingKind.Special4       => AtomName.PlaceSpecial4,
            BuildingKind.Farm           => AtomName.PlaceFarm,
            BuildingKind.Fishery        => AtomName.PlaceFishery,
            BuildingKind.Mine           => AtomName.PlaceMine,
            BuildingKind.Pasture        => AtomName.PlacePasture,
            BuildingKind.Weaver         => AtomName.PlaceWeaver,
            _                           => AtomName.None,   // None sentinel
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~AtomNamesTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/AtomName.cs Assets/Sim/Memory/AtomNames.cs Headless/Sim.MemoryTests/AtomNamesTests.cs
git commit -m "$(cat <<'EOF'
feat(atoms): AtomName enum + From maps (Phase A, D1)

One flat, auto-numbered identity enum for every perceivable/memory atom,
with total+injective From maps off the four source enums (sentinels →
None). ToId() exposes the free AtomName→AtomTypeId conversion. No wiring
yet — helpers and consumers still on bands.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `AtomCatalog` — the frozen four-faces table

**Files:**
- Create: `/home/uggeli/slopfall/Assets/Sim/Memory/AtomCatalog.cs`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/AtomCatalogTests.cs`

**Interfaces:**
- Consumes: `AtomName`, `AtomNames` (Task 1); existing `AtomMeta`, `MemoryFlags`, `AtomTypeId`, `Fixed` (`DaggerfallWorkshop.Sim.Memory`); source enums.
- Produces: `enum AtomCategory { None, Kind, Role, Race, Activity, Somatic, PlaceKind, PlaceProvisions, PlaceDanger }`; `struct AtomTone { Fixed Valence; Fixed Arousal; static AtomTone Neutral }`; `struct AtomEntry { AtomCategory Category; AtomMeta Salience; bool Shareable; AtomTone Tone; bool IsIdentity }`; `static class AtomCatalog` with `AtomEntry For(AtomName)`, `AtomEntry For(AtomTypeId)`, `AtomName NameOf(AtomTypeId)`, `bool IsDefined(AtomName)`.

> **Why the catalog is populated by iterating the From maps:** within a family every atom shares the same `(Category, Salience, Shareable)`, so iterating `AtomNames.From(...)` over each source enum keeps the catalog and the From maps a single source of truth — add an atom and forget its From entry, the completeness test finds the hole. The reverse `int→AtomName` map is built the same way, calling the *current* helpers, so `For(AtomTypeId)` resolves correctly whether the helpers still emit old bands (now) or dense ids (after Task 8). **Salience values are transcribed from the old `MemorySalience.For` (`MemorySalience.cs:15-26`); Shareable reproduces the old `>= PlaceAtoms.KindBase` gossip gate; the family→Category split reproduces the old band ranges.** The deterministic soak (Task 11) is the end-to-end proof these transcriptions are faithful.

- [ ] **Step 1: Write the failing test**

Create `/home/uggeli/slopfall/Headless/Sim.MemoryTests/AtomCatalogTests.cs`:

```csharp
using System;
using Xunit;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;

namespace Sim.MemoryTests
{
    public class AtomCatalogTests
    {
        // Completeness: every non-None AtomName has exactly one catalog entry (no holes).
        [Fact]
        public void Catalog_HasEntry_ForEveryAtomName()
        {
            foreach (AtomName n in Enum.GetValues(typeof(AtomName)))
            {
                if (n == AtomName.None) continue;
                Assert.True(AtomCatalog.IsDefined(n), "no catalog entry for AtomName." + n);
            }
        }

        // Per-family Category / Salience / Shareable, transcribed from the old band scheme.
        [Theory]
        [InlineData(AtomName.Civilian,       AtomCategory.Kind,            (byte)160, MemoryFlags.None,     false, true)]
        [InlineData(AtomName.Keeper,         AtomCategory.Role,            (byte)160, MemoryFlags.None,     false, true)]
        [InlineData(AtomName.RaceBreton,     AtomCategory.Race,            (byte)160, MemoryFlags.None,     false, true)]
        [InlineData(AtomName.ActIdle,        AtomCategory.Activity,        (byte)160, MemoryFlags.None,     false, false)]
        [InlineData(AtomName.SomaticHunger,  AtomCategory.Somatic,         (byte)160, MemoryFlags.None,     false, false)]
        [InlineData(AtomName.PlaceTavern,    AtomCategory.PlaceKind,       (byte)255, MemoryFlags.Innate,   true,  false)]
        [InlineData(AtomName.PlaceProvisions,AtomCategory.PlaceProvisions, (byte)160, MemoryFlags.None,     true,  false)]
        [InlineData(AtomName.PlaceDanger,    AtomCategory.PlaceDanger,     (byte)255, MemoryFlags.Surprise, true,  false)]
        public void Catalog_Entry_HasExpectedFaces(
            AtomName name, AtomCategory cat, byte strength, MemoryFlags flags, bool shareable, bool identity)
        {
            AtomEntry e = AtomCatalog.For(name);
            Assert.Equal(cat, e.Category);
            Assert.Equal(strength, e.Salience.Strength);
            Assert.Equal(flags, e.Salience.Flags);
            Assert.Equal(shareable, e.Shareable);
            Assert.Equal(identity, e.IsIdentity);
        }

        // Tone is reserved (neutral) in Phase A — declared, not filled.
        [Fact]
        public void Catalog_Tone_IsNeutral_InPhaseA()
        {
            Assert.Equal(Fixed.Zero, AtomCatalog.For(AtomName.PlaceDanger).Tone.Valence);
            Assert.Equal(Fixed.Zero, AtomCatalog.For(AtomName.PlaceDanger).Tone.Arousal);
        }

        // Reverse resolution: a stamped atom resolves back to its AtomName and entry.
        // (Works whatever ints the helpers currently emit — old bands today, dense after Task 8.)
        [Fact]
        public void For_AtomTypeId_ResolvesThroughTheHelpers()
        {
            AtomTypeId tavern = PlaceAtoms.Kind(BuildingKind.Tavern);
            Assert.Equal(AtomName.PlaceTavern, AtomCatalog.NameOf(tavern));
            Assert.Equal(AtomCategory.PlaceKind, AtomCatalog.For(tavern).Category);

            AtomTypeId hunger = SomaticAtoms.Hunger;
            Assert.Equal(AtomName.SomaticHunger, AtomCatalog.NameOf(hunger));
            Assert.False(AtomCatalog.For(hunger).IsIdentity);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~AtomCatalogTests"`
Expected: FAIL — compile error, `AtomCatalog`/`AtomCategory`/`AtomEntry`/`AtomTone` do not exist.

- [ ] **Step 3: Create the catalog**

Create `/home/uggeli/slopfall/Assets/Sim/Memory/AtomCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>The atom family — the thing the old id-band high-digits used to encode, now a field.</summary>
    public enum AtomCategory
    {
        None,
        Kind, Role, Race,          // identity
        Activity, Somatic,         // transient state
        PlaceKind, PlaceProvisions, PlaceDanger,
    }

    /// <summary>Reserved affective cell (Phase B fills it). Phase A leaves it neutral. The catalog
    /// has a TONE face and no VERDICT face — there is nowhere on an atom to write "threat."</summary>
    public readonly struct AtomTone
    {
        public readonly Fixed Valence;
        public readonly Fixed Arousal;
        public AtomTone(Fixed valence, Fixed arousal) { Valence = valence; Arousal = arousal; }
        public static readonly AtomTone Neutral = new AtomTone(Fixed.Zero, Fixed.Zero);
    }

    /// <summary>One atom's frozen, per-type metadata.</summary>
    public readonly struct AtomEntry
    {
        public readonly AtomCategory Category;
        public readonly AtomMeta Salience;     // memory encode/decay seed (was MemorySalience.For)
        public readonly bool Shareable;        // gossip-relayable (was the >= KindBase gate)
        public readonly AtomTone Tone;         // reserved (Phase B)

        public AtomEntry(AtomCategory category, AtomMeta salience, bool shareable, AtomTone tone)
        {
            Category = category; Salience = salience; Shareable = shareable; Tone = tone;
        }

        public bool IsIdentity =>
            Category == AtomCategory.Kind || Category == AtomCategory.Role || Category == AtomCategory.Race;
    }

    /// <summary>
    /// The single frozen source of truth for per-atom metadata, keyed by AtomName. Replaces three
    /// scattered band-checks: MemorySalience's by-band switch, the >= ActivityBase identity test,
    /// and the >= KindBase gossip gate. Populated by iterating the From maps so catalog and From
    /// stay one source of truth. For(AtomTypeId) resolves an emitted atom back to its entry via a
    /// reverse map built over the current helpers (old bands now, dense ids after the helper flip).
    /// </summary>
    public static class AtomCatalog
    {
        const byte Ordinary = 160;   // a learned, fade-able fact (was MemorySalience.Ordinary)

        static readonly Dictionary<AtomName, AtomEntry> _entries = BuildEntries();
        static readonly Dictionary<int, AtomName> _byId = BuildReverse();

        static readonly AtomEntry Fallback =
            new AtomEntry(AtomCategory.None, new AtomMeta(Ordinary, MemoryFlags.None), false, AtomTone.Neutral);

        public static bool IsDefined(AtomName name) => _entries.ContainsKey(name);

        public static AtomEntry For(AtomName name) =>
            _entries.TryGetValue(name, out var e) ? e : Fallback;

        public static AtomName NameOf(AtomTypeId atom) =>
            _byId.TryGetValue(atom.Value, out var n) ? n : AtomName.None;

        public static AtomEntry For(AtomTypeId atom) => For(NameOf(atom));

        static Dictionary<AtomName, AtomEntry> BuildEntries()
        {
            var ordinary = new AtomMeta(Ordinary, MemoryFlags.None);
            var innate   = new AtomMeta(255, MemoryFlags.Innate);     // structural, permanent
            var surprise = new AtomMeta(255, MemoryFlags.Surprise);   // survival-grade, resists decay

            var m = new Dictionary<AtomName, AtomEntry>();

            void Put(AtomName n, AtomCategory c, AtomMeta sal, bool share)
            {
                if (n == AtomName.None) return;                       // sentinels carry no entry
                m[n] = new AtomEntry(c, sal, share, AtomTone.Neutral);
            }

            foreach (EntityKind k in Enum.GetValues(typeof(EntityKind)))
                Put(AtomNames.From(k), AtomCategory.Kind, ordinary, false);
            foreach (ResidentRole r in Enum.GetValues(typeof(ResidentRole)))
                Put(AtomNames.From(r), AtomCategory.Role, ordinary, false);
            foreach (int race in AtomNames.RaceRoster)
                Put(AtomNames.FromRace(race), AtomCategory.Race, ordinary, false);
            foreach (ActivityKind a in Enum.GetValues(typeof(ActivityKind)))
                Put(AtomNames.From(a), AtomCategory.Activity, ordinary, false);

            Put(AtomName.SomaticHunger, AtomCategory.Somatic, ordinary, false);
            Put(AtomName.SomaticEnergy, AtomCategory.Somatic, ordinary, false);
            Put(AtomName.SomaticFear,   AtomCategory.Somatic, ordinary, false);

            foreach (BuildingKind b in Enum.GetValues(typeof(BuildingKind)))
                Put(AtomNames.From(b), AtomCategory.PlaceKind, innate, true);

            Put(AtomName.PlaceProvisions, AtomCategory.PlaceProvisions, ordinary, true);
            Put(AtomName.PlaceDanger,     AtomCategory.PlaceDanger,     surprise, true);

            return m;
        }

        static Dictionary<int, AtomName> BuildReverse()
        {
            var m = new Dictionary<int, AtomName>();

            void Add(AtomTypeId id, AtomName n)
            {
                if (id.IsNone || n == AtomName.None) return;
                if (m.ContainsKey(id.Value))
                    throw new InvalidOperationException(
                        "Duplicate atom id " + id.Value + " for " + n + " and " + m[id.Value]);
                m[id.Value] = n;
            }

            foreach (EntityKind k in Enum.GetValues(typeof(EntityKind)))
                if (k != EntityKind.Unknown) Add(PerceivableAtoms.Kind(k), AtomNames.From(k));
            foreach (ResidentRole r in Enum.GetValues(typeof(ResidentRole)))
                Add(PerceivableAtoms.Role(r), AtomNames.From(r));
            foreach (int race in AtomNames.RaceRoster)
                Add(PerceivableAtoms.Race(race), AtomNames.FromRace(race));
            foreach (ActivityKind a in Enum.GetValues(typeof(ActivityKind)))
                if (a != ActivityKind.None) Add(PerceivableAtoms.Activity(a), AtomNames.From(a));

            Add(SomaticAtoms.Hunger, AtomName.SomaticHunger);
            Add(SomaticAtoms.Energy, AtomName.SomaticEnergy);
            Add(SomaticAtoms.Fear,   AtomName.SomaticFear);

            foreach (BuildingKind b in Enum.GetValues(typeof(BuildingKind)))
                if (b != BuildingKind.None) Add(PlaceAtoms.Kind(b), AtomNames.From(b));

            Add(PlaceAtoms.Provisions, AtomName.PlaceProvisions);
            Add(PlaceAtoms.Danger,     AtomName.PlaceDanger);

            return m;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~AtomCatalogTests"`
Expected: PASS (all `[Theory]` rows + the 3 `[Fact]`s).

- [ ] **Step 5: Run the full suite to confirm nothing else moved**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (now 225 + AtomNamesTests + AtomCatalogTests).

- [ ] **Step 6: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/AtomCatalog.cs Headless/Sim.MemoryTests/AtomCatalogTests.cs
git commit -m "$(cat <<'EOF'
feat(atoms): AtomCatalog — Category/Salience/Shareable, Tone reserved (D2)

Frozen four-faces table keyed by AtomName, populated by iterating the From
maps (catalog + From = one source of truth). Salience transcribed from
MemorySalience.For, Shareable from the >= KindBase gossip gate, Category
from the old band ranges. For(AtomTypeId) resolves via a reverse map over
the current helpers, so it works on old bands now and dense ids later.
Tone is declared-but-neutral: a tone face and no verdict face.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Migrate consumer 1 — `MemorySalience.For` → catalog

**Files:**
- Modify: `/home/uggeli/slopfall/Assets/Sim/Memory/MemorySalience.cs`

**Interfaces:**
- Consumes: `AtomCatalog.For(AtomTypeId).Salience` (Task 2).
- Produces: `MemorySalience.For(AtomTypeId) → AtomMeta` (unchanged signature; caller `AgentMemoryRegistry.cs:84` untouched).

> No new test: `AtomCatalogTests` already pins `Salience` per atom, and `MemorySalience.For` is now a one-line delegator. The existing `Sim.MemoryTests` (place-memory write paths that read salience) prove behaviour is unchanged.

- [ ] **Step 1: Replace the band switch with a catalog delegation**

In `/home/uggeli/slopfall/Assets/Sim/Memory/MemorySalience.cs`, replace the `For` method body (currently lines 15-26). The class currently is:

```csharp
public static class MemorySalience
{
    public const byte Ordinary = 160;   // a learned, fade-able fact

    /// <summary>The seed meta for one atom type.</summary>
    public static AtomMeta For(AtomTypeId atom)
    {
        int v = atom.Value;
        // PLACES vocabulary (PlaceAtoms id range 5000+).
        if (v >= PlaceAtoms.KindBase && v < PlaceAtoms.Provisions.Value)
            return new AtomMeta(255, MemoryFlags.Innate);     // building kind: structural, permanent
        if (v == PlaceAtoms.Danger.Value)
            return new AtomMeta(255, MemoryFlags.Surprise);   // danger: survival-grade, resists decay
        if (v == PlaceAtoms.Provisions.Value)
            return new AtomMeta(Ordinary, MemoryFlags.None);  // provisions: ordinary learned fact
        return new AtomMeta(Ordinary, MemoryFlags.None);      // default: ordinary
    }
}
```

Replace the whole class with:

```csharp
public static class MemorySalience
{
    public const byte Ordinary = 160;   // a learned, fade-able fact (kept for any external reference)

    /// <summary>The seed meta for one atom type — now the catalog's Salience face. Kept as a thin
    /// shim so the single caller (AgentMemoryRegistry.MergePlaceAtom) is untouched.</summary>
    public static AtomMeta For(AtomTypeId atom) => AtomCatalog.For(atom).Salience;
}
```

- [ ] **Step 2: Build + run the suite**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`. (Salience now comes from the catalog; place-memory tests unchanged.)

- [ ] **Step 3: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/MemorySalience.cs
git commit -m "$(cat <<'EOF'
refactor(atoms): MemorySalience.For delegates to AtomCatalog (D3 1/4)

Drop the by-band salience switch; the catalog owns per-atom Salience now.
Caller (AgentMemoryRegistry) unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Migrate consumer 2 — `PerceivableRegistry.Signature` → `IsIdentity`

**Files:**
- Modify: `/home/uggeli/slopfall/Assets/Sim/Engine/Units/PerceivableRegistry.cs`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/LearnedValenceReinforceTests.cs:23`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/MemoryWriteSystemTests.cs:55-56`

**Interfaces:**
- Consumes: `AtomCatalog.For(AtomTypeId).IsIdentity` (Task 2).
- Produces: `PerceivableRegistry.Signature(EntityId) → AtomBag` (unchanged signature; now keeps the identity atoms by catalog category rather than `< ActivityBase`).

> Old behaviour: keep atoms with `Type.Value < ActivityBase` (= exactly Kind/Role/Race, ids 1000-3999). New: keep atoms whose catalog entry `IsIdentity`. Same set today. The two consuming tests assert "no activity atoms in the signature"; rewrite them to the catalog predicate so they stay correct after the Task-8 id flip.

- [ ] **Step 1: Rewrite the two test assertions first (catalog predicate, flip-invariant)**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/LearnedValenceReinforceTests.cs`, replace line 23:

```csharp
        Assert.DoesNotContain(sig.Atoms, a => a.Type.Value >= PerceivableAtoms.ActivityBase);
```
with:
```csharp
        // A signature is identity atoms only — never a transient activity/somatic atom.
        Assert.All(sig.Atoms, a => Assert.True(AtomCatalog.For(a.Type).IsIdentity,
            "signature carried a non-identity atom: " + AtomCatalog.NameOf(a.Type)));
```
Ensure the file has `using DaggerfallWorkshop.Sim.Memory;` (it already does, per the audit).

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/MemoryWriteSystemTests.cs`, replace lines 55-56:

```csharp
        Assert.DoesNotContain(p.Signature.Atoms, a => a.Type.Value >= PerceivableAtoms.ActivityBase);
        Assert.DoesNotContain(p.Percept.Atoms, a => a.Type.Value >= PerceivableAtoms.ActivityBase);
```
with:
```csharp
        // Signature is identity-only; the percept may carry transient (activity/somatic) atoms,
        // so assert the split via the catalog, not a band boundary.
        Assert.All(p.Signature.Atoms, a => Assert.True(AtomCatalog.For(a.Type).IsIdentity,
            "signature carried a non-identity atom: " + AtomCatalog.NameOf(a.Type)));
        Assert.DoesNotContain(p.Percept.Atoms, a => AtomCatalog.For(a.Type).Category == AtomCategory.Activity);
```

- [ ] **Step 2: Run the two tests — they must still pass on the unchanged (band) Signature**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~LearnedValenceReinforceTests|FullyQualifiedName~MemoryWriteSystemTests"`
Expected: PASS. (The catalog predicate over old-band ids agrees with `< ActivityBase`; this proves the rewrite is behaviour-equivalent before we touch the producer.)

- [ ] **Step 3: Migrate `Signature` to the catalog**

In `/home/uggeli/slopfall/Assets/Sim/Engine/Units/PerceivableRegistry.cs`, replace the `Signature` method (lines 76-88):

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
with:
```csharp
    /// <summary>The entity's identity atoms only (Kind/Role/Race) — its recognition signature.</summary>
    public AtomBag Signature(EntityId id)
    {
        AtomBag full = Bag(id);
        List<Atom> ids = null;
        for (int i = 0; i < full.Count; i++)
        {
            if (!AtomCatalog.For(full[i].Type).IsIdentity) continue;
            if (ids == null) ids = new List<Atom>(full.Count);
            ids.Add(full[i]);
        }
        return ids == null ? AtomBag.Empty : AtomBag.Create(ids);
    }
```
Confirm the file's usings include `DaggerfallWorkshop.Sim.Memory` (it references `AtomBag`/`Atom`/`PerceivableAtoms` already, so the namespace is in scope).

- [ ] **Step 4: Run the suite**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/PerceivableRegistry.cs Headless/Sim.MemoryTests/LearnedValenceReinforceTests.cs Headless/Sim.MemoryTests/MemoryWriteSystemTests.cs
git commit -m "$(cat <<'EOF'
refactor(atoms): Signature keeps identity atoms by catalog, not band (D3 2/4)

PerceivableRegistry.Signature drops the >= ActivityBase test for
AtomCatalog.IsIdentity. Its two consuming tests move to the catalog
predicate (flip-invariant) and are shown equivalent on the old ids first.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Migrate consumer 3 — activity extraction → `Category==Activity`

**Files:**
- Modify: `/home/uggeli/slopfall/Assets/Sim/Engine/Units/PerceivableActivitySystem.cs:40-50`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PerceivableActivityTests.cs:39`

**Interfaces:**
- Consumes: `AtomCatalog.For(AtomTypeId).Category` (Task 2).
- Produces: `CurrentActivityAtom(AtomBag)` (private; unchanged behaviour — finds the bag's single activity atom).

- [ ] **Step 1: Rewrite the test helper first (catalog category, flip-invariant)**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PerceivableActivityTests.cs`, replace the filter at line 39:

```csharp
            .Where(x => x.Type.Value >= PerceivableAtoms.ActivityBase && x.Type.Value < PerceivableAtoms.ActivityBase + 1000)
```
with:
```csharp
            .Where(x => AtomCatalog.For(x.Type).Category == AtomCategory.Activity)
```
Confirm `using DaggerfallWorkshop.Sim.Memory;` is present (it is, per the audit).

- [ ] **Step 2: Run the test — passes on the unchanged (band) producer**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~PerceivableActivityTests"`
Expected: PASS (catalog category over old-band ids == the `[4000,5000)` range).

- [ ] **Step 3: Migrate `CurrentActivityAtom`**

In `/home/uggeli/slopfall/Assets/Sim/Engine/Units/PerceivableActivitySystem.cs`, replace `CurrentActivityAtom` (lines 40-50):

```csharp
    /// <summary>The activity-range atom currently in the bag (AtomTypeId.None if none).</summary>
    static AtomTypeId CurrentActivityAtom(AtomBag bag)
    {
        for (int i = 0; i < bag.Count; i++)
        {
            int v = bag[i].Type.Value;
            if (v >= PerceivableAtoms.ActivityBase && v < PerceivableAtoms.ActivityBase + 1000)
                return bag[i].Type;
        }
        return AtomTypeId.None;
    }
```
with:
```csharp
    /// <summary>The activity atom currently in the bag (AtomTypeId.None if none).</summary>
    static AtomTypeId CurrentActivityAtom(AtomBag bag)
    {
        for (int i = 0; i < bag.Count; i++)
            if (AtomCatalog.For(bag[i].Type).Category == AtomCategory.Activity)
                return bag[i].Type;
        return AtomTypeId.None;
    }
```

- [ ] **Step 4: Run the suite**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/PerceivableActivitySystem.cs Headless/Sim.MemoryTests/PerceivableActivityTests.cs
git commit -m "$(cat <<'EOF'
refactor(atoms): activity extraction by catalog category, not band (D3 3/4)

PerceivableActivitySystem.CurrentActivityAtom finds the activity atom via
AtomCatalog Category==Activity. Its test's range filter moves to the same
predicate (flip-invariant), shown equivalent on the old ids first.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Migrate consumer 4 — gossip gate → `Shareable`

**Files:**
- Modify: `/home/uggeli/slopfall/Assets/Sim/Engine/Units/Communication.cs:125`

**Interfaces:**
- Consumes: `AtomCatalog.For(AtomTypeId).Shareable` (Task 2).
- Produces: `CommunicationSystem.Update(long)` (unchanged behaviour — only place facts relay).

> No dedicated unit test exists for the gate; `AtomCatalogTests` pins `Shareable` per atom (place atoms true, the rest false), and the soak exercises gossip end-to-end. This is a mechanical predicate swap.

- [ ] **Step 1: Migrate the gate**

In `/home/uggeli/slopfall/Assets/Sim/Engine/Units/Communication.cs`, inside `CommunicationSystem.Update`, replace the gate line (currently line 125):

```csharp
            if (atoms[k].Type.Value < PlaceAtoms.KindBase) continue;   // P2: only PLACE facts relay
```
with:
```csharp
            if (!AtomCatalog.For(atoms[k].Type).Shareable) continue;   // P2: only PLACE facts relay
```
Confirm `using DaggerfallWorkshop.Sim.Memory;` is present (the method already references `PlaceAtoms`/`Atom`/`AtomBag`).

- [ ] **Step 2: Build + run the suite**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

- [ ] **Step 3: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/Communication.cs
git commit -m "$(cat <<'EOF'
refactor(atoms): gossip gate by catalog Shareable, not band (D3 4/4)

CommunicationSystem relays atoms whose catalog entry is Shareable (the
place facts) instead of testing >= PlaceAtoms.KindBase. Last band-as-logic
consumer migrated.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Migrate the ARENA2-gated seeding tests → catalog category

**Files:**
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PerceivableSeedingTests.cs:37-46`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PlaceSeedingTests.cs:40-47`

**Interfaces:**
- Consumes: `AtomCatalog.For(AtomTypeId).Category` (Task 2).

> These assert seeded atoms fall in band *ranges*. After Task 8 the ids are dense and the ranges break, so move them to catalog category now — flip-invariant, and still passing on the current band ids. (Both are gated by `Arena2Available`; they pass vacuously without the data, so run them with `DAGGERFALL_ARENA2` set to truly exercise the change.)

- [ ] **Step 1: Rewrite `PerceivableSeedingTests`**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PerceivableSeedingTests.cs`, replace the role-atom range check (lines 37-38):

```csharp
            Assert.Contains(bag.Atoms, a => a.Type.Value >= PerceivableAtoms.RoleBase
                                             && a.Type.Value < PerceivableAtoms.RoleBase + 1000);   // a role atom
```
with:
```csharp
            Assert.Contains(bag.Atoms, a => AtomCatalog.For(a.Type).Category == AtomCategory.Role);   // a role atom
```
and replace the race-atom range check (lines 45-46):

```csharp
                if (world.Perceivable.Bag(kv.Key).Atoms.Any(a => a.Type.Value >= PerceivableAtoms.RaceBase
                                                                  && a.Type.Value < PerceivableAtoms.RaceBase + 1000))
```
with:
```csharp
                if (world.Perceivable.Bag(kv.Key).Atoms.Any(a => AtomCatalog.For(a.Type).Category == AtomCategory.Race))
```

- [ ] **Step 2: Rewrite `PlaceSeedingTests`**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PlaceSeedingTests.cs`, replace the place-kind range check (lines 40-41):

```csharp
                if (places[i].DeltaBag.Atoms.Any(a => a.Type.Value >= PlaceAtoms.KindBase
                                                    && a.Type.Value < PlaceAtoms.Provisions.Value))
```
with:
```csharp
                if (places[i].DeltaBag.Atoms.Any(a => AtomCatalog.For(a.Type).Category == AtomCategory.PlaceKind))
```
and update the assertion message at line 47:

```csharp
            Assert.True(anyKindAtom, "a seeded place record must carry a building-kind atom (>= KindBase)");
```
to:
```csharp
            Assert.True(anyKindAtom, "a seeded place record must carry a building-kind atom (Category == PlaceKind)");
```

- [ ] **Step 3: Run the seeding tests (with ARENA2 so they don't pass vacuously)**

Run:
```bash
cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 \
  dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release \
  --filter "FullyQualifiedName~PerceivableSeedingTests|FullyQualifiedName~PlaceSeedingTests"
```
Expected: PASS (catalog category over old-band ids agrees with the old ranges).

- [ ] **Step 4: Commit**

```bash
cd /home/uggeli/slopfall
git add Headless/Sim.MemoryTests/PerceivableSeedingTests.cs Headless/Sim.MemoryTests/PlaceSeedingTests.cs
git commit -m "$(cat <<'EOF'
test(atoms): seeding tests assert catalog category, not band ranges (D6)

PerceivableSeedingTests (role/race) and PlaceSeedingTests (place-kind) move
off band-range checks to AtomCatalog Category — flip-invariant, still green
on the current ids. Prepares the helper-body flip.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: The flip — re-route helper bodies to dense `AtomName` ids

**Files:**
- Modify: `/home/uggeli/slopfall/Assets/Sim/Memory/PerceivableAtoms.cs`
- Modify: `/home/uggeli/slopfall/Assets/Sim/Memory/PlaceAtoms.cs`
- Modify: `/home/uggeli/slopfall/Assets/Sim/Memory/SomaticAtoms.cs`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PerceivableAtomsTests.cs`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PlaceMemoryWriteTests.cs`
- Test: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/UtteranceWireTests.cs:92`

**Interfaces:**
- Consumes: `AtomNames.From(...)`/`FromRace`/`ToId` (Task 1).
- Produces: the same helper signatures (`PerceivableAtoms.Kind/Role/Race/Activity/TryActivity`, `PlaceAtoms.Kind/Provisions/Danger`, `SomaticAtoms.Hunger/Energy/Fear`) now emitting `(int)AtomName`. **All seven stamp call-sites are unchanged.** The band-base constants stay for now (deleted in Task 10) so the build is green between tasks.

> This is the one commit where atom **integers** change (old bands → dense). All four consumers already read the catalog (which re-resolves via the rebuilt reverse map), so they keep working. Only tests that hard-code literal ints break — rewrite them here.

- [ ] **Step 1: Re-route `PerceivableAtoms` bodies (keep the band consts for now)**

In `/home/uggeli/slopfall/Assets/Sim/Memory/PerceivableAtoms.cs`, replace the four helper bodies and `TryActivity` while keeping the four `const int …Base` lines in place (Task 10 removes them):

```csharp
        public const int KindBase = 1000;
        public const int RoleBase = 2000;
        public const int RaceBase = 3000;
        public const int ActivityBase = 4000;

        public static AtomTypeId Kind(EntityKind kind) => new AtomTypeId(KindBase + (int)kind);
        public static AtomTypeId Role(ResidentRole role) => new AtomTypeId(RoleBase + (int)role);
        public static AtomTypeId Race(int raceId) => new AtomTypeId(RaceBase + raceId);
        public static AtomTypeId Activity(ActivityKind kind) => new AtomTypeId(ActivityBase + (int)kind);
```
becomes:
```csharp
        public const int KindBase = 1000;
        public const int RoleBase = 2000;
        public const int RaceBase = 3000;
        public const int ActivityBase = 4000;

        public static AtomTypeId Kind(EntityKind kind) => AtomNames.From(kind).ToId();
        public static AtomTypeId Role(ResidentRole role) => AtomNames.From(role).ToId();
        public static AtomTypeId Race(int raceId) => AtomNames.FromRace(raceId).ToId();
        public static AtomTypeId Activity(ActivityKind kind) => AtomNames.From(kind).ToId();
```
Leave `TryActivity` exactly as-is — it short-circuits `ActivityKind.None` *before* calling `Activity`, so it never asks the helper for a None atom.

- [ ] **Step 2: Re-route `PlaceAtoms` bodies (keep `KindBase` for now)**

In `/home/uggeli/slopfall/Assets/Sim/Memory/PlaceAtoms.cs`:

```csharp
        public const int KindBase = 5000;
        public static AtomTypeId Kind(BuildingKind kind) => new AtomTypeId(KindBase + (int)kind);
        public static AtomTypeId Provisions => new AtomTypeId(6000);
        public static AtomTypeId Danger => new AtomTypeId(6001);
```
becomes:
```csharp
        public const int KindBase = 5000;
        public static AtomTypeId Kind(BuildingKind kind) => AtomNames.From(kind).ToId();
        public static AtomTypeId Provisions => AtomName.PlaceProvisions.ToId();
        public static AtomTypeId Danger => AtomName.PlaceDanger.ToId();
```

- [ ] **Step 3: Re-route `SomaticAtoms` fields**

In `/home/uggeli/slopfall/Assets/Sim/Memory/SomaticAtoms.cs`:

```csharp
        public static readonly AtomTypeId Hunger = new AtomTypeId(7000);
        public static readonly AtomTypeId Energy = new AtomTypeId(7001);   // tiredness deficit
        public static readonly AtomTypeId Fear   = new AtomTypeId(7002);
```
becomes:
```csharp
        public static readonly AtomTypeId Hunger = AtomName.SomaticHunger.ToId();
        public static readonly AtomTypeId Energy = AtomName.SomaticEnergy.ToId();   // tiredness deficit
        public static readonly AtomTypeId Fear   = AtomName.SomaticFear.ToId();
```

- [ ] **Step 4: Rewrite `PerceivableAtomsTests` to catalog semantics**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PerceivableAtomsTests.cs`, the test `Helpers_AreRangeCorrect_AndDistinct` (lines 12-15) asserts literal band ints. Replace those four assertions:

```csharp
        Assert.Equal(1000 + (int)EntityKind.CivilianNPC, PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
        Assert.Equal(2000 + (int)ResidentRole.Keeper, PerceivableAtoms.Role(ResidentRole.Keeper).Value);
        Assert.Equal(3000 + 7, PerceivableAtoms.Race(7).Value);
        Assert.Equal(4000 + (int)ActivityKind.Beg, PerceivableAtoms.Activity(ActivityKind.Beg).Value);
```
with catalog-semantic assertions (the helper output equals the named atom; the atom has the right category; the four are distinct):

```csharp
        // The helper stamps the named atom; identity is the AtomName, not a band offset.
        Assert.Equal(AtomName.Civilian.ToId(), PerceivableAtoms.Kind(EntityKind.CivilianNPC));
        Assert.Equal(AtomName.Keeper.ToId(),   PerceivableAtoms.Role(ResidentRole.Keeper));
        Assert.Equal(AtomName.RaceKhajiit.ToId(), PerceivableAtoms.Race(7));
        Assert.Equal(AtomName.ActBeg.ToId(),   PerceivableAtoms.Activity(ActivityKind.Beg));

        Assert.Equal(AtomCategory.Kind,     AtomCatalog.For(PerceivableAtoms.Kind(EntityKind.CivilianNPC)).Category);
        Assert.Equal(AtomCategory.Role,     AtomCatalog.For(PerceivableAtoms.Role(ResidentRole.Keeper)).Category);
        Assert.Equal(AtomCategory.Race,     AtomCatalog.For(PerceivableAtoms.Race(7)).Category);
        Assert.Equal(AtomCategory.Activity, AtomCatalog.For(PerceivableAtoms.Activity(ActivityKind.Beg)).Category);

        // Distinct identities.
        var ids = new[]
        {
            PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value,
            PerceivableAtoms.Role(ResidentRole.Keeper).Value,
            PerceivableAtoms.Race(7).Value,
            PerceivableAtoms.Activity(ActivityKind.Beg).Value,
        };
        Assert.Equal(ids.Length, new System.Collections.Generic.HashSet<int>(ids).Count);
```
Ensure the file's usings include `DaggerfallWorkshop.Sim.Memory` (it does, per the audit). If the test class name or other assertions reference `1000`/`2000`/etc. elsewhere, none were found — only these four lines.

- [ ] **Step 5: Rewrite `PlaceMemoryWriteTests` to catalog semantics**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/PlaceMemoryWriteTests.cs`, the test `PlaceAtoms_RangesDistinct` (lines 17-19) asserts literal band ints. Replace:

```csharp
        Assert.Equal(5000 + (int)BuildingKind.Tavern, PlaceAtoms.Kind(BuildingKind.Tavern).Value);
        Assert.Equal(6000, PlaceAtoms.Provisions.Value);
        Assert.Equal(6001, PlaceAtoms.Danger.Value);
```
with:
```csharp
        // Place atoms are named identities with the right category — distinct, no magic bands.
        Assert.Equal(AtomName.PlaceTavern.ToId(), PlaceAtoms.Kind(BuildingKind.Tavern));
        Assert.Equal(AtomCategory.PlaceKind,       AtomCatalog.For(PlaceAtoms.Kind(BuildingKind.Tavern)).Category);
        Assert.Equal(AtomCategory.PlaceProvisions, AtomCatalog.For(PlaceAtoms.Provisions).Category);
        Assert.Equal(AtomCategory.PlaceDanger,     AtomCatalog.For(PlaceAtoms.Danger).Category);
        Assert.NotEqual(PlaceAtoms.Provisions.Value, PlaceAtoms.Danger.Value);
        Assert.NotEqual(PlaceAtoms.Kind(BuildingKind.Tavern).Value, PlaceAtoms.Provisions.Value);
```

Then the delta-bag array assertion in `Observe_AccumulatesAtoms_OnBuildingRecord_ValueWins` (line 34):

```csharp
        Assert.Equal(new[] { 5000 + (int)BuildingKind.Tavern, 6001 }, rec.DeltaBag.Atoms.Select(a => a.Type.Value).OrderBy(x => x).ToArray());
```
Replace with an identity-based assertion (the bag holds exactly the two observed atoms, order-independent):

```csharp
        Assert.Equal(2, rec.DeltaBag.Count);
        Assert.True(rec.DeltaBag.Contains(PlaceAtoms.Kind(BuildingKind.Tavern)), "delta bag missing the tavern-kind atom");
        Assert.True(rec.DeltaBag.Contains(PlaceAtoms.Danger), "delta bag missing the danger atom");
```
(`AtomBag.Contains(AtomTypeId)` and `.Count` exist — verified in `AtomBag.cs`.)

- [ ] **Step 6: Fix the stale comment in `UtteranceWireTests`**

In `/home/uggeli/slopfall/Headless/Sim.MemoryTests/UtteranceWireTests.cs` line 92, the comment hard-codes the old id:

```csharp
                new Atom(PlaceAtoms.Danger, Fixed.One),                 // 6001 -> 1.0
```
Replace the comment so it no longer asserts a band int:
```csharp
                new Atom(PlaceAtoms.Danger, Fixed.One),                 // PlaceDanger -> 1.0
```

- [ ] **Step 7: Run the full suite**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`. (Ids are now dense; `AtomCatalogTests.For_AtomTypeId_ResolvesThroughTheHelpers` now exercises the dense path; every consumer reads the catalog.)

- [ ] **Step 8: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/PerceivableAtoms.cs Assets/Sim/Memory/PlaceAtoms.cs Assets/Sim/Memory/SomaticAtoms.cs \
        Headless/Sim.MemoryTests/PerceivableAtomsTests.cs Headless/Sim.MemoryTests/PlaceMemoryWriteTests.cs \
        Headless/Sim.MemoryTests/UtteranceWireTests.cs
git commit -m "$(cat <<'EOF'
refactor(atoms)!: stamp helpers emit dense AtomName ids (D1 stamp sites)

PerceivableAtoms/PlaceAtoms/SomaticAtoms now route enum -> AtomName -> id;
the seven stamp call-sites are untouched. Atom integers change (bands ->
dense); nothing persists them and every consumer reads the catalog, so
behaviour is preserved (soak gate in Task 11). Literal-int tests move to
catalog semantics. Band consts kept for one more task.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Drop the orphan `MemorySeeds`; add the recognition payoff test

**Files:**
- Delete: `/home/uggeli/slopfall/Assets/Sim/Memory/MemorySeeds.cs`
- Delete: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/MemorySeedsTests.cs`
- Create: `/home/uggeli/slopfall/Headless/Sim.MemoryTests/RecognitionPayoffTests.cs`

**Interfaces:**
- Consumes: existing `MeaningsStore(int capacity, MeaningsConfig)`, `MeaningsStore.AddNode(AtomBag, Fixed valence, Fixed arousal, bool innate)`, `MeaningsStore.Recognize(AtomBag) → CategoryId`, `CategoryId.None`, `MeaningsConfig.Default`; `PerceivableAtoms.Kind` (Task 8).

> `MemorySeeds` seeds abstract atoms 1-4 that perception never emits — the exact reason today's recognition is dead. It is referenced *only* by `MemorySeedsTests`. Delete both; its role (a real seed list) returns in Phase C on real `AtomName`s. The new payoff test is the proof Phase A delivered its reason: a category seeded on the *same* `AtomName` perception emits is recognized — impossible under the old 1-4 / 1000+ gap.

- [ ] **Step 1: Write the failing payoff test**

Create `/home/uggeli/slopfall/Headless/Sim.MemoryTests/RecognitionPayoffTests.cs`:

```csharp
using Xunit;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;

namespace Sim.MemoryTests
{
    /// <summary>
    /// Phase A's reason, made a test: a category seeded on the SAME atom perception emits is
    /// recognized. Under the old id-bands this was structurally impossible — seeds lived at ids
    /// 1-4 while perception emitted 1000+, so SignatureDistance never hit zero. With AtomName the
    /// seed and the percept share one key by construction.
    /// </summary>
    public class RecognitionPayoffTests
    {
        static AtomBag Sig(AtomTypeId atom) => AtomBag.Create(new[] { new Atom(atom, Fixed.One) });

        [Fact]
        public void Recognize_Fires_WhenSeededOnTheAtomPerceptionEmits()
        {
            var store = new MeaningsStore(8, MeaningsConfig.Default);

            // Seed a category whose prototype is the very atom a civilian broadcasts.
            AtomTypeId civilian = PerceivableAtoms.Kind(EntityKind.CivilianNPC);
            store.AddNode(Sig(civilian), Fixed.FromDouble(0.1), Fixed.FromDouble(0.9), true);

            // Perceiving that same atom recognizes the seeded category.
            Assert.NotEqual(CategoryId.None, store.Recognize(Sig(civilian)));
        }

        [Fact]
        public void Recognize_Misses_OnADifferentAtom()
        {
            var store = new MeaningsStore(8, MeaningsConfig.Default);
            store.AddNode(Sig(PerceivableAtoms.Kind(EntityKind.CivilianNPC)),
                          Fixed.FromDouble(0.1), Fixed.FromDouble(0.9), true);

            // A place-kind atom is far from the civilian prototype — no recognition.
            Assert.Equal(CategoryId.None, store.Recognize(Sig(PlaceAtoms.Kind(BuildingKind.Tavern))));
        }
    }
}
```

- [ ] **Step 2: Run it — passes already (the path is correct as of Task 8), and the second test guards distance**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~RecognitionPayoffTests"`
Expected: PASS. (If `Recognize_Misses_OnADifferentAtom` fails because the default `MatchThresholdRaw` is wide enough to match across distinct kinds, widen the separation — use two atoms in the prototype — or assert the *nearer* category wins; note the finding for the reviewer. The civilian-vs-tavern distance is large, so the miss is expected.)

- [ ] **Step 3: Delete the orphan and its test**

```bash
cd /home/uggeli/slopfall
git rm Assets/Sim/Memory/MemorySeeds.cs Headless/Sim.MemoryTests/MemorySeedsTests.cs
```

- [ ] **Step 4: Build + run the suite (confirm nothing referenced `MemorySeeds`)**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`, and no compile error about `MemorySeeds` (it was test-only). If the build complains of a missing reference, grep `grep -rn "MemorySeeds" /home/uggeli/slopfall/Assets /home/uggeli/slopfall/Headless` and remove the straggler.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Headless/Sim.MemoryTests/RecognitionPayoffTests.cs
git commit -m "$(cat <<'EOF'
feat(atoms): drop orphan MemorySeeds; prove recognition path (D5)

MemorySeeds seeded abstract ids 1-4 that perception never emits (the dead
seed). Delete it and MemorySeedsTests. RecognitionPayoffTests is Phase A's
reason as a test: a category seeded on the atom perception emits is now
recognized — impossible under the old 1-4 / 1000+ gap.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Delete the dead band constants; grep gate

**Files:**
- Modify: `/home/uggeli/slopfall/Assets/Sim/Memory/PerceivableAtoms.cs`
- Modify: `/home/uggeli/slopfall/Assets/Sim/Memory/PlaceAtoms.cs`

**Interfaces:**
- Produces: `PerceivableAtoms` and `PlaceAtoms` with no band-base constants.

> The validation gate from the spec: after migration the band `Base` constants exist only behind the helpers; **no `>=`/`<` band range-check survives outside the catalog.**

- [ ] **Step 1: Confirm the constants are now unreferenced**

Run:
```bash
grep -rn "KindBase\|RoleBase\|RaceBase\|ActivityBase" /home/uggeli/slopfall/Assets /home/uggeli/slopfall/Headless
```
Expected: hits **only** inside `PerceivableAtoms.cs` / `PlaceAtoms.cs` (the declarations themselves). If any other file still references them, that consumer/test was missed — migrate it to the catalog before deleting (do not delete a still-referenced constant).

- [ ] **Step 2: Delete the constants**

In `/home/uggeli/slopfall/Assets/Sim/Memory/PerceivableAtoms.cs`, delete these four lines:

```csharp
        public const int KindBase = 1000;
        public const int RoleBase = 2000;
        public const int RaceBase = 3000;
        public const int ActivityBase = 4000;
```
In `/home/uggeli/slopfall/Assets/Sim/Memory/PlaceAtoms.cs`, delete this line:

```csharp
        public const int KindBase = 5000;
```
Update the now-stale class doc-comments in both files that describe the "Base + (int)enum offset" / "id range 5000+" scheme, so the comment matches the AtomName-routed reality (e.g. PerceivableAtoms: "Maps each owning enum to its AtomName-identified AtomTypeId — see AtomNames"; PlaceAtoms: "PLACE atoms, identified by AtomName — see AtomNames").

- [ ] **Step 3: The grep gate — no band range-check survives outside the catalog**

Run:
```bash
grep -rn "ActivityBase\|KindBase\|RoleBase\|RaceBase\|>= 5000\|>= 7000\|< 5000\|>= 4000" \
  /home/uggeli/slopfall/Assets /home/uggeli/slopfall/Headless
```
Expected: **no hits.** (All band constants deleted; all range-checks migrated.) Any hit is a survivor to migrate.

- [ ] **Step 4: Build + run the suite**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`. Also build the host to be sure nothing else referenced the consts:
`cd /home/uggeli/slopfall/Headless && dotnet build --configuration Release` → `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/PerceivableAtoms.cs Assets/Sim/Memory/PlaceAtoms.cs
git commit -m "$(cat <<'EOF'
refactor(atoms): delete the id-band constants (grep gate clean)

KindBase/RoleBase/RaceBase/ActivityBase and PlaceAtoms.KindBase are gone;
no band range-check survives outside the catalog. The bands are no longer a
thing that can silently collide.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Soak regression gate — behaviour preserved

**Files:**
- Read: the two baseline files from Task 0.

**Interfaces:** none — this is the end-to-end behaviour-preservation gate.

> The refactor is behaviour-preserving. The soak is deterministic (seed 12345), so the post-refactor run must match the Task-0 baseline on the behavioural metrics (population trajectory, and the F1/F3 soak metrics: chain-count and social-cull-count). Atom-id changes must not show up as behaviour.

- [ ] **Step 1: Re-run both soaks on the refactored tree**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall Gallotale 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/after-gallotale.txt 2>&1
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/after-gothway.txt 2>&1
```

- [ ] **Step 2: Diff against the baseline**

```bash
cd /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad
diff baseline-gallotale.txt after-gallotale.txt && echo "GALLOTALE IDENTICAL"
diff baseline-gothway.txt  after-gothway.txt  && echo "GOTHWAY IDENTICAL"
```
Expected: both print `… IDENTICAL`. The soak output carries no raw atom ids in its metric lines (pop / hunger / coin / chains / culls), so a clean diff is the expected, correct result.

> **If the diff is non-empty:** do NOT tune. A behavioural delta means the refactor changed semantics — a bug. Use superpowers:systematic-debugging. Most likely causes, in order: (a) a `From`/catalog row transcribed wrong (compare the offending atom's `Category`/`Salience`/`Shareable` to the old band logic); (b) a consumer reading the catalog for an atom not in the reverse map (hits `Fallback` — check `AtomCatalog.NameOf` returns non-None for it); (c) the `TryActivity`/`Activity(None)` edge if some caller passes `ActivityKind.None` directly. The per-line `t=…` trace localizes the first diverging tick.

- [ ] **Step 3: Full green sweep (both test projects)**

Run:
```bash
cd /home/uggeli/slopfall/Headless
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release
dotnet test Sim.SpatialTests/Sim.SpatialTests.csproj -c Release
```
Expected: `Sim.MemoryTests` `Failed: 0` (now includes the new AtomNames/AtomCatalog/RecognitionPayoff tests, minus the deleted MemorySeedsTests); `Sim.SpatialTests` `Failed: 0` (13).

- [ ] **Step 4: Final commit (gate evidence)**

No code change — record the gate result in the body so the branch history carries the proof:

```bash
cd /home/uggeli/slopfall
git commit --allow-empty -m "$(cat <<'EOF'
test(atoms): soak gate — behaviour preserved across the refactor

Gallotale + Gothway 1-day soaks (seed 12345) diff IDENTICAL to the
pre-refactor baseline. Atom ids changed bands -> dense with zero
behavioural delta. Phase A complete.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage** (D1–D6 + validation):
- **D1** (flat enum; families flattened; source enums stay; `From` map; stamp ergonomics) → Tasks 1, 8. Race enumeration (D1a, closed roster) → Task 1 (`RaceRoster`, `FromRace`).
- **D2** (catalog: Category/Salience/Shareable, Tone reserved) → Task 2.
- **D3** (four band-as-logic consumers → catalog) → Tasks 3 (MemorySalience), 4 (Signature), 5 (activity extract), 6 (gossip gate).
- **D4** (`AffordanceCatalog.Public` stays — not a band consumer) → not touched (confirmed by the stamp-site audit); explicitly out of scope.
- **D5** (drop `MemorySeeds`) → Task 9.
- **D6** (migrate band-int tests) → Tasks 4, 5, 7, 8 (each test moves next to the change that needs it) + Task 9 (delete MemorySeedsTests).
- **Validation:** grep gate → Task 10; catalog completeness → Task 2; behaviour-preserving consumers → Tasks 3-6 (catalog pins the faces; each consumer's test shown equivalent on old ids first); the payoff (recognition fires) → Task 9; soak regression → Tasks 0 + 11.

**2. Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N". Every code step shows full code; every run step shows the command and expected output. The one conditional (Task 9 Step 2's threshold note) gives a concrete fallback, not a placeholder.

**3. Type consistency:** `AtomName.ToId()` (Task 1) used in Tasks 2/8/9. `AtomCatalog.For(AtomName)`, `For(AtomTypeId)`, `NameOf(AtomTypeId)`, `IsDefined` (Task 2) used consistently in Tasks 3-8. `AtomEntry.{Category, Salience, Shareable, Tone, IsIdentity}` and `AtomCategory.{Kind,Role,Race,Activity,Somatic,PlaceKind,PlaceProvisions,PlaceDanger}` consistent across Tasks 2-8. `AtomMeta(byte, MemoryFlags)`, `MemoryFlags.{None,Innate,Surprise}`, `Fixed.{Zero,FromDouble}`, `AtomBag.{Create,Contains,Count}`, `MeaningsStore.{AddNode,Recognize}`, `CategoryId.None`, `MeaningsConfig.Default` are all existing symbols used per the audit.

**Open flags for the reviewer / handoff:**
- The current Kind stamp (`TownLoader.cs:545`) only stamps `EntityKind.CivilianNPC` today; the other kind/race/role atoms are exercised by tests and the reverse map, not the live town. Behaviour-preservation is unaffected; the catalog is total for future producers.
- `AtomCatalog.For`/`NameOf` return a `Fallback`/`None` for an unknown int rather than throwing — defensive only; after Task 8 every stamped atom is in the reverse map (the `For_AtomTypeId_ResolvesThroughTheHelpers` test + the completeness test guarantee coverage).
