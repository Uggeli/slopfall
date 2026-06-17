using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DaggerfallWorkshop.Sim;
using Sim.AssetExport;

// Web spectator: pan over the living town in a browser, click a dude, see who
// he is. Usage: dotnet run <region> <location> [--port 8080] [--timescale 600]

string region = null, location = null;
int port = 8080;
float timeScale = 120f;     // 2 game-min per real second — watchable walking
bool wholeRegion = false;   // --region: spectate every settlement at once, not one town
int startHour = -1;         // --starthour H: boot the clock at hour H (else dawn)

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--timescale": timeScale = float.Parse(args[++i]); break;
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

// One boot, two scopes. Region mode loads every settlement into the combined grid
// (the same context the throughput bench runs); town mode loads one location. The
// static map payload, the asset endpoints, and the agent stream all read whichever
// scope booted — the client is identical either way.
SimBootResult boot;
string worldRegionName, worldName;
int worldBlocksWide, worldBlocksHigh, worldCivilians;
List<(string name, float ox, float oz)> settlements = null;
List<(float minX, float minZ, float maxX, float maxZ, float dx, float dz)> remap = null;
// Region-terrain streaming state: pixel bbox, tile size, shared datum, town pixels.
int rMx0 = 0, rMy0 = 0, rMx1 = 0, rMy1 = 0;
float rTileSize = 0f, rDatum = 0f;
List<(int mx, int my, string name, int w, int h)> rTowns = null;
(int mx, int my, string name, int w, int h) rCentre = default;
if (wholeRegion)
{
    boot = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, timeScale);
    worldRegionName = boot.Region.RegionName;
    worldName = boot.Region.Settlements + " settlements";
    worldBlocksWide = boot.Region.BlocksWide;
    worldBlocksHigh = boot.Region.BlocksHigh;
    worldCivilians = boot.Region.Civilians;

    // Place settlements at their TRUE overworld positions, not the sim's packed
    // grid. The sim packs them into a dense walkability grid for pathfinding; since
    // no agent yet crosses between settlements, where one sits in the world is purely
    // a render choice. So use the real map-pixel coordinates (one pixel == one
    // 819.2 m terrain tile) and remap each settlement's agents by the same delta —
    // the buildings get the geographic origin, the agents get (geo - packed).
    const float TileSize = 32768f * TownLayout.GlobalScale;  // 819.2 m, one map pixel
    const float BlockSide = 4096f * TownLayout.GlobalScale;  // 102.4 m (RMBDimension)
    var sAll = boot.Ctx.Settlements.All;
    const int TerrainPad = 3;   // pixels of wilderness/sea to keep around the towns
    int mx0 = int.MaxValue, my0 = int.MaxValue, mx1 = int.MinValue, my1 = int.MinValue;
    foreach (var s in sAll)
    {
        if (s.MapPixelX < mx0) mx0 = s.MapPixelX;
        if (s.MapPixelX > mx1) mx1 = s.MapPixelX;
        if (s.MapPixelY < my0) my0 = s.MapPixelY;
        if (s.MapPixelY > my1) my1 = s.MapPixelY;
    }
    mx0 = Math.Max(0, mx0 - TerrainPad); my0 = Math.Max(0, my0 - TerrainPad);
    mx1 = Math.Min(999, mx1 + TerrainPad); my1 = Math.Min(499, my1 + TerrainPad);
    settlements = new List<(string, float, float)>();
    remap = new List<(float, float, float, float, float, float)>();
    foreach (var s in sAll)
    {
        float geoX = (s.MapPixelX - mx0) * TileSize;
        float geoZ = (s.MapPixelY - my0) * TileSize;
        settlements.Add((s.Name, geoX, geoZ));
        remap.Add((s.OriginX, s.OriginZ,
                   s.OriginX + s.BlocksWide * BlockSide,
                   s.OriginZ + s.BlocksHigh * BlockSide,
                   geoX - s.OriginX, geoZ - s.OriginZ));
    }

    // Terrain-streaming metadata: pixel bbox + the town pixels (so the tile endpoint
    // knows which pixels flatten a location). The shared datum is set once the asset
    // service exists, below.
    rTileSize = TileSize;
    rMx0 = mx0; rMy0 = my0; rMx1 = mx1; rMy1 = my1;
    rTowns = sAll.Select(s => (s.MapPixelX, s.MapPixelY, s.Name, s.BlocksWide, s.BlocksHigh)).ToList();
    // The settlement nearest the bbox centre supplies the datum the region levels to.
    int cmx = (rMx0 + rMx1) / 2, cmy = (rMy0 + rMy1) / 2;
    rCentre = rTowns[0];
    int bestD = int.MaxValue;
    foreach (var t in rTowns)
    {
        int d = (t.mx - cmx) * (t.mx - cmx) + (t.my - cmy) * (t.my - cmy);
        if (d < bestD) { bestD = d; rCentre = t; }
    }
}
else
{
    boot = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, timeScale);
    worldRegionName = boot.Town.RegionName;
    worldName = boot.Town.Name;
    worldBlocksWide = boot.Town.BlocksWide;
    worldBlocksHigh = boot.Town.BlocksHigh;
    worldCivilians = boot.Town.Civilians;
}

// Optional: jump the clock to a given hour (e.g. midday) so the scene is lit
// regardless of the spectator's speed control.
if (startHour >= 0)
    boot.Ctx.Inputs.Enqueue(new SeedClockInput
    {
        Year = 405, Month = 0, Day = 3, Hour = startHour, Minute = 0, Second = 0f, TimeScale = timeScale,
    });

var publisher = new SnapshotPublisher();
var simThread = new SimThread(boot.Loop, boot.Ctx, publisher);
simThread.Start();

// IncludeFields: the sim's DTOs (EntityDetail etc.) use public fields, which
// System.Text.Json ignores by default.
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
var townGrid = boot.Ctx.TownGrid.Current;
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
    buildings = boot.Ctx.Buildings.All
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

// Asset service (render-client plane 1): decodes ARENA2 geometry/textures to
// glTF + PNG on demand. Geometry embeds in each model's glTF; textures are
// shared URLs so the browser caches each one town-wide.
var assets = new AssetService(SimBoot.DefaultArena2Path);

// Shared region datum: the centre settlement's flattened floor. Every streamed tile
// levels to this one value, so neighbouring tiles meet at a continuous seam.
if (wholeRegion && rTowns is { Count: > 0 })
    rDatum = assets.RegionTileFloor(region, rCentre.name, rCentre.w, rCentre.h);

var builder = WebApplication.CreateBuilder();
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
// 3x3 neighbours. Region mode: deferred — a full 819 m tile per packed settlement
// would overlap its neighbours at mismatched flatten-heights, so settlements render
// on no ground until slice 2 fits right-sized pads.
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
    float addX = (mx - rMx0) * rTileSize, addZ = (my - rMy0) * rTileSize;
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
// the settlement whose packed rectangle holds it, shift by that settlement's delta.
(float x, float z) GeoRemap(float x, float z)
{
    if (remap != null)
        foreach (var r in remap)
            if (x >= r.minX && x < r.maxX && z >= r.minZ && z < r.maxZ)
                return (x + r.dx, z + r.dz);
    return (x, z);
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
            var snap = publisher.Latest;
            if (snap != null && snap.Tick != lastTick)
            {
                lastTick = snap.Tick;
                var payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "snap",
                    tick = snap.Tick,
                    hour = snap.Hour,
                    minute = snap.Minute,
                    night = snap.IsNight,
                    sun = snap.SunIntensity,
                    weather = snap.Weather.ToString(),
                    speed = boot.Ctx.WorldClock.Current.TimeScale,
                    // Compact rows: [id, x, z, activity, phase, yaw, kind]
                    // (2D spectator reads [0..4]; 3D viewer also uses yaw+kind.)
                    // Region mode projects packed coords to the geographic world.
                    entities = snap.Entities.Select(e =>
                    {
                        var (ex, ez) = wholeRegion ? GeoRemap(e.X, e.Z) : (e.X, e.Z);
                        return new object[]
                            { e.Id, MathF.Round(ex, 1), MathF.Round(ez, 1), (int)e.Activity, (int)e.Phase,
                              MathF.Round(e.Yaw, 3), (int)e.Kind };
                    }),
                }, jsonOptions);
                await Send(payload);
            }
            await Task.Delay(200);
        }
    });

    // Receive loop: inspect requests — the first client→server channel.
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
                {
                    var detail = Inspector.Inspect(boot.Ctx, new EntityId(idProp.GetInt32()));
                    await Send(JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "detail", detail }, jsonOptions));
                    break;
                }
                case "inspectBuilding" when doc.RootElement.TryGetProperty("i", out var bProp):
                {
                    var building = Inspector.InspectBuilding(boot.Ctx, bProp.GetInt32());
                    await Send(JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "building", building }, jsonOptions));
                    break;
                }
                case "speed" when doc.RootElement.TryGetProperty("scale", out var sProp):
                {
                    // First control message a client sends: through the same
                    // InputBus player verbs will use.
                    boot.Ctx.Inputs.Enqueue(new SetTimeScaleInput { TimeScale = (float)sProp.GetDouble() });
                    break;
                }
            }
        }
    }
    catch (WebSocketException) { /* client went away mid-frame */ }

    try { await pump; } catch { /* pump dies with the socket */ }
});

Console.WriteLine($"spectating {worldRegionName} / {worldName} "
    + $"({worldCivilians} civilians) at http://localhost:{port}");
app.Run();
