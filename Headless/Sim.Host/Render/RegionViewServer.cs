using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim.Host.Render
{
    /// Region viewer host: runs the parallel sim on its own thread, publishes an
    /// immutable snapshot after each batch, and serves it to a browser over HTTP.
    ///   GET  /            the canvas viewer page
    ///   GET  /static      one-time map geometry (buildings, grid, settlements)
    ///   GET  /snapshot    the latest per-tick snapshot (one-shot poll)
    ///   GET  /stream      server-sent events: a snapshot per published batch
    ///   POST /control     cmd=pause|play, or cmd=speed&v=<ticksPerBatch>
    /// The sim thread is the SOLE reader of the registries (in the post-Step read
    /// phase); HTTP threads only ever touch the already-built, frozen snapshot.
    public sealed class RegionViewServer
    {
        readonly SimWorld _world;
        readonly HttpListener _http = new HttpListener();
        readonly JsonSerializerOptions _json;
        readonly string _staticJson;

        volatile SnapshotDto _latest;
        volatile bool _paused;
        volatile int _ticksPerBatch = 200;   // sim speed knob
        long _tick;
        bool _running = true;

        public RegionViewServer(SimWorld world, int port)
        {
            _world = world;
            _json = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true,   // our DTOs expose public fields, not properties
                Converters = { new JsonStringEnumConverter() },
            };
            _staticJson = JsonSerializer.Serialize(SnapshotBuilder.BuildStatic(world), _json);
            _latest = SnapshotBuilder.BuildDynamic(world, 0, _paused);
            _http.Prefixes.Add($"http://localhost:{port}/");
            _http.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public void Run(int port)
        {
            _http.Start();
            var sim = new Thread(SimLoop) { IsBackground = true, Name = "sim" };
            sim.Start();
            Console.WriteLine($"region viewer: http://localhost:{port}/  (Ctrl+C to stop)");

            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _http.GetContext(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        void SimLoop()
        {
            while (_running)
            {
                if (_paused) { Thread.Sleep(20); continue; }
                int n = _ticksPerBatch;
                for (int i = 0; i < n; i++) { _world.Step(); _tick++; }
                // Build the snapshot here, on the sim thread, in the read phase.
                _latest = SnapshotBuilder.BuildDynamic(_world, _tick, _paused);
                Thread.Sleep(33);   // ~30 publishes/sec
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                switch (path)
                {
                    case "/":              Send(ctx, "text/html; charset=utf-8", ViewerPage.Html); break;
                    case "/static":        SendJson(ctx, _staticJson); break;
                    case "/snapshot":      SendJson(ctx, JsonSerializer.Serialize(_latest, _json)); break;
                    case "/stream":        Stream(ctx); break;
                    case "/control":       Control(ctx); break;
                    default:               ctx.Response.StatusCode = 404; ctx.Response.Close(); break;
                }
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }

        void Control(HttpListenerContext ctx)
        {
            var q = ctx.Request.QueryString;
            switch (q["cmd"])
            {
                case "pause": _paused = true; break;
                case "play": _paused = false; break;
                case "speed":
                    if (int.TryParse(q["v"], out var v)) _ticksPerBatch = Math.Clamp(v, 1, 20000);
                    break;
            }
            SendJson(ctx, $"{{\"paused\":{(_paused ? "true" : "false")},\"ticksPerBatch\":{_ticksPerBatch}}}");
        }

        /// Server-sent events: push the latest snapshot whenever its tick advances.
        void Stream(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            res.ContentType = "text/event-stream";
            res.Headers.Add("Cache-Control", "no-cache");
            res.SendChunked = true;
            var outp = res.OutputStream;
            long sent = -1;
            try
            {
                while (_running)
                {
                    var snap = _latest;
                    if (snap != null && snap.Tick != sent)
                    {
                        sent = snap.Tick;
                        var json = JsonSerializer.Serialize(snap, _json);
                        var bytes = Encoding.UTF8.GetBytes("data: " + json + "\n\n");
                        outp.Write(bytes, 0, bytes.Length);
                        outp.Flush();
                    }
                    else Thread.Sleep(16);
                }
            }
            catch { /* client disconnected */ }
            finally { try { res.Close(); } catch { } }
        }

        void SendJson(HttpListenerContext ctx, string json) => Send(ctx, "application/json", json);

        static void Send(HttpListenerContext ctx, string contentType, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}
