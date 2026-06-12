using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Sim.Tests
{
    /// End-to-end over the real Sim.Web binary: page serves, WebSocket
    /// handshake delivers world + snapshots, inspect round-trips. Skips
    /// silently if the Sim.Web build output or game data is missing.
    public class WebSpectatorTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static string WebDll => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../Sim.Web/bin/Debug/net10.0/DaggerfallSim.Web.dll"));

        static bool Available => Directory.Exists(Arena2) && File.Exists(WebDll);

        [Fact]
        public async Task PageServes_WorldArrives_SnapshotsFlow_InspectAnswers()
        {
            if (!Available) return;

            int port = 18000 + new Random().Next(2000);
            using var server = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{WebDll}\" Daggerfall \"Gothway Garden\" --port {port}",
                WorkingDirectory = Path.GetDirectoryName(WebDll),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            try
            {
                using var http = new HttpClient();
                string html = null;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    try
                    {
                        html = await http.GetStringAsync($"http://localhost:{port}/");
                        break;
                    }
                    catch (HttpRequestException) { await Task.Delay(500); }
                }
                Assert.NotNull(html);
                Assert.Contains("canvas", html);

                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

                var world = await ReceiveJson(ws);
                Assert.Equal("world", world.RootElement.GetProperty("type").GetString());
                Assert.Equal("Gothway Garden", world.RootElement.GetProperty("name").GetString());
                Assert.Equal(175, world.RootElement.GetProperty("buildings").GetArrayLength());

                var snap = await ReceiveJson(ws);
                Assert.Equal("snap", snap.RootElement.GetProperty("type").GetString());
                Assert.Equal(337, snap.RootElement.GetProperty("entities").GetArrayLength());

                var ask = Encoding.UTF8.GetBytes("{\"type\":\"inspect\",\"id\":1}");
                await ws.SendAsync(ask, WebSocketMessageType.Text, true, CancellationToken.None);

                // Snapshots keep flowing; scan until the detail reply shows up.
                for (int i = 0; i < 20; i++)
                {
                    var msg = await ReceiveJson(ws);
                    if (!msg.RootElement.TryGetProperty("type", out var typeProp))
                        Assert.Fail("typeless message: " + msg.RootElement.GetRawText().Substring(0, Math.Min(300, msg.RootElement.GetRawText().Length)));
                    if (typeProp.GetString() != "detail") continue;
                    string raw = msg.RootElement.GetRawText();
                    Assert.True(msg.RootElement.TryGetProperty("detail", out var detail),
                        "detail prop missing in: " + raw.Substring(0, Math.Min(300, raw.Length)));
                    Assert.True(detail.TryGetProperty("id", out var idEl),
                        "id missing in: " + raw.Substring(0, Math.Min(300, raw.Length)));
                    Assert.Equal(1, idEl.GetInt32());
                    Assert.False(string.IsNullOrEmpty(detail.GetProperty("name").GetString()));
                    return;     // success
                }
                Assert.Fail("no detail reply within 20 messages");
            }
            finally
            {
                try { server.Kill(entireProcessTree: true); } catch { }
            }
        }

        static async Task<JsonDocument> ReceiveJson(ClientWebSocket ws)
        {
            var buffer = new byte[1 << 20];
            int total = 0;
            while (true)
            {
                var seg = new ArraySegment<byte>(buffer, total, buffer.Length - total);
                var result = await ws.ReceiveAsync(seg, new CancellationTokenSource(15000).Token);
                total += result.Count;
                if (result.EndOfMessage) break;
            }
            return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, total));
        }
    }
}
