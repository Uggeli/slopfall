# Region POI Unified Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Place *every* region location (not just the 6 settled types) in the regional view behind a unified, per-POI model that records each location's purpose, with rendering as the first consumer and per-POI simulation as the future one.

**Architecture:** A new `RegionPoi` record (one per location) becomes the unified root held in a `PoiRegistry` on `SimWorld`. The region loader builds a `RegionPoi` for every location; settled-role POIs additionally get the **existing, unchanged** `SettlementData` linked into them (reparent-not-rewrite). The web boot positions and renders *all* POIs geographically; only POIs that own a settlement also remap agents. This pass is render-only — no simulation behavior for the new types.

**Tech Stack:** C# / .NET 8, the `Headless/` project set (`Sim.Core`, `Sim.World`, `Sim.Web`, `Sim.Host`, `Sim.Tests`), Daggerfall data via `Sim.Data` (`MapsFile`/`BlocksFile`). Build with `dotnet`. xUnit for tests.

## Global Constraints

- **`Sim.Core` must never reference the Daggerfall API enum `DFRegion.LocationTypes`.** Core is API-agnostic by design (`Assets/Sim/Registries/SettlementRegistry.cs:5-9`). All `LocationType`→semantic mapping lives in `Sim.World` (the project that references `Sim.Data`), exactly as `TownLoader.KindOf` already does. Core stores the raw location type as a plain `int`, never the enum.
- **The working settled economy must not change behavior.** `SettlementData`, `SettlementRegistry`, `TownLoader.LoadLocationInto`/`SeedSettlement`/`SeedStock`, and every settlement consumer keep their exact current behavior. Settlements are reached through their POI; they are not rewritten.
- **Render-only for new POIs.** No monsters, residents, employment, or economy for non-settled POIs in this pass.
- **Exclude `PlayerShip`** (`DFRegion.LocationTypes.HomeYourShips`) from load and render.
- Source layout: `Sim.Core` compiles `Assets/Sim/Engine/**`, `Assets/Sim/Registries/**`. `Sim.World` compiles `Assets/Sim/World/**`. New core types go under `Assets/Sim/Registries/`; the classifier goes under `Assets/Sim/World/`.
- Daggerfall data: probes/soaks need `arena2` data. Per project memory it lives at `/home/uggeli/df-data/arena2`; `SimBoot.DefaultArena2Path` resolves it (set `DAGGERFALL_ARENA2` if unset). Use a small **walled** region for checks (e.g. a region containing `Gallotale`) per existing testing guidance.
- Test-suite caveat: `Sim.Tests` is mid-rewrite; ~28 tests fail on master unrelated to this work. Run ONLY the new tests with a name filter; never gate on the full suite.

---

### Task 1: Core POI types + registry, wired into `SimWorld`

Introduces the unified data model in `Sim.Core` (no API enum) and exposes it on the world.

**Files:**
- Create: `Assets/Sim/Registries/PoiRegistry.cs`
- Modify: `Assets/Sim/Engine/SimWorld.cs` (declarations ~line 23, ctor ~line 71, registries array ~line 93)

**Interfaces:**
- Produces:
  - `enum PoiRole { None=0, City, Hamlet, Village, Farm, Temple, Tavern, Dungeon, Coven, CultShrine, Graveyard, ManorWealthy, HovelPoor, PlayerShip }` (namespace `DaggerfallWorkshop.Sim`)
  - `sealed class RegionPoi` with public mutable fields: `string Name; string RegionName; int RawLocationType; PoiRole Role; int MapPixelX, MapPixelY; float OriginX, OriginY, OriginZ; int BlocksWide, BlocksHigh; bool HasExterior; SettlementData Settlement;`
  - `sealed class PoiRegistry : Registry` with `RegionPoi Add(RegionPoi poi)`, `IReadOnlyList<RegionPoi> All`, `int Count`, no-op `Update`.
  - `static bool PoiRoles.IsSettled(PoiRole r)` → true for City/Hamlet/Village/Farm/Temple/Tavern.
  - `SimWorld.Pois` (public readonly `PoiRegistry`).

- [ ] **Step 1: Create the core types**

Create `Assets/Sim/Registries/PoiRegistry.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim
{
    /// Semantic role of any region location — the superset spanning settled places
    /// (which also carry a SettlementData) and the non-settled POIs that are rendered
    /// now and simulated later. Mapped from DFRegion.LocationTypes in Sim.World
    /// (PoiClassifier.PoiRoleOf), so core never references the API enum.
    ///
    /// Future per-POI simulation purpose (this pass renders only):
    ///   Dungeon      — threat/lair node: monster·bandit·undead source, danger to towns, loot/quests
    ///   Coven        — witch enclave: night activity, reagent/potion trade, recruits from population
    ///   CultShrine   — covert/heretical worship: cultists, rituals, clandestine recruitment
    ///   Graveyard    — undead emergence at night, necromancy, threat seepage into towns
    ///   ManorWealthy — isolated noble household: employs locals, trades, a family unit
    ///   HovelPoor    — isolated subsistence family / hermit
    ///   PlayerShip   — out of scope
    public enum PoiRole
    {
        None = 0,
        City, Hamlet, Village, Farm, Temple, Tavern,   // settled — carry a Settlement
        Dungeon, Coven, CultShrine, Graveyard,         // non-settled
        ManorWealthy, HovelPoor,
        PlayerShip,                                    // excluded from load/render
    }

    public static class PoiRoles
    {
        /// Settled roles carry a permanent population and own a SettlementData.
        public static bool IsSettled(PoiRole r) =>
            r == PoiRole.City || r == PoiRole.Hamlet || r == PoiRole.Village ||
            r == PoiRole.Farm || r == PoiRole.Temple || r == PoiRole.Tavern;
    }

    /// The unified root for ONE region location. Every location in a loaded region
    /// gets exactly one of these. Settled POIs also link the existing SettlementData
    /// (Settlement != null); non-settled POIs carry only role + geography and render
    /// their exterior (HasExterior) with no simulation yet. RawLocationType is the
    /// numeric DFRegion.LocationTypes value (core stays API-enum-free).
    /// Written only by the region loader (sim-thread, at load); read thereafter.
    public sealed class RegionPoi
    {
        public string Name;
        public string RegionName;
        public int RawLocationType;
        public PoiRole Role;

        public int MapPixelX, MapPixelY;       // region-map position
        public float OriginX, OriginY, OriginZ; // world-space render offset (filled by the web boot)
        public int BlocksWide, BlocksHigh;
        public bool HasExterior;                // false when the exterior has no RMB blocks

        public SettlementData Settlement;       // non-null only for settled POIs (the unchanged economy object)
    }

    /// Every POI in the loaded region, in load order. Seeded by the region loader
    /// (direct Add — load-time). Static after load; no runtime mutation, so Update is a no-op.
    public sealed class PoiRegistry : Registry
    {
        readonly List<RegionPoi> _all = new List<RegionPoi>();
        public PoiRegistry(EventBus events) : base(events) { }

        public RegionPoi Add(RegionPoi poi) { _all.Add(poi); return poi; }
        public IReadOnlyList<RegionPoi> All => _all;
        public int Count => _all.Count;

        public override void Update(long tick) { }
    }
}
```

- [ ] **Step 2: Wire `Pois` into `SimWorld`**

In `Assets/Sim/Engine/SimWorld.cs`, add the declaration after line 23 (`public readonly SettlementRegistry Settlements;`):

```csharp
        public readonly PoiRegistry Pois;
```

In the constructor, on the line that builds `Settlements` (line 71), append the construction:

```csharp
            Settlements = new SettlementRegistry(e); Buildings = new BuildingRegistry(e);
            Pois = new PoiRegistry(e);
```

In the `registries` array (line 90-98), add `Pois` to the list (e.g. right after `Settlements,`):

```csharp
                Settlements, Pois, Buildings, Position, Behavior, Intent, Identity, Needs, Vitals, Life,
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Headless/Sim.World/Sim.World.csproj`
Expected: Build succeeded (this transitively builds `Sim.Core`, which compiles the new file).

- [ ] **Step 4: Commit**

```bash
git add Assets/Sim/Registries/PoiRegistry.cs Assets/Sim/Engine/SimWorld.cs
git commit -m "Sim/: unified RegionPoi model + PoiRegistry on SimWorld"
```

---

### Task 2: POI classifier (`DFRegion.LocationTypes` → `PoiRole`)

The single API-enum→role mapping, living in `Sim.World` beside `TownLoader.KindOf`, with a pure table-driven test.

**Files:**
- Create: `Assets/Sim/World/PoiClassifier.cs`
- Test: `Headless/Sim.Tests/PoiClassifierTests.cs`

**Interfaces:**
- Consumes: `PoiRole`, `PoiRoles.IsSettled` (Task 1); `DFRegion.LocationTypes` (Sim.Data).
- Produces: `static PoiRole PoiClassifier.PoiRoleOf(DFRegion.LocationTypes t)` (namespace `DaggerfallWorkshop.Sim`).

- [ ] **Step 1: Write the failing test**

Create `Headless/Sim.Tests/PoiClassifierTests.cs`:

```csharp
using DaggerfallConnect;
using DaggerfallWorkshop.Sim;
using Xunit;

public class PoiClassifierTests
{
    [Theory]
    [InlineData(DFRegion.LocationTypes.TownCity, PoiRole.City)]
    [InlineData(DFRegion.LocationTypes.TownHamlet, PoiRole.Hamlet)]
    [InlineData(DFRegion.LocationTypes.TownVillage, PoiRole.Village)]
    [InlineData(DFRegion.LocationTypes.HomeFarms, PoiRole.Farm)]
    [InlineData(DFRegion.LocationTypes.ReligionTemple, PoiRole.Temple)]
    [InlineData(DFRegion.LocationTypes.Tavern, PoiRole.Tavern)]
    [InlineData(DFRegion.LocationTypes.DungeonLabyrinth, PoiRole.Dungeon)]
    [InlineData(DFRegion.LocationTypes.DungeonKeep, PoiRole.Dungeon)]
    [InlineData(DFRegion.LocationTypes.DungeonRuin, PoiRole.Dungeon)]
    [InlineData(DFRegion.LocationTypes.Coven, PoiRole.Coven)]
    [InlineData(DFRegion.LocationTypes.ReligionCult, PoiRole.CultShrine)]
    [InlineData(DFRegion.LocationTypes.Graveyard, PoiRole.Graveyard)]
    [InlineData(DFRegion.LocationTypes.HomeWealthy, PoiRole.ManorWealthy)]
    [InlineData(DFRegion.LocationTypes.HomePoor, PoiRole.HovelPoor)]
    [InlineData(DFRegion.LocationTypes.HomeYourShips, PoiRole.PlayerShip)]
    [InlineData(DFRegion.LocationTypes.None, PoiRole.None)]
    public void Maps_each_location_type_to_its_role(DFRegion.LocationTypes t, PoiRole expected)
        => Assert.Equal(expected, PoiClassifier.PoiRoleOf(t));

    [Fact]
    public void Settled_roles_match_the_six_simulated_types()
    {
        Assert.True(PoiRoles.IsSettled(PoiRole.City));
        Assert.True(PoiRoles.IsSettled(PoiRole.Tavern));
        Assert.False(PoiRoles.IsSettled(PoiRole.Dungeon));
        Assert.False(PoiRoles.IsSettled(PoiRole.None));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter FullyQualifiedName~PoiClassifier`
Expected: FAIL to build / `PoiClassifier` does not exist.

- [ ] **Step 3: Write the classifier**

Create `Assets/Sim/World/PoiClassifier.cs`:

```csharp
using DaggerfallConnect;

namespace DaggerfallWorkshop.Sim
{
    /// The single DFRegion.LocationTypes → PoiRole mapping. Lives in Sim.World (which
    /// references the Daggerfall API) so Sim.Core stays API-enum-free. The three dungeon
    /// types collapse to PoiRole.Dungeon; the dungeon sub-type stays available via
    /// RegionMapTable.DungeonType for later work.
    public static class PoiClassifier
    {
        public static PoiRole PoiRoleOf(DFRegion.LocationTypes t)
        {
            switch (t)
            {
                case DFRegion.LocationTypes.TownCity:         return PoiRole.City;
                case DFRegion.LocationTypes.TownHamlet:       return PoiRole.Hamlet;
                case DFRegion.LocationTypes.TownVillage:      return PoiRole.Village;
                case DFRegion.LocationTypes.HomeFarms:        return PoiRole.Farm;
                case DFRegion.LocationTypes.ReligionTemple:   return PoiRole.Temple;
                case DFRegion.LocationTypes.Tavern:           return PoiRole.Tavern;
                case DFRegion.LocationTypes.DungeonLabyrinth: return PoiRole.Dungeon;
                case DFRegion.LocationTypes.DungeonKeep:      return PoiRole.Dungeon;
                case DFRegion.LocationTypes.DungeonRuin:      return PoiRole.Dungeon;
                case DFRegion.LocationTypes.Coven:            return PoiRole.Coven;
                case DFRegion.LocationTypes.ReligionCult:     return PoiRole.CultShrine;
                case DFRegion.LocationTypes.Graveyard:        return PoiRole.Graveyard;
                case DFRegion.LocationTypes.HomeWealthy:      return PoiRole.ManorWealthy;
                case DFRegion.LocationTypes.HomePoor:         return PoiRole.HovelPoor;
                case DFRegion.LocationTypes.HomeYourShips:    return PoiRole.PlayerShip;
                default:                                      return PoiRole.None;
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter FullyQualifiedName~PoiClassifier`
Expected: PASS (17 cases + 1 fact).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/World/PoiClassifier.cs Headless/Sim.Tests/PoiClassifierTests.cs
git commit -m "Sim/: PoiClassifier maps every DF location type to a PoiRole (+ test)"
```

---

### Task 3: `RegionLoader` builds a POI for every location

Enumerate all locations into `world.Pois`; link the unchanged settlement build into settled-role POIs.

**Files:**
- Modify: `Assets/Sim/World/RegionLoader.cs` (the two-pass body, lines 44-115)

**Interfaces:**
- Consumes: `PoiRole`, `RegionPoi`, `PoiRoles.IsSettled`, `world.Pois.Add` (Task 1); `PoiClassifier.PoiRoleOf` (Task 2).
- Produces: after `LoadRegion`, `world.Pois.All` contains one `RegionPoi` per loaded location (except `PlayerShip`); each settled POI has `Settlement != null`; `RegionPoi.HasExterior` set; `MapPixelX/Y`, `BlocksWide/High`, `RawLocationType`, `Role` filled. `OriginX/Y/Z` left at 0 here (the web boot fills them). Existing `RegionLoadResult` counts (Settlements/Buildings/Civilians) unchanged.

- [ ] **Step 1: Replace Pass 1 (settled gather) to also build POIs**

In `Assets/Sim/World/RegionLoader.cs`, the current Pass 1 (lines 44-53) keeps only settled locations. Replace it so every loaded location becomes a `RegionPoi`, while still collecting the settled ones (with non-empty exteriors) into `locs` for the existing packing/seed path. Replace lines 44-53 with:

```csharp
            // Pass 1: build a POI for every loadable location; collect the settled,
            // non-empty ones (in region order) for the settlement seed below.
            var locs = new List<DFLocation>();
            foreach (var poiLoc in EnumerateLocations(maps, region, regionName))
            {
                var role = PoiClassifier.PoiRoleOf(poiLoc.MapTableData.LocationType);
                if (role == PoiRole.PlayerShip) continue;   // out of scope

                bool hasExterior = poiLoc.Exterior.ExteriorData.Width > 0 &&
                                   poiLoc.Exterior.ExteriorData.Height > 0;
                var pixPoi = MapsFile.LongitudeLatitudeToMapPixel(
                    poiLoc.MapTableData.Longitude, poiLoc.MapTableData.Latitude);

                world.Pois.Add(new RegionPoi
                {
                    Name = poiLoc.Name,
                    RegionName = regionName,
                    RawLocationType = (int)poiLoc.MapTableData.LocationType,
                    Role = role,
                    MapPixelX = pixPoi.X,
                    MapPixelY = pixPoi.Y,
                    BlocksWide = poiLoc.Exterior.ExteriorData.Width,
                    BlocksHigh = poiLoc.Exterior.ExteriorData.Height,
                    HasExterior = hasExterior,
                });

                if (PoiRoles.IsSettled(role) && hasExterior)
                    locs.Add(poiLoc);
            }
```

- [ ] **Step 2: Add the enumeration helper and link settlements into their POIs**

Still in `RegionLoader.cs`, add this private helper inside the class (e.g. just below the `BufferBlocks` const, replacing the now-unused `IsSettled` method — delete `IsSettled` at lines 30-37):

```csharp
        /// Every loadable location of a region, in region order.
        static IEnumerable<DFLocation> EnumerateLocations(MapsFile maps, DFRegion region, string regionName)
        {
            for (int i = 0; i < region.LocationCount; i++)
            {
                var loc = maps.GetLocation(regionName, region.MapNames[i]);
                if (loc.Loaded) yield return loc;
            }
        }
```

Then, in Pass 2 where the settlement row is created (line 94), link it back to its POI so the model stays unified. Replace line 94:

```csharp
                var s = world.Settlements.Add(loc.Name, regionName, TownLoader.KindOf(loc));
```

with:

```csharp
                var s = world.Settlements.Add(loc.Name, regionName, TownLoader.KindOf(loc));
                LinkSettlement(world, s);
```

and add this helper next to `EnumerateLocations`:

```csharp
        /// Attach a freshly-built settlement to its POI (matched by name+region).
        static void LinkSettlement(SimWorld world, SettlementData s)
        {
            foreach (var poi in world.Pois.All)
                if (poi.Settlement == null && poi.Name == s.Name && poi.RegionName == s.RegionName)
                { poi.Settlement = s; return; }
        }
```

Add `using System.Linq;` is NOT required (foreach used). Ensure `using System.Collections.Generic;` (already present at line 1).

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Headless/Sim.World/Sim.World.csproj`
Expected: Build succeeded. (`IsSettled` is gone; the loader now references `PoiClassifier`, `PoiRole`, `RegionPoi`.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Sim/World/RegionLoader.cs
git commit -m "Sim/: RegionLoader builds a RegionPoi per location, links settlements"
```

---

### Task 4: Web boot positions and renders all POIs

Geo-position every POI (not just settlements) and feed those with exteriors to the renderer; remap agents only for POIs that own a settlement.

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` (region boot block, lines 58-139)

**Interfaces:**
- Consumes: `world.Pois.All` (`RegionPoi` with `MapPixelX/Y`, `BlocksWide/High`, `HasExterior`, `Settlement`) from Task 3.
- Produces: `settlements` (the `List<(string, float, float, float)>` render list) now contains one entry per renderable POI; `rTowns` likewise; `remap` still contains only settlement-owning POIs. `RegionPoi.OriginX/Y/Z` are filled for rendered POIs. `/asset/town` output unchanged in shape.

- [ ] **Step 1: Compute the bbox over renderable POIs**

In `Headless/Sim.Web/Program.cs`, introduce the renderable-POI list and replace the settlement-only bbox loop (lines 86-95). Replace:

```csharp
    var sAll = world.Settlements.All;
    const int TerrainPad = 3;   // pixels of wilderness/sea to keep around the towns
    int mx0 = int.MaxValue, my0 = int.MaxValue, mx1 = int.MinValue, my1 = int.MinValue;
    foreach (var s in sAll)
    {
        if (s.MapPixelX < mx0) mx0 = s.MapPixelX;
        if (s.MapPixelX > mx1) mx1 = s.MapPixelX;
        if (s.MapPixelY < my0) my0 = s.MapPixelY;
        if (s.MapPixelY > my1) my1 = s.MapPixelY;
    }
```

with:

```csharp
    // Render every POI with an exterior — settled towns AND the non-settled POIs
    // (dungeons, covens, graveyards, isolated homes). Each lands at its true map
    // pixel; only settlement-owning POIs additionally remap agents (below).
    var pAll = world.Pois.All.Where(p => p.HasExterior).ToList();
    const int TerrainPad = 3;   // pixels of wilderness/sea to keep around the locations
    int mx0 = int.MaxValue, my0 = int.MaxValue, mx1 = int.MinValue, my1 = int.MinValue;
    foreach (var p in pAll)
    {
        if (p.MapPixelX < mx0) mx0 = p.MapPixelX;
        if (p.MapPixelX > mx1) mx1 = p.MapPixelX;
        if (p.MapPixelY < my0) my0 = p.MapPixelY;
        if (p.MapPixelY > my1) my1 = p.MapPixelY;
    }
```

- [ ] **Step 2: Build the terrain-pixel list from POIs**

Replace the `rTowns` assignment (line 103):

```csharp
    rTowns = sAll.Select(s => (s.MapPixelX, s.MapPixelY, s.Name, s.BlocksWide, s.BlocksHigh)).ToList();
```

with:

```csharp
    rTowns = pAll.Select(p => (p.MapPixelX, p.MapPixelY, p.Name, p.BlocksWide, p.BlocksHigh)).ToList();
```

(`rCentre`/`rDatum` selection below at lines 104-115 already operates on `rTowns`, so it now also considers non-settled POIs — correct: the datum is the location nearest the bbox centre regardless of type.)

- [ ] **Step 3: Place every POI; remap agents only for settlement-owning POIs**

Replace the placement loop (lines 121-138):

```csharp
    settlements = new List<(string, float, float, float)>();
    remap = new List<(float, float, float, float, float, float, float)>();
    foreach (var s in sAll)
    {
        var (cx, cz) = TownCentre(s.BlocksWide, s.BlocksHigh);
        // +X = east (MapPixelX grows east), +Z = north (MapPixelY grows south, so Z
        // counts down from the bbox's south edge my1). This matches DFU's native
        // terrain frame, so heights + autotiling render with no reflection.
        float geoX = (s.MapPixelX - mx0) * TileSize + cx;
        float geoZ = (my1 - s.MapPixelY) * TileSize + cz;
        float floor = assets.RegionTileFloor(region, s.Name, s.BlocksWide, s.BlocksHigh);
        float padY = (floor - rDatum) * TerrainTile.MaxTerrainHeight;
        settlements.Add((s.Name, geoX, padY, geoZ));
        remap.Add((s.OriginX, s.OriginZ,
                   s.OriginX + s.BlocksWide * BlockSide,
                   s.OriginZ + s.BlocksHigh * BlockSide,
                   geoX - s.OriginX, padY, geoZ - s.OriginZ));
    }
```

with:

```csharp
    settlements = new List<(string, float, float, float)>();
    remap = new List<(float, float, float, float, float, float, float)>();
    foreach (var p in pAll)
    {
        var (cx, cz) = TownCentre(p.BlocksWide, p.BlocksHigh);
        // +X = east (MapPixelX grows east), +Z = north (MapPixelY grows south, so Z
        // counts down from the bbox's south edge my1). This matches DFU's native
        // terrain frame, so heights + autotiling render with no reflection.
        float geoX = (p.MapPixelX - mx0) * TileSize + cx;
        float geoZ = (my1 - p.MapPixelY) * TileSize + cz;
        float floor = assets.RegionTileFloor(region, p.Name, p.BlocksWide, p.BlocksHigh);
        float padY = (floor - rDatum) * TerrainTile.MaxTerrainHeight;
        p.OriginX = geoX; p.OriginY = padY; p.OriginZ = geoZ;
        settlements.Add((p.Name, geoX, padY, geoZ));

        // Only settled POIs have agents to remap from the packed grid to geo space.
        var s = p.Settlement;
        if (s != null)
            remap.Add((s.OriginX, s.OriginZ,
                       s.OriginX + s.BlocksWide * BlockSide,
                       s.OriginZ + s.BlocksHigh * BlockSide,
                       geoX - s.OriginX, padY, geoZ - s.OriginZ));
    }
```

- [ ] **Step 4: Confirm `System.Linq` is in scope**

`pAll` uses `.Where`/`.Select`/`.ToList`. `Program.cs` already calls `.Select(...).ToList()` at the old line 103, so `using System.Linq;` is present — no change needed. If a build error reports `Where` not found, add `using System.Linq;` at the top.

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.Web/Program.cs
git commit -m "Sim/: region boot places + renders all POIs (agents remap only for settled)"
```

---

### Task 5: `--poicheck` probe + manual `town3d` verification

A deterministic loader probe (the reliable integration check given the stale suite) plus a visual confirm in the regional viewer.

**Files:**
- Create: `Headless/Sim.Host/PoiCheck.cs`
- Modify: `Headless/Sim.Host/Program.cs` (switch ~line 22-25; usage string ~line 74)

**Interfaces:**
- Consumes: `SimBoot.CreateRegion`, `world.Pois.All`, `world.Settlements.All` (Tasks 1, 3).
- Produces: `static int PoiCheck.Run(string region)` printing a per-role POI histogram and a settled-parity assertion.

- [ ] **Step 1: Write the probe**

Create `Headless/Sim.Host/PoiCheck.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Loads a whole region and reports the unified POI model: how many POIs of each
    /// role, how many have renderable exteriors, and a parity check that every settled
    /// POI links a SettlementData and the count matches the settlement registry.
    public static class PoiCheck
    {
        public static int Run(string region)
        {
            if (string.IsNullOrEmpty(region)) { Console.Error.WriteLine("usage: --poicheck <region>"); return 2; }

            var world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, 600f, 12345);
            var pois = world.Pois.All;

            Console.WriteLine($"region {region}: {pois.Count} POIs");
            foreach (var g in pois.GroupBy(p => p.Role).OrderBy(g => g.Key.ToString()))
            {
                int withExt = g.Count(p => p.HasExterior);
                int settled = g.Count(p => p.Settlement != null);
                Console.WriteLine($"  {g.Key,-13} count={g.Count(),-4} exterior={withExt,-4} settlement={settled}");
            }

            int settledPois = pois.Count(p => p.Settlement != null);
            int settledRolePois = pois.Count(p => PoiRoles.IsSettled(p.Role) && p.HasExterior);
            int registrySettlements = world.Settlements.All.Count;
            bool ok = settledPois == registrySettlements && settledPois == settledRolePois;

            Console.WriteLine($"parity: settledPois={settledPois} registry={registrySettlements} settledRole+ext={settledRolePois} => {(ok ? "OK" : "MISMATCH")}");
            return ok ? 0 : 1;
        }
    }
}
```

- [ ] **Step 2: Wire the command into the host**

In `Headless/Sim.Host/Program.cs`, add a case after the `--gatecheck` case (line 25-26):

```csharp
                case "--poicheck":
                    return DaggerfallWorkshop.Sim.Host.PoiCheck.Run(args.Length > 1 ? args[1] : null);
```

Update the usage string (line 74) to mention it:

```csharp
            "usage: --probe | --poicheck region | --enginedemo | --enginesmoke [ticks] | --engineworld R L [ticks] | --soak R L [days] | --soakregion R [days] | --gatecheck region location");
```

(Match the exact namespace used by `PoiCheck` — `DaggerfallWorkshop.Sim.Host`. If `Program.cs` is in a different namespace, adjust the qualified call or add a `using`.)

- [ ] **Step 3: Build and run the probe**

Run:
```bash
dotnet build Headless/Sim.Host/Sim.Host.csproj
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host/Sim.Host.csproj -- --poicheck <a-small-walled-region>
```
Expected: a per-role histogram that now includes non-settled roles (e.g. `Dungeon`, `Graveyard`, `HovelPoor`) with `count > 0` and `settlement=0`, the settled roles showing `settlement == count`, and the final line ending `=> OK`. Pick a region known to contain `Gallotale` for a light check.

- [ ] **Step 4: Manual `town3d` verification**

Run the web viewer in region mode and confirm visually:
```bash
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web/Sim.Web.csproj
```
Open `town3d.html` (region mode), and confirm non-settled POIs (dungeons/graveyards/isolated homes) now appear at plausible map-relative positions around the towns, and the existing towns still render and animate as before (no regression). Note: POIs whose exterior is empty contribute no geometry — that's expected (markers are a deferred follow-up).

- [ ] **Step 5: Commit**

```bash
git add Headless/Sim.Host/PoiCheck.cs Headless/Sim.Host/Program.cs
git commit -m "Sim/: --poicheck probe — per-role POI histogram + settled parity"
```

---

## Self-Review

**Spec coverage:**
- Unified `RegionPoi` root + `PoiRole` spine → Task 1. ✓
- Settlements reparented via nullable `Settlement`, economy untouched → Task 1 (model), Task 3 (`LinkSettlement`), Task 4 (remap guarded on `Settlement != null`). ✓
- `LocationType → PoiRole` single classification point → Task 2. ✓
- Loader enumerates all locations, attaches settlements → Task 3. ✓
- Geo-positioning over all POIs; region bounds grow; renderer unchanged → Task 4. ✓
- Render only `HasExterior`; empty-exterior POIs recorded not rendered → Task 4 (`Where(p => p.HasExterior)`), record created in Task 3. ✓
- Purpose taxonomy documented → Task 1 (XML doc on `PoiRole`). ✓
- Exclude `PlayerShip`; no interiors; no markers; no sim behavior → Tasks 1-4 (PlayerShip skipped in classifier+loader; render-only). ✓
- Testing: classifier unit test (Task 2), loader probe + parity (Task 5), manual viewer check (Task 5) — matches the spec's "targeted probe + manual `town3d`, not the full suite" caveat. ✓

**Spec deviation (noted):** the spec sketched `Settlements => Pois.Where(...)` as a computed property. The real `SettlementRegistry` is written during load via `Add` and participates in the event bus, so it stays as the economy's registry; the unified link runs the other way — each `RegionPoi.Settlement` references its `SettlementData`. This satisfies "POI is the unified root referencing settlements" without rewriting a working, event-wired registry. `RegionPoi.RawLocationType` is stored as `int` (not the enum) to honor the core/API boundary.

**Placeholder scan:** no TBD/TODO/"handle edge cases"; every code step shows complete code; commands have expected output.

**Type consistency:** `PoiRole`, `RegionPoi` (fields `Name/RegionName/RawLocationType/Role/MapPixelX/Y/OriginX/Y/Z/BlocksWide/High/HasExterior/Settlement`), `PoiRoles.IsSettled`, `PoiRegistry.Add/All/Count`, `world.Pois`, `PoiClassifier.PoiRoleOf` are referenced identically across Tasks 1-5. ✓
