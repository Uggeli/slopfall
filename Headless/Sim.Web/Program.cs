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

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--timescale": timeScale = float.Parse(args[++i]); break;
        default:
            if (region == null) region = args[i];
            else if (location == null) location = args[i];
            break;
    }
}
region ??= "Daggerfall";
location ??= "Gothway Garden";

var boot = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, timeScale);
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
    region = boot.Town.RegionName,
    name = boot.Town.Name,
    blocksWide = boot.Town.BlocksWide,
    blocksHigh = boot.Town.BlocksHigh,
    civilians = boot.Town.Civilians,
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
    var town = assets.GetTown(region, location);
    ctx.Response.Headers.CacheControl = "no-cache";   // live aggregate; evolves during dev
    return Results.Json(town, jsonOptions);
});

// Terrain heightfield + tilemap for the booted town's map pixel (render plane 2).
app.MapGet("/asset/terrain", (HttpContext ctx) =>
{
    var terrain = assets.GetTownTerrain(region, location);
    ctx.Response.Headers.CacheControl = "no-cache";   // live aggregate; evolves during dev
    return Results.Json(terrain, jsonOptions);
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
                    entities = snap.Entities.Select(e => new object[]
                        { e.Id, MathF.Round(e.X, 1), MathF.Round(e.Z, 1), (int)e.Activity, (int)e.Phase,
                          MathF.Round(e.Yaw, 3), (int)e.Kind }),
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

Console.WriteLine($"spectating {boot.Town.RegionName} / {boot.Town.Name} "
    + $"({boot.Town.Civilians} civilians) at http://localhost:{port}");
app.Run();
