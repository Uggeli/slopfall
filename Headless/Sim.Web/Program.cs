using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
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
// Asset service: shared by the region boot (for per-town pad heights) and the
// endpoints. Created here so the boot block below can query tile floors.
var assets = new AssetService(SimBoot.DefaultArena2Path);
// Region-terrain streaming state: pixel bbox, tile size, shared datum, town pixels.
int rMx0 = 0, rMy0 = 0, rMx1 = 0, rMy1 = 0;
float rTileSize = 0f, rDatum = 0f;
List<(int mx, int my, string name, int w, int h)> rTowns = null;
// Region geometry partitioned by map pixel, served per-tile by /asset/towntile so
// the viewer streams buildings on the same ring as terrain (null in town mode).
Sim.AssetExport.RegionPlacementIndex regionIndex = null;
if (wholeRegion)
{
    world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, ClockRunning, 12345,
        (loc, w, h) => assets.RegionTileFloor(region, loc, w, h), TerrainTile.MaxTerrainHeight);
    var grid0 = world.TownGrid.Current;
    worldRegionName = region;
    worldName = world.Settlements.All.Count + " settlements";
    worldBlocksWide = grid0.BlocksWide;
    worldBlocksHigh = grid0.BlocksHigh;
    worldCivilians = CountCivilians(world);

    // Geo overworld placement is now sim truth: RegionLoader Pass 3 set every POI's
    // OriginX/Y/Z and seeded world.Geography (bbox, datum, packed→geo agent remap).
    // The web host just reads it back and reshapes it for the asset endpoints.
    var geo = world.Geography;
    rMx0 = geo.Mx0; rMy0 = geo.My0; rMx1 = geo.Mx1; rMy1 = geo.My1;
    rTileSize = geo.TileSize; rDatum = geo.Datum;

    var pAll = world.Pois.All.Where(p => p.HasExterior).ToList();
    // Town pixels (so /asset/terraintile knows which pixels flatten a location).
    rTowns = pAll.Select(p => (p.MapPixelX, p.MapPixelY, p.Name, p.BlocksWide, p.BlocksHigh)).ToList();
    // Geo origins for the geometry partition (AssetExport reads plain tuples, not the registry).
    settlements = pAll.Select(p => (p.Name, p.OriginX, p.OriginY, p.OriginZ)).ToList();
    regionIndex = assets.GetRegionIndex(region, settlements, rMx0, rMy1, rTileSize);
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

// Atom name table (built once): atomType:int -> concept name. Source the catalogs the
// utterance content draws from — PlaceAtoms (Danger/Provisions + the BuildingKind range)
// and PerceivableAtoms (entity Kind/Role/Race/Activity) — so the client can read content
// atoms as words. Keys are stringified ints (a JSON object is {string:value}).
var AtomNames = BuildAtomNames();

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
    ctx.Response.Headers.CacheControl = "no-cache";   // live aggregate; evolves during dev
    // Region mode streams geometry per pixel via /asset/towntile instead, so the
    // aggregate is empty here (mirrors /asset/terrain).
    if (wholeRegion)
        return Results.Json(new TownLayout.TownData
        {
            Region = region, Location = worldName,
            ClimateBase = regionIndex?.ClimateBase ?? 2, Season = 0,
        }, jsonOptions);
    return Results.Json(assets.GetTown(region, location), jsonOptions);
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

// One map pixel's render geometry (render plane 1, streamed). The client requests
// the pixels its camera ring covers and caches them. Empty/out-of-bbox pixels return
// 200 with empty arrays so the client caches "nothing here" and never re-asks.
app.MapGet("/asset/towntile/{mx:int}/{my:int}", (HttpContext ctx, int mx, int my) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    var tile = regionIndex?.At(mx, my) ?? new Sim.AssetExport.RegionTile();
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

// Atom name table: atomType:int -> concept name, so the client (and a later LLM) can
// verbalise utterance content ("Danger @b7" instead of "6001"). Sourced from the atom
// catalogs (PlaceAtoms + PerceivableAtoms); cached (the table is static for a boot).
app.MapGet("/asset/atoms", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Json(AtomNames, jsonOptions);
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

app.MapGet("/asset/texture/{archive:int}/{record:int}", (HttpContext ctx, int archive, int record) =>
{
    var png = assets.GetTexturePng(archive, record);
    if (png == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(png, "image/png");
});

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

    // Camera ring the client last reported (region mode). viewMx < 0 ⇒ unknown:
    // send all agents (back-compat, and town mode never sends a view).
    int viewMx = -1, viewMy = -1, viewR = 0;

    // ODD snapshot: the single agent this connection is watching (-1 = none).
    int watchedId = -1;

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
                        var (ex, ey, ez) = wholeRegion ? world.Geography.GeoRemap(e.X, e.Z) : (e.X, 0f, e.Z);
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
                    // Communications spoken since the previous frame (drained sim-side buffer —
                    // structure only, no prose; the client resolves atom ids via /asset/atoms).
                    utterances = (snap.Utterances ?? System.Array.Empty<WorldRunner.UtteranceRow>())
                        .Select(u => new
                        {
                            speaker = u.Speaker,
                            audience = u.Audience,
                            channel = u.Channel,
                            act = u.Act,
                            subject = u.Subject,
                            confidence = System.Math.Round(u.Confidence, 3),
                            content = u.Content.Select(c => new { atom = c.atom, value = System.Math.Round(c.value, 3) }),
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

                case "pickBuilding"
                    when doc.RootElement.TryGetProperty("x", out var pbx)
                      && doc.RootElement.TryGetProperty("z", out var pbz):
                    await Send(JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "building", building = PickBuilding(pbx.GetDouble(), pbz.GetDouble()) }, jsonOptions));
                    break;

                // Camera ring (region mode): filter agent snapshots to the pixels the
                // client's viewport covers, so a region doesn't stream every settlement's
                // agents at once. The pump reads viewMx/viewMy/viewR on its next frame.
                case "view"
                    when doc.RootElement.TryGetProperty("mx", out var vmx)
                      && doc.RootElement.TryGetProperty("my", out var vmy):
                    viewMx = vmx.GetInt32();
                    viewMy = vmy.GetInt32();
                    viewR = doc.RootElement.TryGetProperty("r", out var vr) ? vr.GetInt32() : 6;
                    break;

                case "speed" when doc.RootElement.TryGetProperty("scale", out var sProp):
                    runner.SetTickRate((int)sProp.GetDouble());   // ticks/sec; -1 = unlimited
                    break;

                case "watch":
                    // Stop watching whatever this connection watched before.
                    if (watchedId >= 0)
                    {
                        OddSystem.SnapshotWatch.TryRemove(new EntityId(watchedId), out _);
                        OddSystem.StructuredSnapshots.TryRemove(new EntityId(watchedId), out _);
                        watchedId = -1;
                    }
                    if (doc.RootElement.TryGetProperty("id", out var widProp)
                        && widProp.ValueKind == JsonValueKind.Number)
                    {
                        watchedId = widProp.GetInt32();
                        OddSystem.SnapshotWatch[new EntityId(watchedId)] = 1;
                    }
                    break;
            }
        }
    }
    catch (WebSocketException) { /* client went away mid-frame */ }

    if (watchedId >= 0)
    {
        OddSystem.SnapshotWatch.TryRemove(new EntityId(watchedId), out _);
        OddSystem.StructuredSnapshots.TryRemove(new EntityId(watchedId), out _);
    }

    try { await pump; } catch { /* pump dies with the socket */ }
});

Console.WriteLine($"spectating {worldRegionName} / {worldName} "
    + $"({worldCivilians} civilians) at http://localhost:{port}");
app.Run();

// --- local helpers (read the new registries) ---

// atomType:int -> concept name, from the catalogs utterance content uses. PlaceAtoms:
// Danger/Provisions (graded place facts) + the BuildingKind presence range (Kind:Tavern…).
// PerceivableAtoms: entity Kind/Role/Race/Activity ranges. Keys are stringified ints so the
// result serialises as a plain { "6001": "Danger", ... } object.
static Dictionary<string, string> BuildAtomNames()
{
    var t = new Dictionary<string, string>();
    void Add(int id, string name) => t[id.ToString()] = name;

    // PLACES — graded facts and the per-BuildingKind presence atoms.
    Add(PlaceAtoms.Danger.Value, "Danger");
    Add(PlaceAtoms.Provisions.Value, "Provisions");
    foreach (BuildingKind k in System.Enum.GetValues(typeof(BuildingKind)))
    {
        if (k == BuildingKind.None) continue;
        Add(PlaceAtoms.Kind(k).Value, "Kind:" + k);
    }

    // PERCEIVABLES — entity attributes (cheap; the same ranges utterance content may carry).
    foreach (EntityKind k in System.Enum.GetValues(typeof(EntityKind)))
        Add(PerceivableAtoms.Kind(k).Value, "Entity:" + k);
    foreach (ResidentRole r in System.Enum.GetValues(typeof(ResidentRole)))
        Add(PerceivableAtoms.Role(r).Value, "Role:" + r);
    foreach (ActivityKind a in System.Enum.GetValues(typeof(ActivityKind)))
    {
        if (a == ActivityKind.None) continue;
        Add(PerceivableAtoms.Activity(a).Value, "Doing:" + a);
    }
    return t;
}

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
            object odd = null;
            if (OddSystem.StructuredSnapshots.TryGetValue(id, out var snap) && snap.Nodes != null)
                odd = new
                {
                    ts = $"{snap.Hour:00}:{snap.Minute:00}",
                    chosen = snap.Chosen,
                    nodes = snap.Nodes.Select(nd => new
                    {
                        parent = nd.Parent, verb = nd.Verb, direct = nd.Direct,
                        prop = nd.Prop, total = nd.Total, terminal = nd.Terminal,
                    }),
                };
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
                odd = odd,
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

            // Residents (and surnames for the building's display name) in one scan.
            var residents = new List<object>();
            string keeperSurname = null, anySurname = null;
            foreach (var kv in world.Residency.All)
            {
                if (kv.Value == null || kv.Value.BuildingIndex != i) continue;
                string rname = world.Identity.TryGet(kv.Key, out var rid) ? rid.Name : null;
                residents.Add(new { id = kv.Key.Value, name = rname, role = kv.Value.Role.ToString() });
                if (world.Lineage.TryGet(kv.Key, out var lin) && lin != null && !string.IsNullOrEmpty(lin.Surname))
                {
                    if (kv.Value.Role == ResidentRole.Keeper && keeperSurname == null) keeperSurname = lin.Surname;
                    if (anySurname == null) anySurname = lin.Surname;
                }
            }

            // Workers: employees whose employer's residency points at this building.
            var workers = new List<object>();
            foreach (var kv in world.Employment.All)
            {
                var emp = kv.Value;
                if (emp == null || emp.Employer.IsNone) continue;
                if (world.Residency.TryGet(emp.Employer, out var er) && er != null && er.BuildingIndex == i)
                    workers.Add(new { id = kv.Key.Value, name = world.Identity.TryGet(kv.Key, out var wid) ? wid.Name : null });
            }

            // Settlement membership (the loader tags each building to one settlement).
            object settlement = null;
            foreach (var s in world.Settlements.All)
                if (s.Buildings.Contains(i))
                {
                    settlement = new
                    {
                        name = s.Name,
                        kind = s.Kind.ToString(),
                        residents = s.Residents.Count,
                        treasury = world.Treasury.Get(s.Treasury),
                    };
                    break;
                }

            string kindLabel = b.Kind.ToString();
            bool isHome = (b.Kind >= BuildingKind.House1 && b.Kind <= BuildingKind.House6)
                          || b.Kind == BuildingKind.HouseForSale;
            string name = keeperSurname != null ? $"{keeperSurname}'s {kindLabel}"
                        : (isHome && anySurname != null) ? $"{anySurname} residence"
                        : kindLabel;
            string production = b.Kind switch
            {
                BuildingKind.Farm => "provisions",
                BuildingKind.Fishery => "fish",
                BuildingKind.Mine => "ore",
                BuildingKind.Pasture => "wool",
                BuildingKind.Weaver => "cloth",
                BuildingKind.ClothingStore => "attire",
                _ => null,
            };

            return new
            {
                i, kind = kindLabel, quality = b.Quality, faction = b.FactionId,
                x = b.X, z = b.Z, name, production, residents, workers, settlement,
            };
        }
        catch (InvalidOperationException) { Thread.Sleep(2); }
    }
    return new { i, busy = true };
}

// Resolve a clicked world point to the nearest building, then return its detail.
// Region mode places building meshes (and agents) in geo-remapped world space, so
// remap each building's town-local origin before comparing; town mode compares raw.
object PickBuilding(double x, double z)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            int bestI = -1; double best = double.MaxValue;
            foreach (var kv in world.Buildings.All)
            {
                double bx = kv.Value.X, bz = kv.Value.Z;
                if (wholeRegion)
                {
                    var (gx, _, gz) = world.Geography.GeoRemap(kv.Value.X, kv.Value.Z);
                    bx = gx; bz = gz;
                }
                double dx = bx - x, dz = bz - z, d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; bestI = kv.Key; }
            }
            if (bestI < 0) return new { missing = true };
            return InspectBuilding(bestI);
        }
        catch (InvalidOperationException) { Thread.Sleep(2); }
    }
    return new { busy = true };
}
