using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DaggerfallWorkshop.Sim;

// Web spectator: pan over the living town in a browser, click a dude, see who
// he is. Usage: dotnet run <region> <location> [--port 8080] [--timescale 600]

string region = null, location = null;
int port = 8080;
float timeScale = 600f;

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
var roadCells = new List<int>();
var townGrid = boot.Ctx.TownGrid.Current;
if (townGrid != null)
{
    for (int y = 0; y < townGrid.Height; y++)
        for (int x = 0; x < townGrid.Width; x++)
            if (townGrid.CostAt(x, y) == 1)
            {
                roadCells.Add(x);
                roadCells.Add(y);
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

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

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
                    // Compact rows: [id, x, z, activity, phase]
                    entities = snap.Entities.Select(e => new object[]
                        { e.Id, MathF.Round(e.X, 1), MathF.Round(e.Z, 1), (int)e.Activity, (int)e.Phase }),
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
            if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "inspect"
                && doc.RootElement.TryGetProperty("id", out var idProp))
            {
                var detail = Inspector.Inspect(boot.Ctx, new EntityId(idProp.GetInt32()));
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new { type = "detail", detail }, jsonOptions);
                await Send(payload);
            }
        }
    }
    catch (WebSocketException) { /* client went away mid-frame */ }

    try { await pump; } catch { /* pump dies with the socket */ }
});

Console.WriteLine($"spectating {boot.Town.RegionName} / {boot.Town.Name} "
    + $"({boot.Town.Civilians} civilians) at http://localhost:{port}");
app.Run();
