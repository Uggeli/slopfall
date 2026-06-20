# Region Spatial Index — Stage 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stream a Daggerfall region's buildings/flats and agents by camera viewport, ending the "terrain streams but geometry/agents dump in full" asymmetry — via a map-pixel spatial index.

**Architecture:** A new sim-core registry `RegionGeographyRegistry` (`world.Geography`) holds the region's overworld layout (bbox, datum, packed→geo remap, POI-by-pixel buckets) and query API. In **Stage 1** the web host computes geo exactly as today and *seeds* the registry post-boot; consumers read it from the start. An `AssetExport` `RegionPlacementIndex` buckets RMB placements by map pixel, served per-tile via a new `/asset/towntile/{mx}/{my}` endpoint. The client streams structures on the existing terrain ring; the WebSocket gains a `view` message so agent snapshots filter to the camera ring.

**Tech Stack:** C# / .NET 10, Godot-free headless libs (`Sim.Core`, `Sim.World`, `Sim.AssetExport`, `Sim.Web`), xUnit 2.9, Three.js client (`town3d.html`).

Spec: `docs/superpowers/specs/2026-06-20-region-spatial-index-design.md`. This plan covers **Stage 1 only**; Stage 2 (relocating geo computation into `RegionLoader`) gets its own plan once Stage 1 is green.

## Global Constraints

- `GlobalScale = 0.025f` (`TownLayout.GlobalScale`). One map pixel = `32768 * GlobalScale = 819.2f` metres (`TileSize`); one RMB block = `4096 * GlobalScale = 102.4f` metres.
- Pixel key (every index): `PixelKey(mx, my) = ((long)mx << 32) | (uint)my`. Do **not** reuse `AssetService.TileKey` (it is `<<20`, a different scheme).
- Pixel-of-geo formula (must match `Program.cs:132-133` placement and `town3d.html:221-222` client inverse): `mx = mx0 + round(geoX / tileSize)`, `my = my1 − round(geoZ / tileSize)`.
- Sim core namespaces: registry types in `DaggerfallWorkshop.Sim.Engine`; `EventBus`, `Registry` live there too. AssetExport types in `Sim.AssetExport`.
- Registries are static-after-load where possible: `Update(long)` is a no-op, seeded once by a `Set`/`Seed` method (mirror `TownGridRegistry`).
- TDD: failing test → minimal impl → green → commit, per step. Tests use xUnit `[Fact]`.
- Arena2-dependent tests no-op silently when `DAGGERFALL_ARENA2` (or the default path) is absent — mirror `Arena2DataTests` (`static bool Available => Directory.Exists(Arena2)`).
- Back-compat: town mode (single settlement) behavior is untouched throughout.

---

## File Structure

- **Create** `Assets/Sim/Engine/Units/RegionGeographyRegistry.cs` — sim-core spatial index registry (Task 1).
- **Modify** `Assets/Sim/Engine/SimWorld.cs` — declare/construct/register `Geography` (Task 1).
- **Create** `Headless/Sim.Tests/RegionGeographyTests.cs` — pure unit tests (Task 1).
- **Create** `Headless/Sim.AssetExport/RegionPlacementIndex.cs` — geometry partition (Task 2).
- **Modify** `Headless/Sim.AssetExport/AssetService.cs` — `GetRegionIndex` (Task 2).
- **Modify** `Headless/Sim.Tests/Sim.Tests.csproj` — add `Sim.AssetExport` reference (Task 2).
- **Create** `Headless/Sim.Tests/RegionPlacementIndexTests.cs` — pure conservation unit test (Task 2).
- **Modify** `Headless/Sim.Web/Program.cs` — seed `Geography`, build index, `/asset/towntile`, `/asset/town` region-empty, WS view filter (Tasks 3, 4).
- **Create** `Headless/Sim.Tests/RegionStreamingSmokeTests.cs` — env-gated conservation integration (Task 3).
- **Modify** `Headless/Sim.Web/wwwroot/town3d.html` — `streamRegionStructures`, eviction, `view` send (Task 5).

---

## Task 1: `RegionGeographyRegistry` (sim-core spatial index)

**Files:**
- Create: `Assets/Sim/Engine/Units/RegionGeographyRegistry.cs`
- Modify: `Assets/Sim/Engine/SimWorld.cs:24` (field), `:73` (construction), `:92-101` (registries array)
- Test: `Headless/Sim.Tests/RegionGeographyTests.cs`

**Interfaces:**
- Produces:
  - `struct GeoRemapEntry { float MinX, MinZ, MaxX, MaxZ, Dx, Dy, Dz; }`
  - `class RegionGeographyRegistry : Registry`
    - `void Seed(int mx0, int my0, int mx1, int my1, float tileSize, float datum, IEnumerable<(int mx,int my,int poiId)> poiPixels, IEnumerable<GeoRemapEntry> remap)`
    - `bool HasRegion { get; }`
    - `int Mx0/My0/Mx1/My1 { get; }`, `float TileSize { get; }`, `float Datum { get; }`
    - `static long PixelKey(int mx, int my)`
    - `IReadOnlyList<int> PoisAt(int mx, int my)`
    - `List<int> RingPois(int mx, int my, int r)`
    - `(int mx, int my) PixelOf(float geoX, float geoZ)`
    - `(float x, float y, float z) GeoRemap(float x, float z)`
  - On `SimWorld`: `public readonly RegionGeographyRegistry Geography;`

- [ ] **Step 1: Write the failing test**

Create `Headless/Sim.Tests/RegionGeographyTests.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.Tests
{
    public class RegionGeographyTests
    {
        static RegionGeographyRegistry Seeded()
        {
            var g = new RegionGeographyRegistry(new EventBus());
            // bbox mx 10..13, my 20..23; tile 819.2; datum 0.
            // Two POIs: id 0 at pixel (11,21), id 1 at pixel (12,21).
            g.Seed(10, 20, 13, 23, 819.2f, 0f,
                new[] { (11, 21, 0), (12, 21, 1) },
                new[]
                {
                    // settlement 0 packed rect [0,200)x[0,200) → geo delta (+1000,+5,+2000)
                    new GeoRemapEntry { MinX = 0, MinZ = 0, MaxX = 200, MaxZ = 200,
                                        Dx = 1000, Dy = 5, Dz = 2000 },
                });
            return g;
        }

        [Fact]
        public void PixelOf_InvertsThePlacementFormula()
        {
            var g = Seeded();
            // geoX = (mx-mx0)*ts, geoZ = (my1-my)*ts  →  pixel (12,21)
            float ts = 819.2f;
            float geoX = (12 - 10) * ts, geoZ = (23 - 21) * ts;
            Assert.Equal((12, 21), g.PixelOf(geoX, geoZ));
        }

        [Fact]
        public void PoisAt_ReturnsPoisInThatPixel()
        {
            var g = Seeded();
            Assert.Equal(new[] { 1 }, g.PoisAt(12, 21));
            Assert.Empty(g.PoisAt(13, 23));
        }

        [Fact]
        public void RingPois_IsBoundaryInclusive()
        {
            var g = Seeded();
            // r=1 around (11,21) includes pixel (12,21) → poi 1, and (11,21) → poi 0.
            var ring = g.RingPois(11, 21, 1);
            Assert.Contains(0, ring);
            Assert.Contains(1, ring);
            // r=0 around (12,21) excludes poi 0.
            Assert.DoesNotContain(0, g.RingPois(12, 21, 0));
        }

        [Fact]
        public void GeoRemap_ShiftsPointInsideSettlementRectElseIdentity()
        {
            var g = Seeded();
            Assert.Equal((1100f, 5f, 2050f), g.GeoRemap(100f, 50f));   // inside rect 0
            Assert.Equal((900f, 0f, 900f), g.GeoRemap(900f, 900f));   // outside all rects
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter RegionGeographyTests`
Expected: FAIL — `RegionGeographyRegistry` / `GeoRemapEntry` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `Assets/Sim/Engine/Units/RegionGeographyRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// One settlement's packed walkability rectangle and the delta that shifts the
    /// agents inside it into geographic world space (the projection the snapshot
    /// stream applies). Mirrors the tuple the web host builds today.
    public struct GeoRemapEntry { public float MinX, MinZ, MaxX, MaxZ, Dx, Dy, Dz; }

    /// The region's overworld layout as sim truth: map-pixel bbox, the shared
    /// terrain datum, the packed→geo agent remap, and a map-pixel → POI-id spatial
    /// index. Seeded once per region boot (Stage 1: by the web host; Stage 2: by
    /// RegionLoader). Empty/absent in town mode. Static after seed, so Update() is
    /// a no-op — mirrors TownGridRegistry.
    public sealed class RegionGeographyRegistry : Registry
    {
        readonly Dictionary<long, List<int>> _poiByPixel = new();
        readonly List<GeoRemapEntry> _remap = new();
        static readonly IReadOnlyList<int> Empty = new int[0];

        public RegionGeographyRegistry(EventBus events) : base(events) { }

        public bool HasRegion { get; private set; }
        public int Mx0 { get; private set; }
        public int My0 { get; private set; }
        public int Mx1 { get; private set; }
        public int My1 { get; private set; }
        public float TileSize { get; private set; }
        public float Datum { get; private set; }

        public static long PixelKey(int mx, int my) => ((long)mx << 32) | (uint)my;

        public void Seed(int mx0, int my0, int mx1, int my1, float tileSize, float datum,
            IEnumerable<(int mx, int my, int poiId)> poiPixels, IEnumerable<GeoRemapEntry> remap)
        {
            Mx0 = mx0; My0 = my0; Mx1 = mx1; My1 = my1; TileSize = tileSize; Datum = datum;
            _poiByPixel.Clear(); _remap.Clear();
            foreach (var (mx, my, poiId) in poiPixels)
            {
                long key = PixelKey(mx, my);
                if (!_poiByPixel.TryGetValue(key, out var list))
                    _poiByPixel[key] = list = new List<int>();
                list.Add(poiId);
            }
            _remap.AddRange(remap);
            HasRegion = true;
        }

        public IReadOnlyList<int> PoisAt(int mx, int my)
            => _poiByPixel.TryGetValue(PixelKey(mx, my), out var list) ? list : Empty;

        public List<int> RingPois(int mx, int my, int r)
        {
            var hit = new List<int>();
            for (int x = mx - r; x <= mx + r; x++)
                for (int y = my - r; y <= my + r; y++)
                    if (_poiByPixel.TryGetValue(PixelKey(x, y), out var list))
                        hit.AddRange(list);
            return hit;
        }

        public (int mx, int my) PixelOf(float geoX, float geoZ)
            => (Mx0 + (int)MathF.Round(geoX / TileSize),
                My1 - (int)MathF.Round(geoZ / TileSize));

        public (float x, float y, float z) GeoRemap(float x, float z)
        {
            foreach (var r in _remap)
                if (x >= r.MinX && x < r.MaxX && z >= r.MinZ && z < r.MaxZ)
                    return (x + r.Dx, r.Dy, z + r.Dz);
            return (x, 0f, z);
        }

        public override void Update(long tick) { }   // static after seed
    }
}
```

Wire into `Assets/Sim/Engine/SimWorld.cs`:
- After line 24 (`public readonly PoiRegistry Pois;`) add:
```csharp
        public readonly RegionGeographyRegistry Geography;
```
- In the constructor, on the line that builds `Pois` (line 73), append the construction:
```csharp
            Geography = new RegionGeographyRegistry(e);
```
- In the `registries` array (line 93-100), add `Geography` to the list (e.g. after `Pois`):
```csharp
                Settlements, Pois, Geography, Flora, Buildings, Position, Behavior, Intent, Identity, Needs, Vitals, Life,
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter RegionGeographyTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Engine/Units/RegionGeographyRegistry.cs Assets/Sim/Engine/SimWorld.cs Headless/Sim.Tests/RegionGeographyTests.cs
git commit -m "Sim core: RegionGeography registry (map-pixel spatial index)"
```

---

## Task 2: `RegionPlacementIndex` (AssetExport geometry partition)

**Files:**
- Create: `Headless/Sim.AssetExport/RegionPlacementIndex.cs`
- Modify: `Headless/Sim.AssetExport/AssetService.cs:101-106` (add `GetRegionIndex`)
- Modify: `Headless/Sim.Tests/Sim.Tests.csproj` (add `Sim.AssetExport` ProjectReference)
- Test: `Headless/Sim.Tests/RegionPlacementIndexTests.cs`

**Interfaces:**
- Consumes: `TownLayout.TownData`, `TownLayout.Placement { uint ModelId; float[] Matrix; }`, `TownLayout.Flat { int Archive, Record; float X, Y, Z, WorldW, WorldH; }`.
- Produces:
  - `class RegionTile { List<TownLayout.Placement> Placements; List<TownLayout.Flat> Flats; List<uint> ModelIds; }`
  - `class RegionPlacementIndex` with:
    - `static RegionPlacementIndex Build(TownLayout.TownData region, int mx0, int my1, float tileSize)`
    - `RegionTile At(int mx, int my)` (returns an empty tile, never null)
    - `IEnumerable<(long key, RegionTile tile)> AllTiles()`
  - On `AssetService`: `RegionPlacementIndex GetRegionIndex(string region, IReadOnlyList<(string,float,float,float)> settlements, int mx0, int my1, float tileSize)`

- [ ] **Step 1: Add the AssetExport project reference to the test project**

In `Headless/Sim.Tests/Sim.Tests.csproj`, inside the existing `<ItemGroup>` of `ProjectReference`s, add:
```xml
    <ProjectReference Include="..\Sim.AssetExport\Sim.AssetExport.csproj" />
```

- [ ] **Step 2: Write the failing test**

Create `Headless/Sim.Tests/RegionPlacementIndexTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Sim.AssetExport;
using Xunit;

namespace Sim.Tests
{
    public class RegionPlacementIndexTests
    {
        // A placement whose world translation sits at (geoX, _, geoZ).
        static TownLayout.Placement P(uint model, float geoX, float geoZ)
        {
            var m = new float[16];
            m[0] = m[5] = m[10] = m[15] = 1f;   // identity rotation/scale
            m[12] = geoX; m[13] = 0f; m[14] = geoZ;   // column-major translation
            return new TownLayout.Placement { ModelId = model, Matrix = m };
        }

        static TownLayout.TownData Region(float ts)
        {
            var d = new TownLayout.TownData();
            // bbox mx0=10, my1=23. pixel(mx,my): geoX=(mx-10)*ts, geoZ=(23-my)*ts.
            d.Placements.Add(P(100, 0 * ts, 2 * ts));        // pixel (10,21)
            d.Placements.Add(P(100, 0 * ts, 2 * ts));        // pixel (10,21), same model
            d.Placements.Add(P(200, 3 * ts, 0 * ts));        // pixel (13,23)
            // a "city" placement spilling one pixel east of (10,21):
            d.Placements.Add(P(300, 1 * ts, 2 * ts));        // pixel (11,21)
            d.Flats.Add(new TownLayout.Flat { Archive = 504, Record = 1, X = 0 * ts, Z = 2 * ts });
            return d;
        }

        [Fact]
        public void Build_ConservesEveryPlacement()
        {
            float ts = 819.2f;
            var region = Region(ts);
            var idx = RegionPlacementIndex.Build(region, 10, 23, ts);

            int total = idx.AllTiles().Sum(t => t.tile.Placements.Count);
            Assert.Equal(region.Placements.Count, total);   // none lost, none duplicated
        }

        [Fact]
        public void Build_BucketsByPixelAndDedupesModelIds()
        {
            float ts = 819.2f;
            var idx = RegionPlacementIndex.Build(Region(ts), 10, 23, ts);

            var t1021 = idx.At(10, 21);
            Assert.Equal(2, t1021.Placements.Count);          // the two model-100 placements
            Assert.Equal(new uint[] { 100 }, t1021.ModelIds); // deduped within the tile
            Assert.Single(idx.At(13, 23).Placements);         // model 200
            Assert.Single(idx.At(11, 21).Placements);         // the spill placement (own pixel)
            Assert.Empty(idx.At(99, 99).Placements);          // empty pixel → empty tile, not null
        }

        [Fact]
        public void Build_BucketsFlatsByPixel()
        {
            float ts = 819.2f;
            var idx = RegionPlacementIndex.Build(Region(ts), 10, 23, ts);
            Assert.Single(idx.At(10, 21).Flats);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter RegionPlacementIndexTests`
Expected: FAIL — `RegionPlacementIndex` does not exist (compile error).

- [ ] **Step 4: Write minimal implementation**

Create `Headless/Sim.AssetExport/RegionPlacementIndex.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Sim.AssetExport
{
    /// One map pixel's render geometry: the model placements + decorative flats whose
    /// world position falls in this pixel, plus the unique model ids the client must
    /// fetch for this tile (deduped within the tile; the browser dedupes globally).
    public sealed class RegionTile
    {
        public List<TownLayout.Placement> Placements = new();
        public List<TownLayout.Flat> Flats = new();
        public List<uint> ModelIds = new();
    }

    /// Partitions a whole-region placement list (TownLayout.ResolveRegion output) into
    /// per-map-pixel buckets so the viewer can stream geometry on the same pixel ring
    /// terrain already uses. A placement is bucketed by its world translation, so a
    /// city spanning several pixels splits across the correct buckets. Static after
    /// build (placements never move).
    public sealed class RegionPlacementIndex
    {
        readonly Dictionary<long, RegionTile> _tiles = new();
        static readonly RegionTile EmptyTile = new();

        public static long PixelKey(int mx, int my) => ((long)mx << 32) | (uint)my;

        static (int mx, int my) PixelOf(float geoX, float geoZ, int mx0, int my1, float ts)
            => (mx0 + (int)MathF.Round(geoX / ts), my1 - (int)MathF.Round(geoZ / ts));

        public static RegionPlacementIndex Build(TownLayout.TownData region, int mx0, int my1, float tileSize)
        {
            var idx = new RegionPlacementIndex();
            var seenModel = new Dictionary<long, HashSet<uint>>();

            foreach (var p in region.Placements)
            {
                // Column-major 4x4: translation is at [12]=x, [14]=z.
                var (mx, my) = PixelOf(p.Matrix[12], p.Matrix[14], mx0, my1, tileSize);
                var tile = idx.GetOrAdd(mx, my, seenModel, out var seen);
                tile.Placements.Add(p);
                if (seen.Add(p.ModelId)) tile.ModelIds.Add(p.ModelId);
            }
            foreach (var f in region.Flats)
            {
                var (mx, my) = PixelOf(f.X, f.Z, mx0, my1, tileSize);
                idx.GetOrAdd(mx, my, seenModel, out _).Flats.Add(f);
            }
            return idx;
        }

        RegionTile GetOrAdd(int mx, int my, Dictionary<long, HashSet<uint>> seenModel, out HashSet<uint> seen)
        {
            long key = PixelKey(mx, my);
            if (!_tiles.TryGetValue(key, out var tile))
            {
                _tiles[key] = tile = new RegionTile();
                seenModel[key] = new HashSet<uint>();
            }
            seen = seenModel[key];
            return tile;
        }

        public RegionTile At(int mx, int my)
            => _tiles.TryGetValue(PixelKey(mx, my), out var t) ? t : EmptyTile;

        public IEnumerable<(long key, RegionTile tile)> AllTiles()
        {
            foreach (var kv in _tiles) yield return (kv.Key, kv.Value);
        }
    }
}
```

Add to `Headless/Sim.AssetExport/AssetService.cs` after `GetRegion` (line 106):

```csharp
        /// <summary>Region layout partitioned into per-map-pixel buckets so the viewer
        /// streams geometry on the terrain ring instead of loading every settlement.</summary>
        public RegionPlacementIndex GetRegionIndex(string region,
            IReadOnlyList<(string name, float ox, float oy, float oz)> settlements,
            int mx0, int my1, float tileSize)
        {
            lock (_gate)
            {
                var data = TownLayout.ResolveRegion(_arena2, region, settlements);
                return RegionPlacementIndex.Build(data, mx0, my1, tileSize);
            }
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter RegionPlacementIndexTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.AssetExport/RegionPlacementIndex.cs Headless/Sim.AssetExport/AssetService.cs Headless/Sim.Tests/Sim.Tests.csproj Headless/Sim.Tests/RegionPlacementIndexTests.cs
git commit -m "AssetExport: RegionPlacementIndex — partition region geometry by map pixel"
```

---

## Task 3: Server wiring — seed `Geography`, build index, `/asset/towntile`, `/asset/town` region-empty

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` (region boot block `:102-146`; endpoints `:260-301`)
- Test: `Headless/Sim.Tests/RegionStreamingSmokeTests.cs`

**Interfaces:**
- Consumes: `world.Geography.Seed(...)` (Task 1), `assets.GetRegionIndex(...)` and `RegionPlacementIndex.At` / `AllTiles` (Task 2).
- Produces: a module-level `RegionPlacementIndex regionIndex` field readable by the endpoint; `GET /asset/towntile/{mx}/{my}` returning `RegionTile`.

- [ ] **Step 1: Write the failing integration smoke test**

Create `Headless/Sim.Tests/RegionStreamingSmokeTests.cs`. This asserts conservation at the AssetExport layer end-to-end against real data (env-gated):

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Sim.AssetExport;
using Xunit;

namespace Sim.Tests
{
    public class RegionStreamingSmokeTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        const float TileSize = 32768f * TownLayout.GlobalScale;   // 819.2 m
        const float BlockSide = 4096f * TownLayout.GlobalScale;   // 102.4 m

        static (float x, float z) TownCentre(int w, int h)
        {
            int tx = (128 - w * 16) / 2, ty = (128 - h * 16) / 2;
            return (tx / 16f * BlockSide, ty / 16f * BlockSide);
        }

        [Fact]
        public void StreamedTiles_ConserveTheFullRegionPlacementSet()
        {
            if (!Available) return;
            const string region = "Isle of Balfiera";   // Betony-sized; adjust if absent

            var world = SimBoot.CreateRegion(Arena2, region, 0f);
            var assets = new AssetService(Arena2);

            var pAll = world.Pois.All.Where(p => p.HasExterior).ToList();
            if (pAll.Count == 0) return;

            int mx0 = pAll.Min(p => p.MapPixelX) - 3, my0 = pAll.Min(p => p.MapPixelY) - 3;
            int mx1 = pAll.Max(p => p.MapPixelX) + 3, my1 = pAll.Max(p => p.MapPixelY) + 3;
            mx0 = Math.Max(0, mx0); my0 = Math.Max(0, my0);
            mx1 = Math.Min(999, mx1); my1 = Math.Min(499, my1);

            var settlements = pAll.Select(p =>
            {
                var (cx, cz) = TownCentre(p.BlocksWide, p.BlocksHigh);
                return (p.Name, (p.MapPixelX - mx0) * TileSize + cx, 0f, (my1 - p.MapPixelY) * TileSize + cz);
            }).ToList();

            var full = assets.GetRegion(region, settlements);
            var idx = assets.GetRegionIndex(region, settlements, mx0, my1, TileSize);

            int streamed = idx.AllTiles().Sum(t => t.tile.Placements.Count);
            Assert.Equal(full.Placements.Count, streamed);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails (or no-ops)**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter RegionStreamingSmokeTests`
Expected: PASS already if Task 2 is correct and data is present (this guards conservation; if data absent it silently no-ops). If it FAILS on a region-name lookup, switch `region` to one present in the data (e.g. `"Betony"`). This test gates the endpoint wiring below.

- [ ] **Step 3: Build the index + seed Geography at boot**

In `Headless/Sim.Web/Program.cs`, at the end of the region-boot block (after the `foreach (var p in pAll)` loop that fills `settlements`/`remap`, around line 146), add:

```csharp
    // Seed the sim's spatial index (Stage 1: web host computes geo, then seeds;
    // Stage 2 moves the computation into RegionLoader and this call moves with it).
    world.Geography.Seed(mx0, my0, mx1, my1, TileSize, rDatum,
        pAll.Select((p, i) => (p.MapPixelX, p.MapPixelY, i)),
        remap.Select(r => new GeoRemapEntry
        {
            MinX = r.Item1, MinZ = r.Item2, MaxX = r.Item3, MaxZ = r.Item4,
            Dx = r.Item5, Dy = r.Item6, Dz = r.Item7,
        }));

    // Partition the region's geometry by map pixel for per-tile streaming.
    regionIndex = assets.GetRegionIndex(region, settlements, mx0, my1, TileSize);
```

Declare the module-level field near the other region fields (e.g. beside `rTowns`, before the `app.MapGet` block — top-level statement scope). Add:

```csharp
Sim.AssetExport.RegionPlacementIndex regionIndex = null;
```

Add `using DaggerfallWorkshop.Sim.Engine;` if not already present (it is — `Program.cs` uses sim types).

- [ ] **Step 4: Add the `/asset/towntile` endpoint and empty `/asset/town` in region mode**

In `Headless/Sim.Web/Program.cs`, change the `/asset/town` handler (line 260-265) so region mode returns an empty layout (mirroring `/asset/terrain`):

```csharp
app.MapGet("/asset/town", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    if (wholeRegion)
        return Results.Json(new TownLayout.TownData { Region = region, Location = worldName }, jsonOptions);
    return Results.Json(assets.GetTown(region, location), jsonOptions);
});
```

Add the streaming endpoint after `/asset/terraintile` (after line 301):

```csharp
// One map pixel's render geometry (render plane 1, streamed). The client requests
// the pixels its camera ring covers and caches them. Empty/out-of-bbox pixels return
// 200 with empty arrays so the client caches "nothing here" and never re-asks.
app.MapGet("/asset/towntile/{mx:int}/{my:int}", (HttpContext ctx, int mx, int my) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    var tile = regionIndex?.At(mx, my) ?? new Sim.AssetExport.RegionTile();
    return Results.Json(tile, jsonOptions);
});
```

- [ ] **Step 5: Build and run the smoke test again; build the web project**

Run:
```bash
dotnet build Headless/Sim.Web/Sim.Web.csproj
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter RegionStreamingSmokeTests
```
Expected: web project builds clean; smoke test PASSes (or no-ops if data absent).

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.Web/Program.cs Headless/Sim.Tests/RegionStreamingSmokeTests.cs
git commit -m "Sim.Web: seed Geography + serve region geometry per map pixel (/asset/towntile)"
```

---

## Task 4: WebSocket view filter — agent snapshots to the camera ring

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` (WS handler `:372-464`)

**Interfaces:**
- Consumes: `world.Geography.PixelOf(...)` (Task 1), the existing `GeoRemap(...)` local helper (`:363`).
- Produces: per-connection `view` state; the snapshot `entities` list filtered to the ring when a `view` is known.

- [ ] **Step 1: Add per-connection view state and parse the `view` message**

In the `/ws` handler, before the snapshot `pump` Task (around line 392), add mutable per-connection state:

```csharp
    // Camera ring the client last reported (region mode). -1 ⇒ unknown: send all
    // agents (back-compat, and town mode never sends a view).
    int viewMx = -1, viewMy = -1, viewR = 0;
```

In the receive-loop `switch` (after the `speed` case, line 457), add:

```csharp
                case "view"
                    when doc.RootElement.TryGetProperty("mx", out var vmx)
                      && doc.RootElement.TryGetProperty("my", out var vmy):
                    viewMx = vmx.GetInt32();
                    viewMy = vmy.GetInt32();
                    viewR = doc.RootElement.TryGetProperty("r", out var vr) ? vr.GetInt32() : 6;
                    break;
```

- [ ] **Step 2: Filter the snapshot to the ring**

In the pump, the snapshot currently does `entities = snap.Agents.Select(...)` (line 416). Replace the `entities = ...` assignment with a filtered projection that drops agents outside the ring when a view is known:

```csharp
                    entities = snap.Agents.Select(e =>
                    {
                        var (ex, ey, ez) = wholeRegion ? GeoRemap(e.X, e.Z) : (e.X, 0f, e.Z);
                        return (e, ex, ey, ez);
                    })
                    .Where(t =>
                    {
                        if (!wholeRegion || viewMx < 0) return true;   // send-all back-compat
                        var (pmx, pmy) = world.Geography.PixelOf(t.ex, t.ez);
                        return Math.Abs(pmx - viewMx) <= viewR && Math.Abs(pmy - viewMy) <= viewR;
                    })
                    .Select(t => new object[]
                        { t.e.Id, MathF.Round(t.ex, 1), MathF.Round(t.ez, 1), t.e.Activity, t.e.Phase,
                          MathF.Round(t.e.Yaw, 3), t.e.Kind, MathF.Round(t.ey, 1) }),
```

Note: `viewMx`/`viewMy`/`viewR` are captured by the pump closure; they are written by the receive loop on the same connection. Reads/writes are single-int and the race is benign (a stale ring for one frame only loses/gains an agent at the very edge). No lock needed.

- [ ] **Step 3: Build and smoke-run the host**

Run:
```bash
dotnet build Headless/Sim.Web/Sim.Web.csproj
```
Expected: clean build. (Behavioral verification is manual in Task 5 once the client sends `view`.)

- [ ] **Step 4: Commit**

```bash
git add Headless/Sim.Web/Program.cs
git commit -m "Sim.Web: filter agent snapshots to the client's camera ring (WS view message)"
```

---

## Task 5: Client — stream structures on the ring + send `view`

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` (`streamRegionTerrain` `:207-275`; init `:839-863`)

**Interfaces:**
- Consumes: `/asset/towntile/{mx}/{my}` (Task 3), the existing model-proto loader (`loadModel`), `buildFlatInstances`, `getFlatSheet`, and the `cmx/cmy/R` already computed in `streamRegionTerrain`.
- Produces: `regionStructures` Map (pixel-key → THREE.Group); a `view` WS message.

There is no JS unit harness in this repo, so this task is verified by **build + manual run**, not an automated test. Keep the change small and mirror the proven terrain-streaming code exactly.

- [ ] **Step 1: Add a `streamRegionStructures` that piggybacks the terrain ring**

In `town3d.html`, inside `streamRegionTerrain`, after the terrain eviction loop (line 274, before the closing `}`), call into a structures pass that reuses the already-computed `cmx, cmy, R`. Add a `regionStructures` map near `regionTiles` (search for `const regionTiles = new Map()` and add beside it):

```javascript
const regionStructures = new Map();   // pixelKey 'mx,my' -> THREE.Group | 'loading'
const structuresGroup = new THREE.Group(); scene.add(structuresGroup);
```

At the end of `streamRegionTerrain`, add (using the same `cmx, cmy, R` in scope):

```javascript
  // Structures (buildings + decorative flats) stream on the SAME ring as terrain.
  const wantS = [];
  for (let mx = cmx - R; mx <= cmx + R; mx++)
    for (let my = cmy - R; my <= cmy + R; my++) {
      if (mx < regionMeta.mx0 || mx > regionMeta.mx1 ||
          my < regionMeta.my0 || my > regionMeta.my1) continue;
      const key = mx + ',' + my;
      if (regionStructures.has(key)) continue;
      wantS.push({ mx, my, key, d: (mx - cmx) * (mx - cmx) + (my - cmy) * (my - cmy) });
    }
  wantS.sort((a, b) => a.d - b.d);
  for (const w of wantS.slice(0, 4)) {
    regionStructures.set(w.key, 'loading');
    fetch(`/asset/towntile/${w.mx}/${w.my}`)
      .then(r => r.ok ? r.json() : null)
      .then(d => addStructureTile(w.key, d))
      .catch(() => regionStructures.delete(w.key));
  }
  for (const [key, g] of regionStructures) {
    if (g === 'loading') continue;
    const [mx, my] = key.split(',').map(Number);
    if (Math.abs(mx - cmx) > R + 2 || Math.abs(my - cmy) > R + 2) {
      g.traverse(o => { if (o.isMesh || o.isInstancedMesh) o.geometry.dispose(); });
      structuresGroup.remove(g); regionStructures.delete(key);
    }
  }
```

- [ ] **Step 2: Implement `addStructureTile` (instantiate placements + flats)**

Add a function near `buildFlatInstances` (mirror the model-instancing the initial `/asset/town` load already does at `town3d.html:861-875`). The exact prototype-clone + `applyMatrix4` call must match the existing path:

```javascript
async function addStructureTile(key, d) {
  if (!d || (!d.placements?.length && !d.flats?.length)) {
    regionStructures.set(key, 'empty'); return;   // cache the empty pixel; nothing to evict
  }
  const g = new THREE.Group();
  // Buildings: load each unique model once (browser-cached), clone + apply matrix.
  const protos = {};
  await Promise.all((d.modelIds || []).map(id => loadModel(id).then(m => { protos[id] = m; })));
  for (const p of d.placements) {
    const proto = protos[p.modelId];
    if (!proto) continue;
    const inst = proto.clone();
    inst.applyMatrix4(new THREE.Matrix4().fromArray(p.matrix));
    g.add(inst);
  }
  // Decorative flats: group by archive, instance per archive (as the town path does).
  const byArchive = {};
  for (const f of (d.flats || [])) (byArchive[f.archive] ||= []).push(f);
  for (const [archive, items] of Object.entries(byArchive)) {
    const { meta, tex } = await getFlatSheet(Number(archive));
    g.add(buildFlatInstances(meta, tex, items.map(f =>
      ({ record: f.record, x: f.x, y: f.y, z: f.z }))));
  }
  g.visible = ui('bld') ? ui('bld').checked : true;
  structuresGroup.add(g);
  regionStructures.set(key, g);
}
```

Note: confirm the helper names against the current file during implementation — `loadModel`, `buildFlatInstances`, `getFlatSheet`, and the building-visibility checkbox id (`'bld'` or whatever the toggle is). Match them exactly; do not invent.

- [ ] **Step 3: Send the `view` message when the camera's pixel changes**

In `streamRegionTerrain`, after `cmx`/`cmy`/`R` are computed (line 224), report the ring to the server so it can filter agents:

```javascript
  if (cmx !== lastViewMx || cmy !== lastViewMy) {
    lastViewMx = cmx; lastViewMy = cmy;
    if (ws && ws.readyState === 1)
      ws.send(JSON.stringify({ type: 'view', mx: cmx, my: cmy, r: R + 2 }));
  }
```

Declare `let lastViewMx = -999, lastViewMy = -999;` near the `streamAccum` declaration (line 212). Use `R + 2` so the agent ring matches the structure/terrain eviction hysteresis (agents don't pop at the visible edge). Confirm the WebSocket variable name (`ws`) against the file.

- [ ] **Step 4: Build the web project (serves the static file) and verify it loads**

Run:
```bash
dotnet build Headless/Sim.Web/Sim.Web.csproj
```
Expected: clean build (the HTML is static content; this just confirms nothing else broke).

- [ ] **Step 5: Manual verification**

Run the viewer on a region and confirm streaming + filtering:
```bash
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web/Sim.Web.csproj -- --region "Isle of Balfiera"
```
Then open `http://localhost:<port>/town3d.html`, and check:
- Buildings appear only around the camera and stream in/out as you fly (WASD), matching terrain tiles.
- The browser Network tab shows `/asset/towntile/...` requests following the camera, not one giant `/asset/town`.
- Over the WS, the `snap` payload's `entities` count drops to the ring (compare panning toward vs. away from a dense town).
- No console errors; evicted tiles free memory (no unbounded growth flying across the region).

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.Web/wwwroot/town3d.html
git commit -m "town3d: stream region buildings/flats on the terrain ring + report camera view"
```

---

## Self-review notes (resolved)

- **Spec coverage:** Section 1 → Tasks 1 (sim index) & 2 (geometry index); Section 2 boot wiring (Stage 1) → Task 3; Section 3 endpoints/protocol → Tasks 3 (towntile, town-empty), 4 (WS view), 5 (client); Section 4 testing → Task 1 (unit), 2 (conservation unit), 3 (integration smoke), 5 (manual). Stage 2 is explicitly deferred.
- **Conservation invariant** is tested twice: pure (`RegionPlacementIndexTests.Build_ConservesEveryPlacement`) and end-to-end (`RegionStreamingSmokeTests`).
- **Key-scheme hazard:** `PixelKey` is `<<32`; `AssetService.TileKey` is `<<20` and must not be reused — called out in Global Constraints.
- **Names to confirm against `town3d.html` at implementation time** (flagged in Tasks 5.2/5.3): `loadModel`, `buildFlatInstances`, `getFlatSheet`, the building toggle id, and the `ws` variable. The plan must not invent these.
- **Region name** in the env-gated tests (`"Isle of Balfiera"`) may need swapping for one present in the local data — noted in Task 3 Step 2.
