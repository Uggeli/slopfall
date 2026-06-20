# Region Spatial Index — Stage 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make the region's geo overworld layout *sim truth* — move the geo-placement computation out of the web host (`Program.cs`) into `RegionLoader`, so `world.Geography` is seeded at load. Pure refactor: no behavioral change.

**Architecture:** `RegionLoader.LoadRegion` gains an injected `tileFloor` delegate + `maxTerrainHeight` (AssetExport constants the sim cannot reference). A new **Pass 3** computes the bbox, datum, per-POI geo origin (XYZ), and the packed→geo agent remap — the exact logic currently in `Program.cs:71-159` — sets each `RegionPoi.OriginX/Y/Z`, and seeds `world.Geography`. The web host stops computing geo and reads it back from the registry.

**Tech Stack:** C# / .NET 10, `Sim.Core`/`Sim.World`/`Sim.AssetExport`/`Sim.Web`, xUnit (in `Sim.SpatialTests`).

Spec: `docs/superpowers/specs/2026-06-20-region-spatial-index-design.md` (Stage 2 section). Stage 1 is committed (`e02338c40 → e8f9cc38a`).

## Global Constraints

- Geo math must be **bit-for-bit equivalent** to the Stage 1 web-host code. The regression guard is the live region (Betony): `/asset/regionmeta` bbox = `mx0=107, my0=251, mx1=137, my1=277, tileSize=819.2`; `/asset/towntile` sum = **668 placements across 74 non-empty pixels**; WS agent filter **620 → 8** at view `(122,264) r=1`. Any drift in these means the move changed geo.
- Sim-side scale constant is `TownLoader.GlobalScale = 0.025f` (matches `TownLayout.GlobalScale`). `TileSize = 32768 * GlobalScale`, `BlockSide = BlocksFile.RMBDimension * GlobalScale`.
- `maxTerrainHeight` = `TerrainTile.MaxTerrainHeight` = `1539f` — injected, never referenced from the sim.
- `TerrainPad = 3`; bbox clamps `mx ∈ [0,999]`, `my ∈ [0,499]` — Daggerfall-fixed, sim constants.
- The injected delegate is `Func<string loc, int w, int h, float> tileFloor` (region is `RegionLoader`'s own); the composition root adapts `assets.RegionTileFloor(region, loc, w, h)`.
- New optional params default to `null`/`0f` so existing `CreateRegion` callers (`Sim.Host` FloraCheck/PoiCheck/`--soakregion`) are unchanged; Pass 3 runs only when `tileFloor != null`.
- TDD; commit per task.

## File Structure

- **Modify** `Assets/Sim/World/RegionLoader.cs` — add `tileFloor`/`maxTerrainHeight` params + Pass 3 (Task 1).
- **Modify** `Assets/Sim/World/SimBoot.cs:36-48` — thread the params through `CreateRegion` (Task 1).
- **Create** `Headless/Sim.SpatialTests/RegionGeographySeedTests.cs` — verify Pass 3 seeds the known bbox/remap (Task 1).
- **Modify** `Headless/Sim.Web/Program.cs:61-163` (region block), `:396-401` (local `GeoRemap`), `:455` (pump), `:48-49,57` (fields) — read geo from the registry; delete the inlined computation (Task 2).

---

## Task 1: Move geo computation into `RegionLoader` Pass 3

**Files:**
- Modify: `Assets/Sim/World/RegionLoader.cs:48` (signature), end of `LoadRegion` (new Pass 3)
- Modify: `Assets/Sim/World/SimBoot.cs:36-48`
- Test: `Headless/Sim.SpatialTests/RegionGeographySeedTests.cs`

**Interfaces:**
- Consumes: `world.Geography.Seed(...)` and `GeoRemapEntry` (Stage 1, `DaggerfallWorkshop.Sim.Engine`), `RegionPoi.OriginX/Y/Z` (`PoiRegistry.cs:50`).
- Produces:
  - `RegionLoader.LoadRegion(SimWorld, SimRandom, MapsFile, BlocksFile, string, WoodsFile, Func<string,int,int,float> tileFloor = null, float maxTerrainHeight = 0f)`
  - `SimBoot.CreateRegion(string, string, float, int seed = 12345, Func<string,int,int,float> tileFloor = null, float maxTerrainHeight = 0f)`

- [ ] **Step 1: Write the failing test**

Create `Headless/Sim.SpatialTests/RegionGeographySeedTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop.Sim;
using Sim.AssetExport;
using Xunit;

namespace Sim.SpatialTests
{
    /// Stage 2: RegionLoader Pass 3 must seed world.Geography itself (geo is now sim
    /// truth), reproducing the Stage 1 web-host bbox exactly. No-ops without ARENA2.
    public class RegionGeographySeedTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void Pass3_SeedsGeographyWithTheBetonyBbox()
        {
            if (!Available) return;
            var assets = new AssetService(Arena2);

            var world = SimBoot.CreateRegion(Arena2, "Betony", 0f, 12345,
                (loc, w, h) => assets.RegionTileFloor("Betony", loc, w, h),
                TerrainTile.MaxTerrainHeight);

            var g = world.Geography;
            Assert.True(g.HasRegion);
            // Known Stage 1 live values (must be reproduced exactly).
            Assert.Equal(107, g.Mx0);
            Assert.Equal(251, g.My0);
            Assert.Equal(137, g.Mx1);
            Assert.Equal(277, g.My1);
            Assert.Equal(819.2f, g.TileSize, 1);

            // Each exterior POI must have a geo origin set by the loader (not 0,0,0
            // unless it genuinely sits at the bbox origin).
            var pois = world.Pois.All.Where(p => p.HasExterior).ToList();
            Assert.NotEmpty(pois);
            Assert.Contains(pois, p => p.OriginX != 0f || p.OriginZ != 0f);

            // PixelOf round-trips a POI's own geo origin back to its map pixel.
            var s = pois.First(p => p.Settlement != null);
            var (mx, my) = g.PixelOf(s.OriginX, s.OriginZ);
            Assert.Equal(s.MapPixelX, mx);
            Assert.Equal(s.MapPixelY, my);
        }

        [Fact]
        public void CreateRegion_WithoutTileFloor_LeavesGeographyUnseeded()
        {
            if (!Available) return;
            var world = SimBoot.CreateRegion(Arena2, "Betony", 0f);   // no delegate
            Assert.False(world.Geography.HasRegion);   // back-compat for Sim.Host callers
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.SpatialTests/Sim.SpatialTests.csproj --filter RegionGeographySeedTests`
Expected: FAIL — `CreateRegion` has no `tileFloor` overload (compile error), or `HasRegion` is false.

- [ ] **Step 3: Thread the delegate through `SimBoot.CreateRegion`**

In `Assets/Sim/World/SimBoot.cs`, change `CreateRegion`:

```csharp
        public static SimWorld CreateRegion(string arena2Path, string regionName,
            float timeScale, int seed = 12345,
            System.Func<string, int, int, float> tileFloor = null, float maxTerrainHeight = 0f)
        {
            var maps = new MapsFile(System.IO.Path.Combine(arena2Path, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(System.IO.Path.Combine(arena2Path, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            var woods = new WoodsFile(System.IO.Path.Combine(arena2Path, "WOODS.WLD"), FileUsage.UseMemory, true);

            var world = new SimWorld(seed);
            var rng = new SimRandom(seed);
            RegionLoader.LoadRegion(world, rng, maps, blocks, regionName, woods, tileFloor, maxTerrainHeight);
            SeedStart(world, timeScale);
            return world;
        }
```

- [ ] **Step 4: Add the Pass 3 computation to `RegionLoader`**

In `Assets/Sim/World/RegionLoader.cs`, change the `LoadRegion` signature (line 48):

```csharp
        public static RegionLoadResult LoadRegion(SimWorld world, SimRandom rng, MapsFile maps, BlocksFile blocks, string regionName, WoodsFile woods = null,
            System.Func<string, int, int, float> tileFloor = null, float maxTerrainHeight = 0f)
```

Then, immediately before the final `return result;` (currently `RegionLoader.cs:149`), add Pass 3 (verbatim translation of `Program.cs:71-159`):

```csharp
            // Pass 3: geo overworld placement as sim truth. Place every exterior POI at
            // its TRUE map-pixel position (the packed grid above is for pathfinding only),
            // record each origin + the packed→geo agent remap, and seed world.Geography.
            // Skipped when no tileFloor is injected (non-render callers: soak/probe).
            if (tileFloor != null)
            {
                const float TileSize = 32768f * TownLoader.GlobalScale;   // 819.2 m, one map pixel
                const float BlockSide = BlocksFile.RMBDimension * TownLoader.GlobalScale;   // 102.4 m
                const int TerrainPad = 3;

                (float x, float z) TownCentre(int bw, int bh)
                {
                    int tx = (128 - bw * 16) / 2, ty = (128 - bh * 16) / 2;
                    return (tx / 16f * BlockSide, ty / 16f * BlockSide);
                }

                var pAll = new List<RegionPoi>();
                foreach (var p in world.Pois.All) if (p.HasExterior) pAll.Add(p);

                int mx0 = int.MaxValue, my0 = int.MaxValue, mx1 = int.MinValue, my1 = int.MinValue;
                foreach (var p in pAll)
                {
                    if (p.MapPixelX < mx0) mx0 = p.MapPixelX;
                    if (p.MapPixelX > mx1) mx1 = p.MapPixelX;
                    if (p.MapPixelY < my0) my0 = p.MapPixelY;
                    if (p.MapPixelY > my1) my1 = p.MapPixelY;
                }
                if (pAll.Count > 0)
                {
                    mx0 = System.Math.Max(0, mx0 - TerrainPad); my0 = System.Math.Max(0, my0 - TerrainPad);
                    mx1 = System.Math.Min(999, mx1 + TerrainPad); my1 = System.Math.Min(499, my1 + TerrainPad);

                    // Datum: the POI nearest the bbox centre supplies the floor the whole
                    // region levels to (every streamed tile meets at a continuous seam).
                    int cmx = (mx0 + mx1) / 2, cmy = (my0 + my1) / 2;
                    RegionPoi centre = pAll[0];
                    int bestD = int.MaxValue;
                    foreach (var p in pAll)
                    {
                        int d = (p.MapPixelX - cmx) * (p.MapPixelX - cmx) + (p.MapPixelY - cmy) * (p.MapPixelY - cmy);
                        if (d < bestD) { bestD = d; centre = p; }
                    }
                    float datum = tileFloor(centre.Name, centre.BlocksWide, centre.BlocksHigh);

                    var poiPixels = new List<(int, int, int)>();
                    var remap = new List<GeoRemapEntry>();
                    for (int i = 0; i < pAll.Count; i++)
                    {
                        var p = pAll[i];
                        var (cx, cz) = TownCentre(p.BlocksWide, p.BlocksHigh);
                        float geoX = (p.MapPixelX - mx0) * TileSize + cx;
                        float geoZ = (my1 - p.MapPixelY) * TileSize + cz;
                        float floor = tileFloor(p.Name, p.BlocksWide, p.BlocksHigh);
                        float padY = (floor - datum) * maxTerrainHeight;
                        p.OriginX = geoX; p.OriginY = padY; p.OriginZ = geoZ;
                        poiPixels.Add((p.MapPixelX, p.MapPixelY, i));

                        var s = p.Settlement;
                        if (s != null)
                            remap.Add(new GeoRemapEntry
                            {
                                MinX = s.OriginX, MinZ = s.OriginZ,
                                MaxX = s.OriginX + s.BlocksWide * BlockSide,
                                MaxZ = s.OriginZ + s.BlocksHigh * BlockSide,
                                Dx = geoX - s.OriginX, Dy = padY, Dz = geoZ - s.OriginZ,
                            });
                    }
                    world.Geography.Seed(mx0, my0, mx1, my1, TileSize, datum, poiPixels, remap);
                }
            }
```

Ensure `RegionLoader.cs` has `using DaggerfallWorkshop.Sim.Engine;` (it already uses `SimWorld`/registries from that namespace — confirm `GeoRemapEntry`/`RegionPoi` resolve; `RegionPoi` is in `DaggerfallWorkshop.Sim`, already in scope).

- [ ] **Step 5: Run the test to verify it passes**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.SpatialTests/Sim.SpatialTests.csproj --filter RegionGeographySeedTests`
Expected: PASS (2 tests). If the bbox asserts fail, the geo math diverged from Stage 1 — diff against `Program.cs:71-159`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/World/RegionLoader.cs Assets/Sim/World/SimBoot.cs Headless/Sim.SpatialTests/RegionGeographySeedTests.cs
git commit -m "Sim core: RegionLoader Pass 3 computes geo + seeds Geography (Stage 2)"
```

---

## Task 2: Read geo from the registry in `Program.cs`; delete the inlined computation

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` — region block `:61-163`, fields `:48-49,57`, local `GeoRemap` `:396-401`, pump `:455`

**Interfaces:**
- Consumes: `world.Geography` (`Mx0/My0/Mx1/My1/TileSize/Datum/GeoRemap`), `RegionPoi.OriginX/Y/Z` (now set by Task 1), `assets.GetRegionIndex(...)` (Stage 1).

- [ ] **Step 1: Pass the delegate to `CreateRegion` and read geo from the registry**

In `Headless/Sim.Web/Program.cs`, replace the region block body (`:63-162`, from `world = SimBoot.CreateRegion(...)` through the `regionIndex = assets.GetRegionIndex(...)` line) with:

```csharp
    world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, ClockRunning, 12345,
        (loc, w, h) => assets.RegionTileFloor(region, loc, w, h), TerrainTile.MaxTerrainHeight);
    var grid0 = world.TownGrid.Current;
    worldRegionName = region;
    worldName = world.Settlements.All.Count + " settlements";
    worldBlocksWide = grid0.BlocksWide;
    worldBlocksHigh = grid0.BlocksHigh;
    worldCivilians = CountCivilians(world);

    // Geo overworld placement is now sim truth (RegionLoader Pass 3 set every POI's
    // OriginX/Y/Z and seeded world.Geography). The web host just reads it back and
    // reshapes it for the asset endpoints.
    var geo = world.Geography;
    rMx0 = geo.Mx0; rMy0 = geo.My0; rMx1 = geo.Mx1; rMy1 = geo.My1;
    rTileSize = geo.TileSize; rDatum = geo.Datum;

    var pAll = world.Pois.All.Where(p => p.HasExterior).ToList();
    // Town pixels (so /asset/terraintile knows which pixels flatten a location).
    rTowns = pAll.Select(p => (p.MapPixelX, p.MapPixelY, p.Name, p.BlocksWide, p.BlocksHigh)).ToList();
    // Geo origins for the geometry partition (AssetExport reads plain tuples, not the registry).
    settlements = pAll.Select(p => (p.Name, p.OriginX, p.OriginY, p.OriginZ)).ToList();
    regionIndex = assets.GetRegionIndex(region, settlements, rMx0, rMy1, rTileSize);
```

- [ ] **Step 2: Remove the now-dead `remap` field, `rCentre` field, and local `GeoRemap`**

- Delete the field at `Program.cs:49`:
  `List<(float minX, float minZ, float maxX, float maxZ, float dx, float dy, float dz)> remap = null;`
- Delete the field at `Program.cs:57`:
  `(int mx, int my, string name, int w, int h) rCentre = default;`
- Delete the local `GeoRemap` function (`Program.cs:393-401`, the comment block + the `(float x, float y, float z) GeoRemap(float x, float z) { ... }`).

- [ ] **Step 3: Point the pump at the registry's `GeoRemap`**

In the WS pump (`Program.cs:455`), change:

```csharp
                        var (ex, ey, ez) = wholeRegion ? GeoRemap(e.X, e.Z) : (e.X, 0f, e.Z);
```
to:
```csharp
                        var (ex, ey, ez) = wholeRegion ? world.Geography.GeoRemap(e.X, e.Z) : (e.X, 0f, e.Z);
```

(The agent ring filter already calls `world.Geography.PixelOf` — unchanged.)

- [ ] **Step 4: Build and run the full spatial suite**

Run:
```bash
dotnet build Headless/Sim.Web/Sim.Web.csproj
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.SpatialTests/Sim.SpatialTests.csproj
```
Expected: clean build; all tests pass (including the Stage 1 conservation smoke).

- [ ] **Step 5: Live equivalence check (the pure-refactor guard)**

Boot Betony region and confirm the numbers are IDENTICAL to Stage 1:
```bash
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web/Sim.Web.csproj -- Betony --region --port 8141 --tps 0 &
SV=$!; sleep 8
curl -s http://localhost:8141/asset/regionmeta    # expect mx0=107,my0=251,mx1=137,my1=277,tileSize=819.2
curl -s http://localhost:8141/asset/town | python3 -c "import sys,json;print('town placements',len(json.load(sys.stdin)['placements']))"  # expect 0
curl -s http://localhost:8141/asset/regionmeta | python3 -c "
import sys,json,urllib.request
m=json.load(sys.stdin); tot=0; ne=0
for mx in range(m['mx0'],m['mx1']+1):
  for my in range(m['my0'],m['my1']+1):
    d=json.load(urllib.request.urlopen(f'http://localhost:8141/asset/towntile/{mx}/{my}'))
    n=len(d.get('placements',[])); tot+=n; ne+=1 if n else 0
print('towntile total',tot,'non-empty',ne)"   # expect total 668, non-empty 74
kill $SV
```
Expected: bbox `107/251/137/277`, `/asset/town` placements `0`, towntile total `668` / non-empty `74`. Any difference ⇒ the geo move was not equivalence-preserving — diff Pass 3 against the deleted `Program.cs` block.

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.Web/Program.cs
git commit -m "Sim.Web: read region geo from world.Geography; drop the inlined computation (Stage 2)"
```

---

## Self-review notes (resolved)

- **Spec coverage:** Stage 2 section (inject `tileFloor`+`maxTerrainHeight`, Pass 3, `Program.cs` collapse, consumers unchanged) → Tasks 1 & 2.
- **Equivalence guard:** Task 1 pins the bbox + PixelOf round-trip; Task 2 pins the live endpoints (668/74, bbox, town=0) — the towntile distribution is a function of every geo origin, so an unchanged 668/74 proves the origins are unchanged.
- **Back-compat:** optional params keep `Sim.Host` (FloraCheck/PoiCheck/`--soakregion`) and the Stage 1 smoke test compiling and behaving; `CreateRegion` without a delegate leaves `Geography` unseeded (tested).
- **Names verified:** `TownLoader.GlobalScale` (0.025), `BlocksFile.RMBDimension`, `TerrainTile.MaxTerrainHeight` (1539), `RegionPoi.OriginX/Y/Z`, `GeoRemapEntry` fields — all confirmed against the tree.
- **Type consistency:** delegate is `Func<string,int,int,float>` (loc,w,h) everywhere; composition root adapts `assets.RegionTileFloor(region, loc, w, h)`.
