# World Flora & Props Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the world's exterior flats (nature scenery + decorative props) as billboards, and build a per-species vegetation/resource `FloraRegistry` from the same scenery data as the data model for future harvesting.

**Architecture:** Two independent components sharing only the scenery-parse convention, mirroring the POI pattern. **A (render):** `TownLayout` emits `Flat` billboard placements; `AssetService` serves a per-archive flat atlas; `town3d.html` draws batched camera-facing billboards. **B (registry):** a `FloraRegistry` on `SimWorld` holds per-instance `FloraInstance`s parsed by `TownLoader`, classified against a hand-authored `SpeciesCatalog`. The catalog is authored from texture atlases the controller renders and visually inspects.

**Tech Stack:** C# / .NET 8, the `Headless/` project set (`Sim.Core`, `Sim.World`, `Sim.AssetExport`, `Sim.Web`, `Sim.Host`), Daggerfall data via `Sim.Data`. Client: `town3d.html` (three.js). Build with `dotnet`.

## Global Constraints

- **`Sim.Core` must never reference the Daggerfall API enums** (`DFRegion.*`, `DFLocation.*`, `ClimateBases`, etc.). Flora data in core stores `archive/record/climate` as plain `int` and `Category/Resource` as pure `Sim.Core` enums. All DF-data parsing lives in `Sim.World`/`Sim.AssetExport` (which reference `Sim.Data`).
- **Render and registry parse the scenery independently** (like `RegionLoader` vs `TownLayout` for POIs), but MUST use the **same nature-flat world-position formula** so billboards and flora instances coincide: per 16×16 tile `(x,y)`, read `GroundScenery[x, 15-y]`, skip `TextureRecord < 1`, local position `(x*256, -2, y*256 + 256) * GlobalScale` relative to the block origin. `GlobalScale = 0.025f`; `BlocksFile.RMBDimension = 4096`; tile dimension `= 256`.
- **Nature archive = `loc.Climate.NatureArchive`** (`DFLocation.ClimateSettings.NatureArchive`, already populated by `MapsFile.GetWorldClimateSettings`). Winter variants (505/507/509/511) are out of scope for live selection this pass — always use the location's summer `NatureArchive`; the catalog still defines winter→summer inheritance for completeness.
- **People flats excluded:** any flat record with `FactionID != 0` is skipped (render and registry). The live sim owns population.
- **Registry covers nature `GroundScenery` only.** Decorative flats (`BlockFlatObjectRecords`, `MiscFlatObjectRecords`) render but are NOT classified or recorded.
- **No harvesting behavior, no road generation, no interiors** — out of scope.
- **Tests:** the headless `Sim.Tests` xUnit project does not compile during the engine rewrite — do NOT add to or run it. Verification is `dotnet build` of the affected project + deterministic probes (`--flatsheet`, `--floracheck`) + manual `town3d`.
- Source layout: `Sim.Core` compiles `Assets/Sim/Engine/**` + `Assets/Sim/Registries/**`; `Sim.World` compiles `Assets/Sim/World/**`; `Sim.AssetExport` compiles `Headless/Sim.AssetExport/**`. Daggerfall data at `/home/uggeli/df-data/arena2` (`SimBoot.DefaultArena2Path`; set `DAGGERFALL_ARENA2` if unset).

---

### Task 1: Flat texture atlas — packer, endpoints, and `--flatsheet` dump

Builds the per-archive flat atlas (one static cell per texture record) used by both the client renderer and the controller's catalog authoring.

**Files:**
- Create: `Headless/Sim.AssetExport/SpriteFlat.cs`
- Modify: `Headless/Sim.AssetExport/AssetService.cs` (add `GetFlatSheet`/`GetFlatMeta` near the sprite methods ~line 323)
- Modify: `Headless/Sim.Web/Program.cs` (add two endpoints near `/asset/spritesheet` ~line 323)
- Create: `Headless/Sim.Host/FlatSheet.cs`
- Modify: `Headless/Sim.Host/Program.cs` (add `--flatsheet` case near `--gatecheck` ~line 25; usage string ~line 74)

**Interfaces:**
- Consumes: `AssetService` private `TextureFile Tex(int archive)` (`AssetService.cs:344`), `Png.Encode`, `TextureDecode.Rgba(DFBitmap, TextureFile, out int w, out int h)` (used at `AssetService.cs:308-310`), `SpritePerson.GlobalScale = 0.025f`.
- Produces:
  - `class FlatMeta { int Archive; int SheetW, SheetH; int Count; FlatCell[] Cells; }` and `struct FlatCell { int U, V, W, H; float WorldW, WorldH; }` (namespace `DaggerfallWorkshop.Sim.AssetExport`).
  - `static (byte[] png, FlatMeta meta) SpriteFlat.Build(string arena2, int archive)`.
  - `AssetService.GetFlatSheet(int archive) -> byte[]`, `AssetService.GetFlatMeta(int archive) -> FlatMeta`.
  - Endpoints `GET /asset/flatsheet/{archive:int}` (PNG), `GET /asset/flatmeta/{archive:int}` (JSON).
  - `FlatSheet.Run(int archive, string outPng) -> int` and host command `--flatsheet <archive> <out.png>`.

- [ ] **Step 1: Write `SpriteFlat.Build`**

Create `Headless/Sim.AssetExport/SpriteFlat.cs`. It loads the archive, decodes every record's frame 0, lays them out in a fixed 8-column grid (row-major in record order), cell size = the max record dimensions, and records each cell's rect + world size (`pixels * GlobalScale`).

```csharp
using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace DaggerfallWorkshop.Sim.AssetExport
{
    public struct FlatCell { public int U, V, W, H; public float WorldW, WorldH; }

    public sealed class FlatMeta
    {
        public int Archive;
        public int SheetW, SheetH;
        public int Count;
        public int Cols;
        public FlatCell[] Cells;
    }

    /// Packs every record of a TEXTURE archive into a single static atlas (one cell
    /// per record, record order, 8 columns). Used for nature/decorative flat billboards
    /// and as the controller's visual catalog-authoring sheet. Unlike SpritePerson there
    /// is no animation — one frame (0) per record.
    public static class SpriteFlat
    {
        const int Cols = 8;
        const float GlobalScale = 0.025f;

        public static (byte[] png, FlatMeta meta) Build(string arena2, int archive)
        {
            var tex = new TextureFile(System.IO.Path.Combine(arena2, TextureFile.IndexToFileName(archive)), FileUsage.UseMemory, true);
            tex.LoadPalette(System.IO.Path.Combine(arena2, tex.PaletteName));
            int count = tex.RecordCount;

            // Decode each record frame 0; track max cell size.
            var rgbas = new byte[count][];
            var ws = new int[count]; var hs = new int[count];
            int cellW = 1, cellH = 1;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    DFBitmap bmp = tex.GetDFBitmap(i, 0);
                    rgbas[i] = TextureDecode.Rgba(bmp, tex, out int w, out int h);
                    ws[i] = w; hs[i] = h;
                    if (w > cellW) cellW = w;
                    if (h > cellH) cellH = h;
                }
                catch { rgbas[i] = null; ws[i] = 0; hs[i] = 0; }
            }

            int rows = (count + Cols - 1) / Cols;
            int sheetW = Cols * cellW, sheetH = rows * cellH;
            var sheet = new byte[sheetW * sheetH * 4];   // transparent (zeroed)
            var cells = new FlatCell[count];

            for (int i = 0; i < count; i++)
            {
                int cx = (i % Cols) * cellW, cy = (i / Cols) * cellH;
                int w = ws[i], h = hs[i];
                // bottom-align within the cell (feet/base on cell floor), left-align
                int ox = cx, oy = cy + (cellH - h);
                if (rgbas[i] != null)
                    for (int y = 0; y < h; y++)
                        Array.Copy(rgbas[i], y * w * 4, sheet, ((oy + y) * sheetW + ox) * 4, w * 4);
                cells[i] = new FlatCell { U = cx, V = cy, W = cellW, H = cellH, WorldW = w * GlobalScale, WorldH = h * GlobalScale };
            }

            var meta = new FlatMeta { Archive = archive, SheetW = sheetW, SheetH = sheetH, Count = count, Cols = Cols, Cells = cells };
            return (Png.Encode(sheetW, sheetH, sheet), meta);
        }
    }
}
```

- [ ] **Step 2: Add `AssetService` accessors (with a cache)**

In `Headless/Sim.AssetExport/AssetService.cs`, after the sprite accessors (~line 326), add a cache and two methods:

```csharp
        private readonly Dictionary<int, (byte[] png, FlatMeta meta)> _flatCache = new();
        public byte[] GetFlatSheet(int archive) => Flat(archive).png;
        public FlatMeta GetFlatMeta(int archive) => Flat(archive).meta;
        private (byte[] png, FlatMeta meta) Flat(int archive)
        {
            lock (_gate)
            {
                if (!_flatCache.TryGetValue(archive, out var built))
                    _flatCache[archive] = built = SpriteFlat.Build(_arena2, archive);
                return built;
            }
        }
```

(Use the existing lock field — confirm its name near the other cached getters, e.g. `_gate`; if the sprite getters use a different lock or none, match them.)

- [ ] **Step 3: Add the two endpoints**

In `Headless/Sim.Web/Program.cs`, after the `/asset/spritemeta/{archive}` endpoint (~line 337):

```csharp
app.MapGet("/asset/flatsheet/{archive:int}", (HttpContext ctx, int archive) =>
{
    var png = assets.GetFlatSheet(archive);
    if (png == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(png, "image/png");
});
app.MapGet("/asset/flatmeta/{archive:int}", (HttpContext ctx, int archive) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Json(assets.GetFlatMeta(archive), jsonOptions);
});
```

- [ ] **Step 4: Add the `--flatsheet` host command**

Create `Headless/Sim.Host/FlatSheet.cs`:

```csharp
using System;
using System.IO;
using DaggerfallWorkshop.Sim.AssetExport;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Dumps one TEXTURE archive's flat atlas to a PNG file (no web server needed),
    /// for visually authoring the SpeciesCatalog. Cells are record order, 8 per row.
    public static class FlatSheet
    {
        public static int Run(int archive, string outPng)
        {
            if (string.IsNullOrEmpty(outPng)) { Console.Error.WriteLine("usage: --flatsheet <archive> <out.png>"); return 2; }
            var (png, meta) = SpriteFlat.Build(SimBoot.DefaultArena2Path, archive);
            File.WriteAllBytes(outPng, png);
            Console.WriteLine($"archive {archive}: {meta.Count} records, sheet {meta.SheetW}x{meta.SheetH}, {meta.Cols} cols -> {outPng}");
            return 0;
        }
    }
}
```

In `Headless/Sim.Host/Program.cs`, add after the `--gatecheck` case (~line 25):

```csharp
                case "--flatsheet":
                    return DaggerfallWorkshop.Sim.Host.FlatSheet.Run(
                        args.Length > 1 ? int.Parse(args[1]) : 0,
                        args.Length > 2 ? args[2] : null);
```

Update the usage string (~line 74) to include `| --flatsheet archive out.png`.

- [ ] **Step 5: Build and verify with a dump**

Run:
```bash
dotnet build Headless/Sim.Host/Sim.Host.csproj
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host/Sim.Host.csproj -- --flatsheet 504 /tmp/flat504.png
```
Expected: prints `archive 504: <N> records, sheet WxH, 8 cols -> /tmp/flat504.png` with N > 0, and the PNG exists (`ls -l /tmp/flat504.png` non-empty).

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.AssetExport/SpriteFlat.cs Headless/Sim.AssetExport/AssetService.cs Headless/Sim.Web/Program.cs Headless/Sim.Host/FlatSheet.cs Headless/Sim.Host/Program.cs
git commit -m "Sim/: flat texture atlas (SpriteFlat) + /asset/flatsheet|flatmeta + --flatsheet dump"
```

---

### Task 2: Emit flat placements from `TownLayout`

Adds nature + decorative flat billboards to the exported region/town data.

**Files:**
- Modify: `Headless/Sim.AssetExport/TownLayout.cs` (`Placement`/`TownData` ~lines 27-44; `AddLocation` body ~lines 106-176)

**Interfaces:**
- Consumes: existing `Mat.Translate/Mul`, `GlobalScale`, `BlocksFile.RMBDimension`; `DFBlock.RmbBlock.FldHeader.GroundData.GroundScenery[16,16]` (`RmbGroundScenery.TextureRecord`), `sub.Exterior.BlockFlatObjectRecords`, `block.RmbBlock.MiscFlatObjectRecords` (`RmbBlockFlatObjectRecord{ XPos,YPos,ZPos,TextureArchive,TextureRecord,FactionID }`), `loc.Climate.NatureArchive`.
- Produces: `struct Flat { int Archive; int Record; float X, Y, Z; float WorldW, WorldH; }`; `TownData.Flats` (`List<Flat>`) and `TownData.FlatArchives` (`List<int>` — unique archives to prefetch).

- [ ] **Step 1: Add the `Flat` type and `TownData` fields**

In `TownLayout.cs`, after the `Placement` struct (~line 31):

```csharp
        public struct Flat
        {
            public int Archive;
            public int Record;
            public float X, Y, Z;      // world position (billboard base)
            public float WorldW, WorldH;
        }
```

In `TownData` (~line 40), add:

```csharp
            public List<Flat> Flats = new();
            public List<int> FlatArchives = new();   // unique flat archives the client must fetch
```

- [ ] **Step 2: Emit nature + decorative flats in `AddLocation`**

`AddLocation` receives `DFLocation loc` (the `Resolve`/`ResolveRegion` callers already pass it). Add a `seenFlatArchive` set alongside the existing `seen` (model) set — pass it through or make it a local in `AddLocation` keyed per call; simplest is a `HashSet<int>` parameter mirroring `seen`. Inside the block loop, after the existing Misc3d emission and before the block loop closes, add nature scenery; and inside the subrecord loop add decorative flats. Use this helper added to the class:

```csharp
        static void AddFlat(TownData data, HashSet<int> flatSeen, int archive, int record,
            float wx, float wy, float wz, float worldW, float worldH, Bounds b)
        {
            data.Flats.Add(new Flat { Archive = archive, Record = record, X = wx, Y = wy, Z = wz, WorldW = worldW, WorldH = worldH });
            if (flatSeen.Add(archive)) data.FlatArchives.Add(archive);
            b.Add(wx, wy, wz);
        }
```

Nature scenery — add inside the per-block loop (after the Misc3d block, still inside `for bx`/`for by`), using the block origin `(offX + bx*blockSide, offY, offZ + by*blockSide)`:

```csharp
                    // Nature ground scenery (trees/rocks/plants). One per 16x16 tile,
                    // climate archive from the location; same formula the flora registry uses.
                    int natureArchive = loc.Climate.NatureArchive;
                    var ground = block.RmbBlock.FldHeader.GroundData.GroundScenery;
                    if (ground != null)
                    {
                        const float TileDim = 256f, NatureOffsetY = -2f;
                        for (int sx = 0; sx < 16; sx++)
                        for (int sy = 0; sy < 16; sy++)
                        {
                            int rec = ground[sx, 15 - sy].TextureRecord;
                            if (rec < 1) continue;
                            float wx = offX + bx * blockSide + sx * TileDim * GlobalScale;
                            float wy = offY + NatureOffsetY * GlobalScale;
                            float wz = offZ + by * blockSide + (sy * TileDim + TileDim) * GlobalScale;
                            var (fw, fh) = FlatSize(natureArchive, rec);
                            AddFlat(data, flatSeen, natureArchive, rec, wx, wy, wz, fw, fh, b);
                        }
                    }

                    // Block-level misc flat objects (light flats, animals, decor). Skip NPC flats.
                    if (block.RmbBlock.MiscFlatObjectRecords != null)
                        foreach (var f in block.RmbBlock.MiscFlatObjectRecords)
                        {
                            if (f.FactionID != 0) continue;
                            float[] m = Mat.Mul(blockM, Mat.Translate(f.XPos * GlobalScale, -f.YPos * GlobalScale, f.ZPos * GlobalScale));
                            var (fw, fh) = FlatSize(f.TextureArchive, f.TextureRecord);
                            AddFlat(data, flatSeen, f.TextureArchive, f.TextureRecord, m[12], m[13], m[14], fw, fh, b);
                        }
```

Decorative subrecord flats — add inside the `foreach (var sub ...)` loop, after the `subM` is computed (so positions inherit the subrecord transform):

```csharp
                        if (sub.Exterior.BlockFlatObjectRecords != null)
                            foreach (var f in sub.Exterior.BlockFlatObjectRecords)
                            {
                                if (f.FactionID != 0) continue;   // static NPC — sim owns population
                                float[] fm = Mat.Mul(Mat.Mul(blockM, subM),
                                    Mat.Translate(f.XPos * GlobalScale, -f.YPos * GlobalScale, f.ZPos * GlobalScale));
                                var (fw, fh) = FlatSize(f.TextureArchive, f.TextureRecord);
                                AddFlat(data, flatSeen, f.TextureArchive, f.TextureRecord, fm[12], fm[13], fm[14], fw, fh, b);
                            }
```

- [ ] **Step 3: Add the `FlatSize` helper (world billboard size from texture record)**

`AddLocation` needs each flat's world size. Reuse the atlas's per-record world size via a lightweight reader. Add to `TownLayout`:

```csharp
        // Cache of (archive -> per-record world sizes), so flat billboards match their texture.
        static readonly Dictionary<int, (float w, float h)[]> _flatSizes = new();
        static string _flatArena2;
        static (float w, float h) FlatSize(int archive, int record)
        {
            if (!_flatSizes.TryGetValue(archive, out var sizes))
            {
                var meta = SpriteFlat.Build(_flatArena2, archive).meta;
                sizes = new (float, float)[meta.Count];
                for (int i = 0; i < meta.Count; i++) sizes[i] = (meta.Cells[i].WorldW, meta.Cells[i].WorldH);
                _flatSizes[archive] = sizes;
            }
            return (record >= 0 && record < sizes.Length) ? sizes[record] : (1f, 1f);
        }
```

Set `_flatArena2 = arena2;` at the top of both `Resolve` and `ResolveRegion` (they already receive `arena2`). (This shared static mirrors how `AddLocation` is a static helper; acceptable for the single-process exporter. If the reviewer prefers, thread `arena2` into `AddLocation` instead.)

- [ ] **Step 4: Thread `flatSeen` and confirm `AddLocation` signature**

Update `AddLocation` to take `HashSet<int> flatSeen` and both call sites in `Resolve`/`ResolveRegion` to pass a per-call `new HashSet<int>()` (or reuse one shared set per `TownData` so `FlatArchives` is region-wide unique — match how `seen`/`ModelIds` is scoped: one set per `TownData`). Mirror the existing `seen` lifetime exactly.

- [ ] **Step 5: Build and verify emission**

Run:
```bash
dotnet build Headless/Sim.AssetExport/Sim.AssetExport.csproj
```
Expected: Build succeeded. (Visual/JSON verification happens in Task 3 against the running web server.)

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.AssetExport/TownLayout.cs
git commit -m "Sim/: TownLayout emits nature + decorative flat billboards (skip NPC flats)"
```

---

### Task 3: Render flat billboards in `town3d.html`

Draws the exported flats as batched, camera-facing, upright billboards.

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` (add a `flats` group + loader near the placements loop ~line 775, and the billboard sheet fetch mirroring `getSheet` ~line 528)

**Interfaces:**
- Consumes: `data.flats` (`[{archive, record, x, y, z, worldW, worldH}]`), `data.flatArchives`, `/asset/flatsheet/{archive}`, `/asset/flatmeta/{archive}`.
- Produces: a `flats` THREE.Group of billboards in the scene.

- [ ] **Step 1: Add a flat-sheet fetcher**

Near the existing `getSheet` (~line 528), add a parallel cache that loads a flat atlas + meta:

```javascript
const flatSheets = new Map();
function getFlatSheet(archive) {
  if (flatSheets.has(archive)) return flatSheets.get(archive);
  const p = Promise.all([
    fetch(`/asset/flatmeta/${archive}`).then(r => r.json()),
    new Promise(res => texLoader.load(`/asset/flatsheet/${archive}`, res)),
  ]).then(([meta, tex]) => {
    tex.magFilter = THREE.NearestFilter; tex.minFilter = THREE.NearestFilter;
    return { meta, tex };
  });
  flatSheets.set(archive, p);
  return p;
}
```

- [ ] **Step 2: Build the flats group on load**

Where the model placements are added (~line 775, after the `for (const p of data.placements)` loop), add:

```javascript
const flats = new THREE.Group();
scene.add(flats);
// Group flats by archive so each atlas is fetched once.
const byArchive = new Map();
for (const f of (data.flats || [])) {
  if (!byArchive.has(f.archive)) byArchive.set(f.archive, []);
  byArchive.get(f.archive).push(f);
}
for (const [archive, list] of byArchive) {
  getFlatSheet(archive).then(({ meta, tex }) => {
    for (const f of list) {
      const c = meta.cells[f.record];
      if (!c) continue;
      const mat = new THREE.MeshBasicMaterial({ map: tex.clone(), transparent: true, alphaTest: 0.5, side: THREE.DoubleSide });
      mat.map.repeat.set(c.w / meta.sheetW, c.h / meta.sheetH);
      mat.map.offset.set(c.u / meta.sheetW, 1 - (c.v + c.h) / meta.sheetH);
      const geo = new THREE.PlaneGeometry(f.worldW, f.worldH);
      const mesh = new THREE.Mesh(geo, mat);
      mesh.position.set(f.x, f.y + f.worldH / 2, f.z);   // base at f.y, billboard centre up half height
      mesh.userData.billboard = true;
      flats.add(mesh);
    }
  });
}
```

- [ ] **Step 3: Billboard the flats each frame (Y-axis)**

In the render loop where agent billboards are oriented (~line 658, the `lookAt` for agents), add an update for `flats` children so they face the camera but stay upright. After the agents are oriented:

```javascript
flats.children.forEach(m => m.lookAt(camera.position.x, m.position.y, camera.position.z));
```

(If `flats` is not in scope of the render loop, hoist it to a module-level `let flats` set during load, matching how `agents` is referenced.)

- [ ] **Step 4: Build and verify in the browser**

Run:
```bash
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web/Sim.Web.csproj
```
Open `town3d.html` in region mode. Expected: nature billboards (trees/rocks/plants) appear on the ground in/around towns, upright and facing the camera; decorative flats appear near buildings; existing buildings and agents render unchanged. Note any obviously wrong scale/placement for tuning. This is the manual gate; capture a screenshot for the controller.

- [ ] **Step 5: Commit**

```bash
git add Headless/Sim.Web/wwwroot/town3d.html
git commit -m "Sim/: town3d renders flat billboards (nature + decor), Y-axis camera-facing"
```

---

### Controller interlude: author the `SpeciesCatalog` (not a subagent task)

After Task 1 lands, the controller dumps each base nature archive and authors the catalog by sight — this requires image inspection and cannot be delegated to an implementer subagent.

```bash
for a in 500 501 502 503 504 506 508 510; do
  DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host/Sim.Host.csproj -- --flatsheet $a /tmp/flat$a.png
done
```

The controller `Read`s each `/tmp/flat<a>.png`, and for every record (cells are record order, 8 per row, so `record = row*8 + col`) authors an entry `{archive, record, name, category, resource}`. The result is delivered to Task 4 as the catalog data block (a C# initializer in `SpeciesCatalog.cs`) embedded in that task's brief. Records that are clearly empty/duplicate map to `Unknown`/no-resource.

---

### Task 4: Flora core types + `SpeciesCatalog` + `SimWorld` wiring

Adds the core flora data model and the authored classification catalog.

**Files:**
- Create: `Assets/Sim/Registries/FloraRegistry.cs` (Sim.Core)
- Create: `Assets/Sim/World/SpeciesCatalog.cs` (Sim.World)
- Modify: `Assets/Sim/Engine/SimWorld.cs` (declare/construct/register `Flora`, mirroring `Pois`)

**Interfaces:**
- Produces:
  - `enum FloraCategory { Unknown, Tree, Bush, Plant, Crop, Rock, Water, Deadwood }` and `enum ResourceKind { None, Wood, Forage, Stone, Reed, Herb }` (Sim.Core; final members per the authored catalog).
  - `sealed class FloraInstance { int Archive, Record, Climate; float X, Y, Z; int SpeciesId; FloraCategory Category; ResourceKind Resource; }`.
  - `sealed class FloraRegistry : Registry` with `FloraInstance Add(FloraInstance)`, `IReadOnlyList<FloraInstance> All`, `int Count`, no-op `Update`.
  - `SimWorld.Flora` (public readonly `FloraRegistry`).
  - `class SpeciesEntry { string Name; FloraCategory Category; ResourceKind Resource; }`; `static class SpeciesCatalog` with `SpeciesEntry Lookup(int archive, int record)` and `int SpeciesIdOf(int archive, int record)` (returns 0/`Unknown` for unmapped), winter→summer inheritance.

- [ ] **Step 1: Create the core flora types**

Create `Assets/Sim/Registries/FloraRegistry.cs` (mirrors `PoiRegistry.cs`):

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim
{
    /// Coarse kind of a flora instance (from the SpeciesCatalog). Pure Sim.Core enum.
    public enum FloraCategory { Unknown, Tree, Bush, Plant, Crop, Rock, Water, Deadwood }

    /// What a flora instance yields when harvested (future). Pure Sim.Core enum.
    public enum ResourceKind { None, Wood, Forage, Stone, Reed, Herb }

    /// One piece of world vegetation/resource scenery (a tree/rock/plant on a tile).
    /// Parsed from RMB ground scenery at load; classified via SpeciesCatalog. Render is a
    /// separate consumer; harvesting is future. Written only by the loader; read thereafter.
    public sealed class FloraInstance
    {
        public int Archive, Record, Climate;
        public float X, Y, Z;
        public int SpeciesId;
        public FloraCategory Category;
        public ResourceKind Resource;
    }

    /// Every flora instance in the loaded region, in load order. Seeded by the region
    /// loader (direct Add). Static after load; no runtime mutation.
    public sealed class FloraRegistry : Registry
    {
        readonly List<FloraInstance> _all = new List<FloraInstance>();
        public FloraRegistry(EventBus events) : base(events) { }
        public FloraInstance Add(FloraInstance f) { _all.Add(f); return f; }
        public IReadOnlyList<FloraInstance> All => _all;
        public int Count => _all.Count;
        public override void Update(long tick) { }
    }
}
```

- [ ] **Step 2: Wire `Flora` into `SimWorld`**

In `Assets/Sim/Engine/SimWorld.cs`, mirror the `Pois` wiring (declaration after `Pois` ~line 24; construction after `Pois = new PoiRegistry(e);` ~line 72; add `Flora` to the `registries` array ~line 93):

```csharp
        public readonly FloraRegistry Flora;
```
```csharp
            Pois = new PoiRegistry(e);
            Flora = new FloraRegistry(e);
```
```csharp
                Settlements, Pois, Flora, Buildings, Position, Behavior, Intent, Identity, Needs, Vitals, Life,
```

- [ ] **Step 3: Create `SpeciesCatalog` with the authored data**

Create `Assets/Sim/World/SpeciesCatalog.cs`. The `_entries` list and `_map` are the controller-authored data (delivered in this task's brief). Structure (data abbreviated here — the brief carries the full authored table):

```csharp
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class SpeciesEntry
    {
        public string Name;
        public FloraCategory Category;
        public ResourceKind Resource;
    }

    /// Hand-authored (archive,record) -> species classification, authored by visual
    /// inspection of the flat atlases (see --flatsheet). Winter archives inherit their
    /// summer counterpart by record index. Unmapped -> Unknown (id 0).
    public static class SpeciesCatalog
    {
        // id 0 is always Unknown.
        static readonly List<SpeciesEntry> _entries = new()
        {
            new SpeciesEntry { Name = "Unknown", Category = FloraCategory.Unknown, Resource = ResourceKind.None },
            // ... authored entries appended here (from the brief) ...
        };

        // (archive*1000 + record) -> species id. Authored from the contact sheets.
        static readonly Dictionary<int, int> _map = new()
        {
            // { 504*1000 + 7, <id> }, ...  (from the brief)
        };

        // Winter archive -> summer archive (same plants, snow variants).
        static readonly Dictionary<int, int> _winterToSummer = new()
        {
            { 505, 504 }, { 507, 506 }, { 509, 508 }, { 511, 510 },
        };

        static int Key(int archive, int record)
        {
            if (_winterToSummer.TryGetValue(archive, out var summer)) archive = summer;
            return archive * 1000 + record;
        }

        public static int SpeciesIdOf(int archive, int record)
            => _map.TryGetValue(Key(archive, record), out var id) ? id : 0;

        public static SpeciesEntry Lookup(int archive, int record)
            => _entries[SpeciesIdOf(archive, record)];
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build Headless/Sim.World/Sim.World.csproj`
Expected: Build succeeded (compiles Sim.Core + Sim.World). The `Flora` registry is constructed; `SpeciesCatalog.Lookup` resolves authored entries and falls back to `Unknown`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/Registries/FloraRegistry.cs Assets/Sim/World/SpeciesCatalog.cs Assets/Sim/Engine/SimWorld.cs
git commit -m "Sim/: FloraRegistry + FloraInstance + authored SpeciesCatalog, wired on SimWorld"
```

---

### Task 5: Populate `world.Flora` at load + `--floracheck` probe

Parses nature scenery into the registry during region load and adds the verification probe.

**Files:**
- Modify: `Assets/Sim/World/TownLoader.cs` (in `LoadLocationInto`, where it walks the location's blocks)
- Create: `Headless/Sim.Host/FloraCheck.cs`
- Modify: `Headless/Sim.Host/Program.cs` (add `--floracheck` case ~line 25; usage string ~line 74)

**Interfaces:**
- Consumes: `world.Flora.Add` + `FloraInstance` (Task 4); `SpeciesCatalog.SpeciesIdOf/Lookup` (Task 4); `loc.Climate.NatureArchive`, `GroundScenery[x,15-y].TextureRecord`; the block origin used by `TownLoader.LoadLocationInto`.
- Produces: `world.Flora.All` populated after region load; `static int FloraCheck.Run(string region)`.

- [ ] **Step 1: Parse nature scenery in `TownLoader.LoadLocationInto`**

`TownLoader.LoadLocationInto` already walks the location's exterior blocks at a known per-block origin (the same `originX/originY` block coordinates `RegionLoader` passes). At the point where each RMB block is loaded, add nature-scenery parsing that mirrors the render formula EXACTLY (Global Constraint). For each block at block-grid origin `(bxOrigin, byOrigin)` in blocks, with world block side `blockSide = BlocksFile.RMBDimension * TownLoader.GlobalScale` and the settlement world origin `s.OriginX/s.OriginZ`:

```csharp
            // World flora: record each nature scenery flat as a FloraInstance (render is a
            // separate consumer; harvesting is future). Same position formula as TownLayout.
            int natureArchive = loc.Climate.NatureArchive;
            int climate = loc.Climate.WorldClimate;
            var ground = block.RmbBlock.FldHeader.GroundData.GroundScenery;
            if (ground != null)
            {
                const float TileDim = 256f, NatureOffsetY = -2f;
                float bworldX = s.OriginX + bx * blockSide;   // bx,by = this block's grid coords
                float bworldZ = s.OriginZ + by * blockSide;
                for (int sx = 0; sx < 16; sx++)
                for (int sy = 0; sy < 16; sy++)
                {
                    int rec = ground[sx, 15 - sy].TextureRecord;
                    if (rec < 1) continue;
                    int sid = SpeciesCatalog.SpeciesIdOf(natureArchive, rec);
                    var e = SpeciesCatalog.Lookup(natureArchive, rec);
                    world.Flora.Add(new FloraInstance
                    {
                        Archive = natureArchive, Record = rec, Climate = climate,
                        X = bworldX + sx * TileDim * TownLoader.GlobalScale,
                        Y = NatureOffsetY * TownLoader.GlobalScale,
                        Z = bworldZ + (sy * TileDim + TileDim) * TownLoader.GlobalScale,
                        SpeciesId = sid, Category = e.Category, Resource = e.Resource,
                    });
                }
            }
```

Place this where the loader already has `block`, `loc`, `s` (the `SettlementData`), and the per-block `bx`/`by` grid coordinates in scope. If the loader's block loop uses different local names for the block-grid coordinates or block variable, adapt the three names (`block`, `bx`, `by`) accordingly — do not change the formula. If `TownLoader.GlobalScale` is not the existing constant name, use the same scale constant the loader already uses for block sizing.

- [ ] **Step 2: Build**

Run: `dotnet build Headless/Sim.World/Sim.World.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Create the `--floracheck` probe**

Create `Headless/Sim.Host/FloraCheck.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Loads a whole region and reports the flora registry: instance count, per-category
    /// and per-resource histograms, per-species counts, and the Unknown fraction (a high
    /// Unknown share means SpeciesCatalog gaps).
    public static class FloraCheck
    {
        public static int Run(string region)
        {
            if (string.IsNullOrEmpty(region)) { Console.Error.WriteLine("usage: --floracheck <region>"); return 2; }
            var world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, 600f, 12345);
            var flora = world.Flora.All;
            Console.WriteLine($"region {region}: {flora.Count} flora instances");

            Console.WriteLine("by category:");
            foreach (var g in flora.GroupBy(f => f.Category).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-10} {g.Count()}");
            Console.WriteLine("by resource:");
            foreach (var g in flora.GroupBy(f => f.Resource).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-10} {g.Count()}");

            int unknown = flora.Count(f => f.SpeciesId == 0);
            double pct = flora.Count == 0 ? 0 : 100.0 * unknown / flora.Count;
            Console.WriteLine($"unknown: {unknown}/{flora.Count} ({pct:0.0}%)");
            return flora.Count > 0 ? 0 : 1;
        }
    }
}
```

In `Headless/Sim.Host/Program.cs`, add after the `--flatsheet` case:

```csharp
                case "--floracheck":
                    return DaggerfallWorkshop.Sim.Host.FloraCheck.Run(args.Length > 1 ? args[1] : null);
```
Update the usage string to include `| --floracheck region`.

- [ ] **Step 4: Build and run the probe**

Run:
```bash
dotnet build Headless/Sim.Host/Sim.Host.csproj
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host/Sim.Host.csproj -- --floracheck Betony
```
Expected: `region Betony: <N> flora instances` with N > 0; category/resource histograms printed; `unknown: <u>/<N> (<pct>%)` with the Unknown share low (catalog authored). Exit 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sim/World/TownLoader.cs Headless/Sim.Host/FloraCheck.cs Headless/Sim.Host/Program.cs
git commit -m "Sim/: TownLoader records nature scenery into world.Flora + --floracheck probe"
```

---

## Self-Review

**Spec coverage:**
- Flat render export (nature + decorative, skip NPC flats) → Task 2; serving + atlas → Task 1; client billboards → Task 3. ✓
- Per-species `FloraRegistry`/`FloraInstance` → Task 4; populated at load → Task 5. ✓
- `SpeciesCatalog` authored from rendered atlases → Task 1 (tool) + Controller interlude (authoring) + Task 4 (encoding). ✓
- Winter→summer inheritance, `Unknown` fallback → Task 4 (`_winterToSummer`, id 0). ✓
- `Sim.Core` API-enum-free (archive/record/climate as int, pure enums) → Task 4 types. ✓
- Render/registry position-formula parity → Global Constraint, enforced identically in Task 2 (`AddLocation`) and Task 5 (`TownLoader`). ✓
- People flats skipped (`FactionID != 0`) → Tasks 2 + 5 (registry only parses GroundScenery, which has no faction). ✓
- Registry = nature only; decor render-only → Task 5 parses only `GroundScenery`; Task 2 renders all. ✓
- Verification: `--flatsheet` (Task 1), build gates (all), `--floracheck` (Task 5), manual town3d (Task 3). Matches spec's probe-based testing. ✓
- Out of scope (harvesting, roads, interiors, winter live-selection) → not implemented. ✓

**Placeholder scan:** The only deferred content is the `SpeciesCatalog` data table, which is explicitly a controller-authored artifact delivered in Task 4's brief (it requires image inspection and cannot be authored blind) — the *structure*, lookup, inheritance, and fallback are all concrete. No other TBD/TODO; every code step shows complete code; commands have expected output.

**Type consistency:** `Flat{Archive,Record,X,Y,Z,WorldW,WorldH}`, `FlatMeta`/`FlatCell{U,V,W,H,WorldW,WorldH}`, `FloraInstance{Archive,Record,Climate,X,Y,Z,SpeciesId,Category,Resource}`, `FloraRegistry.Add/All/Count`, `SpeciesCatalog.SpeciesIdOf/Lookup`, `world.Flora`, `loc.Climate.NatureArchive`, the `(x,15-y)` + `(x*256,-2,y*256+256)*GlobalScale` formula — all referenced identically across Tasks 1-5. ✓
