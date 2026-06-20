using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Web;
using Sim.AssetExport;

// Web spectator: pan over the living town/region in a browser (town3d.html) on the
// parallel CQRS engine. Usage: dotnet run <region> <location> [--port 8080]
//   [--tps 30] [--region] [--starthour H]

string region = null, location = null;
int port = 8080;
int tickRate = 10;          // engine ticks per real-second at startup; viewer overrides live
const float ClockRunning = 1f;   // clock seed: any value >0 means "advancing" (no longer a rate)
bool wholeRegion = false;   // --region: spectate every settlement at once, not one town
int startHour = -1;         // --starthour H: boot the clock at hour H (else dawn)

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--tps": tickRate = int.Parse(args[++i]); break;
        case "--region": wholeRegion = true; break;
        case "--starthour": startHour = int.Parse(args[++i]); break;
        default:
            if (region == null) region = args[i];
            else if (location == null) location = args[i];
            break;
    }
}
region ??= "Daggerfall";
location ??= "Gothway Garden";

// Daggerfall NPC names: parse the name banks once and hand the generator to the
// loader before any world boots. Best-effort — if the file is missing, NPCs keep
// their descriptive placeholder names instead of crashing the host.
TownLoader.Names = LoadNameGen(Path.Combine(AppContext.BaseDirectory, "NameGen.txt"));

// One boot, two scopes. Region mode loads every settlement into the combined grid;
// town mode loads one location. The static map payload, the asset endpoints, and the
// agent stream all read whichever scope booted — the client is identical either way.
SimWorld world;
string worldRegionName, worldName;
int worldBlocksWide, worldBlocksHigh, worldCivilians;
List<(string name, float ox, float oy, float oz)> settlements = null;
List<(float minX, float minZ, float maxX, float maxZ, float dx, float dy, float dz)> remap = null;
// Asset service: shared by the region boot (for per-town pad heights) and the
// endpoints. Created here so the boot block below can query tile floors.
var assets = new AssetService(SimBoot.DefaultArena2Path);
// Region-terrain streaming state: pixel bbox, tile size, shared datum, town pixels.
int rMx0 = 0, rMy0 = 0, rMx1 = 0, rMy1 = 0;
float rTileSize = 0f, rDatum = 0f;
List<(int mx, int my, string name, int w, int h)> rTowns = null;
(int mx, int my, string name, int w, int h) rCentre = default;
if (wholeRegion)
{
    world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, ClockRunning);
    var grid0 = world.TownGrid.Current;
    worldRegionName = region;
    worldName = world.Settlements.All.Count + " settlements";
    worldBlocksWide = grid0.BlocksWide;
    worldBlocksHigh = grid0.BlocksHigh;
    worldCivilians = CountCivilians(world);

    // Place settlements at their TRUE overworld positions, not the sim's packed
    // grid. The sim packs them into a dense walkability grid for pathfinding; since
    // no agent yet crosses between settlements, where one sits in the world is purely
    // a render choice. So use the real map-pixel coordinates (one pixel == one
    // 819.2 m terrain tile) and remap each settlement's agents by the same delta —
    // the buildings get the geographic origin, the agents get (geo - packed).
    const float TileSize = 32768f * TownLayout.GlobalScale;  // 819.2 m, one map pixel
    const float BlockSide = 4096f * TownLayout.GlobalScale;  // 102.4 m (RMBDimension)
    // Each town's terrain tile flattens a footprint CENTRED in its 819.2 m pixel
    // (TerrainTile.Generate), and that tile streams grid-aligned so it meets its
    // wilderness neighbours seamlessly. So the buildings (and their agents) must be
    // centred in the pixel too — same offset Generate uses: (128 - w*16)/2 tiles,
    // 16 tiles per block. This lands every town on its own flattened ground.
    static (float x, float z) TownCentre(int blocksWide, int blocksHigh)
    {
        int tilePosX = (128 - blocksWide * 16) / 2, tilePosY = (128 - blocksHigh * 16) / 2;
        return (tilePosX / 16f * BlockSide, tilePosY / 16f * BlockSide);
    }
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
    mx0 = Math.Max(0, mx0 - TerrainPad); my0 = Math.Max(0, my0 - TerrainPad);
    mx1 = Math.Min(999, mx1 + TerrainPad); my1 = Math.Min(499, my1 + TerrainPad);

    // Terrain-streaming metadata: pixel bbox + the town pixels (so the tile endpoint
    // knows which pixels flatten a location).
    rTileSize = TileSize;
    rMx0 = mx0; rMy0 = my0; rMx1 = mx1; rMy1 = my1;
    rTowns = pAll.Select(p => (p.MapPixelX, p.MapPixelY, p.Name, p.BlocksWide, p.BlocksHigh)).ToList();
    // The settlement nearest the bbox centre supplies the datum the region levels to.
    int cmx = (rMx0 + rMx1) / 2, cmy = (rMy0 + rMy1) / 2;
    rCentre = rTowns[0];
    int bestD = int.MaxValue;
    foreach (var t in rTowns)
    {
        int d = (t.mx - cmx) * (t.mx - cmx) + (t.my - cmy) * (t.my - cmy);
        if (d < bestD) { bestD = d; rCentre = t; }
    }
    // Shared region datum: the centre settlement's flattened floor. Every streamed
    // tile levels to this value so neighbours meet at a continuous seam.
    rDatum = assets.RegionTileFloor(region, rCentre.name, rCentre.w, rCentre.h);

    // Now place each settlement. A town's terrain pad sits at the world height
    // (townFloor - datum) * MaxHeight — its own elevation, not y=0 — so the
    // buildings and agents must be lifted to that pad, or they float / bury. We
    // query each town's own tile floor and turn the elevation gap into a Y offset.
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
}
else
{
    world = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, ClockRunning);
    worldRegionName = region;
    worldName = location;
    var grid0 = world.TownGrid.Current;
    worldBlocksWide = grid0.BlocksWide;
    worldBlocksHigh = grid0.BlocksHigh;
    worldCivilians = CountCivilians(world);
}

// Optional: jump the clock to a given hour (e.g. midday) so the scene is lit
// regardless of the spectator's speed control. Seeded as a registry intent +
// fold (ApplySeed runs registries only), the same way SimBoot seeds the clock.
if (startHour >= 0)
{
    world.Events.Publish(new WorldClockSetIntent
    {
        Year = 405, Month = 0, Day = 3, Hour = startHour, Minute = 0, Second = 0f,
        TimeScale = ClockRunning, DeltaGameSeconds = 0.1,
    });
    world.ApplySeed();
}

var runner = new WorldRunner(world, tickRate);
runner.Start();

// IncludeFields: the DTOs use public fields, which System.Text.Json ignores by default.
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    IncludeFields = true,
};

// Road cells for the map underlay: flat [x0,z0,x1,z1,...] cell coords.
// Solid (blocked) cells — building footprints, walls — as run-length rows
// [x, y, length, ...] so houses render with real walls and perimeter.
var roadCells = new List<int>();
var solidRuns = new List<int>();
var townGrid = world.TownGrid.Current;
if (townGrid != null)
{
    for (int y = 0; y < townGrid.Height; y++)
    {
        int runStart = -1;
        for (int x = 0; x <= townGrid.Width; x++)
        {
            bool solid = x < townGrid.Width && townGrid.CostAt(x, y) == 0;
            if (solid && runStart < 0) runStart = x;
            if (!solid && runStart >= 0)
            {
                solidRuns.Add(runStart);
                solidRuns.Add(y);
                solidRuns.Add(x - runStart);
                runStart = -1;
            }
            if (x < townGrid.Width && townGrid.CostAt(x, y) == 1)
            {
                roadCells.Add(x);
                roadCells.Add(y);
            }
        }
    }
}

// Static world payload, built once — the same connect-handshake idea as Sim.Net.
var worldJson = JsonSerializer.SerializeToUtf8Bytes(new
{
    type = "world",
    region = worldRegionName,
    name = worldName,
    blocksWide = worldBlocksWide,
    blocksHigh = worldBlocksHigh,
    civilians = worldCivilians,
    cellSize = TownGridData.CellSize,
    roads = roadCells,
    solids = solidRuns,
    buildings = world.Buildings.All
        .OrderBy(kv => kv.Key)
        .Select(kv => new
        {
            i = kv.Key,
            kind = kv.Value.Kind.ToString(),
            x = kv.Value.X,
            z = kv.Value.Z,
            q = kv.Value.Quality,
        }),
}, jsonOptions);

// Pin WebRoot to the wwwroot copied beside the assembly, so the viewer's static
// files resolve no matter what directory the host is launched from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.MapGet("/asset/model/{objectId}", (HttpContext ctx, uint objectId, int? climate, int? season) =>
{
    var json = assets.GetModelGltf(objectId, climate ?? 2, season ?? 0);
    if (json == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Content(json, "model/gltf+json");
});

// Resolved layout of the booted town: model placements + climate (render plane 1).
app.MapGet("/asset/town", (HttpContext ctx) =>
{
    var town = wholeRegion ? assets.GetRegion(region, settlements) : assets.GetTown(region, location);
    ctx.Response.Headers.CacheControl = "no-cache";   // live aggregate; evolves during dev
    return Results.Json(town, jsonOptions);
});

// Terrain heightfield + tilemap (render plane 2). Town mode: the location's pixel +
// 3x3 neighbours, shipped up front. Region mode: empty here — the client streams the
// region per pixel via /asset/regionmeta + /asset/terraintile instead.
app.MapGet("/asset/terrain", (HttpContext ctx) =>
{
    object terrain = wholeRegion
        ? new List<TerrainTileData>()
        : assets.GetTownTerrain(region, location);
    ctx.Response.Headers.CacheControl = "no-cache";   // live aggregate; evolves during dev
    return Results.Json(terrain, jsonOptions);
});

// Region terrain metadata: the pixel bbox + tile size the client uses to decide which
// tiles its viewport needs. isRegion=false in town mode (the client streams nothing).
app.MapGet("/asset/regionmeta", () => Results.Json(new
{
    isRegion = wholeRegion,
    mx0 = rMx0, my0 = rMy0, mx1 = rMx1, my1 = rMy1, tileSize = rTileSize,
}, jsonOptions));

// One region terrain tile (render plane 2, streamed). The client requests only the
// pixels its camera covers and caches them — never the whole region at once.
app.MapGet("/asset/terraintile/{mx:int}/{my:int}", (HttpContext ctx, int mx, int my) =>
{
    if (!wholeRegion || mx < rMx0 || my < rMy0 || mx > rMx1 || my > rMy1)
        return Results.NotFound();
    string locName = null; int lw = 0, lh = 0;
    foreach (var t in rTowns) if (t.mx == mx && t.my == my) { locName = t.name; lw = t.w; lh = t.h; break; }
    // +Z = north: count Z down from the bbox south edge (rMy1), matching the
    // settlement/agent placement and DFU's north-up terrain frame.
    float addX = (mx - rMx0) * rTileSize, addZ = (rMy1 - my) * rTileSize;
    var tile = assets.GetRegionTile(region, mx, my, rDatum, addX, addZ, locName, lw, lh);
    ctx.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Json(tile, jsonOptions);
});

// Ground-tile atlas (8x7 grid of an archive's 56 ground tiles) for terrain painting.
app.MapGet("/asset/groundatlas/{archive:int}", (HttpContext ctx, int archive) =>
{
    var png = assets.GetGroundAtlas(archive);
    if (png == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(png, "image/png");
});

// Live agents (render plane 3): person sprite sheet + metadata, and the civilian
// archive set the client hashes entity ids into.
app.MapGet("/asset/civilians", () => Results.Json(assets.CivilianArchives, jsonOptions));

app.MapGet("/asset/spritepools", () => Results.Json(new
{
    civilian = assets.CivilianArchives,
    guard = assets.GuardArchives,
    monster = assets.MonsterArchives,
}, jsonOptions));

app.MapGet("/asset/spritesheet/{archive:int}", (HttpContext ctx, int archive) =>
{
    var png = assets.GetSpriteSheet(archive);
    if (png == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(png, "image/png");
});

app.MapGet("/asset/spritemeta/{archive:int}", (HttpContext ctx, int archive) =>
{
    var meta = assets.GetSpriteMeta(archive);
    if (meta == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Json(meta, jsonOptions);
});

app.MapGet("/asset/texture/{archive:int}/{record:int}", (HttpContext ctx, int archive, int record) =>
{
    var png = assets.GetTexturePng(archive, record);
    if (png == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(png, "image/png");
});

// Project a packed agent position into geographic world space (region mode): find
// the settlement whose packed rectangle holds it, shift by that settlement's delta
// and lift it to that town's terrain pad height (dy) so agents stand on the ground.
(float x, float y, float z) GeoRemap(float x, float z)
{
    if (remap != null)
        foreach (var r in remap)
            if (x >= r.minX && x < r.maxX && z >= r.minZ && z < r.maxZ)
                return (x + r.dx, r.dy, z + r.dz);
    return (x, 0f, z);
}

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var sendLock = new SemaphoreSlim(1, 1);

    async Task Send(byte[] payload)
    {
        await sendLock.WaitAsync();
        try { await ws.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { sendLock.Release(); }
    }

    await Send(worldJson);

    // Snapshot pump: ~5 Hz is plenty for a spectator page.
    var pump = Task.Run(async () =>
    {
        long lastTick = -1;
        while (ws.State == WebSocketState.Open)
        {
            var snap = runner.Latest;
            if (snap != null && snap.Tick != lastTick)
            {
                lastTick = snap.Tick;
                var payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "snap",
                    tick = snap.Tick,
                    hour = snap.Hour,
                    minute = snap.Minute,
                    night = snap.Night,
                    sun = snap.Sun,
                    weather = snap.Weather.ToString(),
                    speed = runner.TargetTps,   // now a tick rate (ticks/sec); -1 = unlimited
                    // Compact rows: [id, x, z, activity, phase, yaw, kind, groundY]
                    // (2D spectator reads [0..4]; 3D viewer also uses yaw+kind+groundY.)
                    // Region mode projects packed coords to the geographic world and
                    // carries the town's pad height so agents stand on the terrain.
                    entities = snap.Agents.Select(e =>
                    {
                        var (ex, ey, ez) = wholeRegion ? GeoRemap(e.X, e.Z) : (e.X, 0f, e.Z);
                        return new object[]
                            { e.Id, MathF.Round(ex, 1), MathF.Round(ez, 1), e.Activity, e.Phase,
                              MathF.Round(e.Yaw, 3), e.Kind, MathF.Round(ey, 1) };
                    }),
                }, jsonOptions);
                await Send(payload);
            }
            await Task.Delay(200);
        }
    });

    // Receive loop: spectator controls. Inspect reads the live registries directly
    // (rare, on click) — wrapped, since the sim thread may be mid-write.
    var buffer = new byte[4096];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!doc.RootElement.TryGetProperty("type", out var t)) continue;

            switch (t.GetString())
            {
                case "inspect" when doc.RootElement.TryGetProperty("id", out var idProp):
                    await Send(JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "detail", detail = InspectEntity(new EntityId(idProp.GetInt32())) }, jsonOptions));
                    break;

                case "inspectBuilding" when doc.RootElement.TryGetProperty("i", out var bProp):
                    await Send(JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "building", building = InspectBuilding(bProp.GetInt32()) }, jsonOptions));
                    break;

                case "speed" when doc.RootElement.TryGetProperty("scale", out var sProp):
                    runner.SetTickRate((int)sProp.GetDouble());   // ticks/sec; -1 = unlimited
                    break;
            }
        }
    }
    catch (WebSocketException) { /* client went away mid-frame */ }

    try { await pump; } catch { /* pump dies with the socket */ }
});

Console.WriteLine($"spectating {worldRegionName} / {worldName} "
    + $"({worldCivilians} civilians) at http://localhost:{port}");
app.Run();

// --- local helpers (read the new registries) ---

int CountCivilians(SimWorld w)
{
    int n = 0;
    foreach (var kv in w.Identity.All) if (kv.Value.Kind == EntityKind.CivilianNPC) n++;
    return n;
}

// Parse Assets/Resources/NameGen.txt (FullSerializer JSON: race → setCount + sets[],
// each set a setIndex + parts[]) into the bank shape DfNameGen wants. Sets are placed
// by their setIndex so we don't depend on array order.
static DfNameGen LoadNameGen(string path)
{
    try
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"NameGen.txt not found at {path} — NPCs keep placeholder names");
            return null;
        }
        // NameGen.txt is FullSerializer output, which Unity reads but isn't strict JSON:
        // it has a missing comma between two set objects ("}" "{") and trailing commas.
        // Insert the missing separators and allow trailing commas so System.Text.Json
        // accepts it. (Name fragments are short alpha tokens — no braces to false-match.)
        string text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"}\s*{", "},{");
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
        var banks = new Dictionary<int, string[][]>();
        foreach (var race in doc.RootElement.EnumerateObject())
        {
            int bank = DfNameGen.BankIndexFromName(race.Name);
            if (bank < 0) continue;   // Monster* banks — no playable name
            if (!race.Value.TryGetProperty("sets", out var setsEl)) continue;

            var byIndex = new Dictionary<int, string[]>();
            int maxIdx = -1;
            foreach (var setEl in setsEl.EnumerateArray())
            {
                int si = setEl.TryGetProperty("setIndex", out var siEl) ? siEl.GetInt32() : byIndex.Count;
                var parts = new List<string>();
                if (setEl.TryGetProperty("parts", out var partsEl))
                    foreach (var p in partsEl.EnumerateArray()) parts.Add(p.GetString());
                byIndex[si] = parts.ToArray();
                if (si > maxIdx) maxIdx = si;
            }
            var sets = new string[maxIdx + 1][];
            for (int i = 0; i <= maxIdx; i++)
                sets[i] = byIndex.TryGetValue(i, out var arr) ? arr : Array.Empty<string>();
            banks[bank] = sets;
        }
        Console.WriteLine($"NameGen: loaded {banks.Count} name banks");
        return new DfNameGen(banks);
    }
    catch (Exception e)
    {
        Console.WriteLine("NameGen.txt parse failed: " + e.Message);
        return null;
    }
}

// EntityEnums.Races value → display name (1=Breton … 8=Argonian); "—" if unset.
static string RaceName(int race)
{
    switch (race)
    {
        case 1: return "Breton";
        case 2: return "Redguard";
        case 3: return "Nord";
        case 4: return "Dark Elf";
        case 5: return "High Elf";
        case 6: return "Wood Elf";
        case 7: return "Khajiit";
        case 8: return "Argonian";
        default: return "—";
    }
}

// Click-to-inspect, rebuilt on the new registries (replaces the deleted Inspector).
// The sim thread may be writing concurrently; retry a couple times on a transient
// collection-modified race, then give up gracefully.
object InspectEntity(EntityId id)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            if (!world.Identity.TryGet(id, out var ident)) return new { id = id.Value, missing = true };
            world.Behavior.TryGet(id, out var beh);
            world.Needs.TryGet(id, out var needs);
            world.Coin.TryGet(id, out var coin);
            world.Position.TryGet(id, out var pos);
            world.Lineage.TryGet(id, out var lin);
            return new
            {
                id = id.Value,
                name = ident.Name,
                kind = ident.Kind.ToString(),
                race = RaceName(ident.Race),
                level = ident.Level,
                career = ident.CareerIndex,
                faction = ident.FactionId,
                coin = coin,
                activity = beh != null ? beh.Activity.ToString() : "—",
                phase = beh != null ? beh.Phase.ToString() : "—",
                targetBuilding = beh != null ? beh.TargetBuilding : -1,
                needs = needs != null ? needs.V : null,
                // Family-tree hook surfaced for the viewer: surname, household id, spouse.
                family = lin != null ? lin.FamilyId : -1,
                surname = lin != null ? lin.Surname : null,
                spouse = lin != null && lin.Spouse != EntityId.None ? lin.Spouse.Value : -1,
                x = pos != null ? pos.X : 0f,
                z = pos != null ? pos.Z : 0f,
            };
        }
        catch (InvalidOperationException) { Thread.Sleep(2); }
    }
    return new { id = id.Value, busy = true };
}

object InspectBuilding(int i)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            if (!world.Buildings.TryGet(i, out var b)) return new { i, missing = true };
            return new
            {
                i,
                kind = b.Kind.ToString(),
                quality = b.Quality,
                faction = b.FactionId,
                x = b.X,
                z = b.Z,
            };
        }
        catch (InvalidOperationException) { Thread.Sleep(2); }
    }
    return new { i, busy = true };
}
