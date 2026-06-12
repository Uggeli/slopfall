using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DaggerfallWorkshop.Sim.Net;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Top-down TUI town view — the first sim client. One renderer, two
    /// transports: RunLocal boots the sim in-process; RunRemote consumes the
    /// same WorldStatic + RenderSnapshot stream over TCP. Both read only what
    /// the wire carries, so local mode exercises the network contract too.
    ///
    /// Interactive: view refreshes from the latest snapshot, q quits.
    /// --frames N: synchronous, prints N frames to stdout and exits —
    /// CI/agent-friendly.
    public static class TownViewer
    {
        const string Reset = "\x1b[0m";

        sealed class BuildingGlyph
        {
            public char Ch;
            public string Color;
            public int Priority;
        }

        public static int RunLocal(string regionName, string locationName, float timeScale, int frames)
        {
            var boot = SimBoot.CreateTown(DataProbe.Arena2Path, regionName, locationName, timeScale);
            var world = SimServer.BuildWorldStatic(boot);

            if (frames > 0)
            {
                const int ticksPerFrame = 60;
                return Frames(world, frames, () =>
                {
                    for (int i = 0; i < ticksPerFrame; i++)
                        boot.Loop.Step();
                    return SnapshotBuilder.Build(boot.Ctx, 0);
                });
            }

            var publisher = new SnapshotPublisher();
            var thread = new SimThread(boot.Loop, boot.Ctx, publisher);
            thread.Start();
            try
            {
                return Interactive(world, () => publisher.Latest, () => thread.LastException);
            }
            finally
            {
                thread.Stop();
            }
        }

        public static int RunRemote(string host, int port, int frames)
        {
            using (var client = new TcpClient())
            {
                client.Connect(host, port);
                client.NoDelay = true;
                var stream = client.GetStream();

                Protocol.ReadHandshake(stream);
                byte type = Protocol.ReadFrame(stream, out var payload);
                if (type != Protocol.FrameWorldStatic)
                    throw new System.IO.InvalidDataException("expected world static, got frame type " + type);
                var world = Protocol.ReadWorldStatic(payload);
                Console.Error.WriteLine("connected: " + world.RegionName + " / " + world.Name
                    + " (" + world.Buildings.Count + " buildings)");

                if (frames > 0)
                {
                    return Frames(world, frames, () =>
                    {
                        while (true)
                        {
                            byte t = Protocol.ReadFrame(stream, out var p);
                            if (t == Protocol.FrameSnapshot) return Protocol.ReadSnapshot(p);
                        }
                    });
                }

                // Interactive: reader thread feeds a local publisher — the
                // same single-slot pattern the sim thread uses in-process.
                var publisher = new SnapshotPublisher();
                Exception readError = null;
                var reader = new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            byte t = Protocol.ReadFrame(stream, out var p);
                            if (t == Protocol.FrameSnapshot)
                                publisher.Publish(Protocol.ReadSnapshot(p));
                        }
                    }
                    catch (Exception ex) { readError = ex; }
                }) { IsBackground = true };
                reader.Start();

                return Interactive(world, () => publisher.Latest, () => readError);
            }
        }

        static int Interactive(WorldStatic world, Func<RenderSnapshot> latest, Func<Exception> fault)
        {
            int cols = Math.Min(Console.WindowWidth - 2, 130);
            int rows = Math.Min(Console.WindowHeight - 5, 50);
            float worldW = world.BlocksWide * 4096f * TownLoader.GlobalScale;
            float worldH = world.BlocksHigh * 4096f * TownLoader.GlobalScale;
            var baseLayer = RenderBuildings(world.Buildings, worldW, worldH, cols, rows);

            Console.CursorVisible = false;
            Console.Clear();
            long lastTick = -1;
            bool canReadKeys = !Console.IsInputRedirected;
            try
            {
                while (true)
                {
                    if (canReadKeys && Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Q || key == ConsoleKey.Escape) break;
                    }
                    var error = fault();
                    if (error != null)
                    {
                        Console.Error.WriteLine("\nsource failed: " + error.Message);
                        return 1;
                    }

                    var snap = latest();
                    if (snap != null && snap.Tick != lastTick)
                    {
                        lastTick = snap.Tick;
                        Console.SetCursorPosition(0, 0);
                        Console.Write(RenderFrame(world, snap, baseLayer, worldW, worldH, cols, rows));
                    }
                    Thread.Sleep(50);
                }
            }
            finally
            {
                Console.CursorVisible = true;
                Console.WriteLine();
            }
            return 0;
        }

        static int Frames(WorldStatic world, int frames, Func<RenderSnapshot> next)
        {
            const int cols = 100, rows = 28;
            float worldW = world.BlocksWide * 4096f * TownLoader.GlobalScale;
            float worldH = world.BlocksHigh * 4096f * TownLoader.GlobalScale;
            var baseLayer = RenderBuildings(world.Buildings, worldW, worldH, cols, rows);

            for (int f = 0; f < frames; f++)
            {
                var snap = next();
                Console.Write(RenderFrame(world, snap, baseLayer, worldW, worldH, cols, rows));
                Console.WriteLine();
            }
            return 0;
        }

        static BuildingGlyph[,] RenderBuildings(List<KeyValuePair<int, BuildingRow>> buildings,
            float worldW, float worldH, int cols, int rows)
        {
            var layer = new BuildingGlyph[cols, rows];
            foreach (var kv in buildings)
            {
                var b = kv.Value;
                int cx = Clamp((int)(b.X / worldW * cols), 0, cols - 1);
                int cy = Clamp((int)(b.Z / worldH * rows), 0, rows - 1);
                var glyph = GlyphFor(b.Kind);
                if (layer[cx, cy] == null || glyph.Priority > layer[cx, cy].Priority)
                    layer[cx, cy] = glyph;
            }
            return layer;
        }

        static string RenderFrame(WorldStatic world, RenderSnapshot snap, BuildingGlyph[,] baseLayer,
            float worldW, float worldH, int cols, int rows)
        {
            // Entity overlay: walkers drawn last so motion is always visible.
            var overlay = new char[cols, rows];
            var overlayColor = new string[cols, rows];
            var byActivity = new Dictionary<ActivityKind, int>();
            int walking = 0;

            for (int i = 0; i < snap.Entities.Length; i++)
            {
                byActivity[snap.Entities[i].Activity] =
                    (byActivity.TryGetValue(snap.Entities[i].Activity, out var n) ? n : 0) + 1;
                if (snap.Entities[i].Phase == ActivityPhase.Moving) walking++;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < snap.Entities.Length; i++)
                {
                    var e = snap.Entities[i];
                    bool moving = e.Phase == ActivityPhase.Moving;
                    if ((pass == 1) != moving) continue;

                    int cx = Clamp((int)(e.X / worldW * cols), 0, cols - 1);
                    int cy = Clamp((int)(e.Z / worldH * rows), 0, rows - 1);
                    overlay[cx, cy] = moving ? 'o' : ActivityChar(e.Activity);
                    overlayColor[cx, cy] = moving ? "\x1b[97m" : ActivityColor(e.Activity);
                }
            }

            var sb = new StringBuilder(cols * rows * 4);

            sb.Append("\x1b[1m").Append(world.RegionName).Append(" / ").Append(world.Name).Append(Reset)
              .Append("   ").Append(snap.Hour.ToString("00")).Append(':').Append(snap.Minute.ToString("00"))
              .Append(snap.IsNight ? "  night" : "  day").Append("  sun ").Append(snap.SunIntensity.ToString("F2"))
              .Append("  ").Append(snap.Weather)
              .Append("  tick ").Append(snap.Tick)
              .Append("  pop ").Append(snap.Entities.Length)
              .Append("\x1b[K\n");

            sb.Append(HistogramLine(byActivity))
              .Append("\x1b[97mwalking ").Append(walking).Append(Reset).Append("\x1b[K\n");

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    if (overlay[x, y] != '\0')
                        sb.Append(overlayColor[x, y]).Append(overlay[x, y]).Append(Reset);
                    else if (baseLayer[x, y] != null)
                        sb.Append(baseLayer[x, y].Color).Append(baseLayer[x, y].Ch).Append(Reset);
                    else
                        sb.Append(' ');
                }
                sb.Append("\x1b[K\n");
            }

            sb.Append("\x1b[2mT tavern  + temple  G guild  B bank  $ shop  # house  ░ wall   ")
              .Append("z sleep  w work  e eat  s social  v visit  . idle/wander  o walking   q quit")
              .Append(Reset).Append("\x1b[K");
            return sb.ToString();
        }

        static string HistogramLine(Dictionary<ActivityKind, int> byActivity)
        {
            var sb = new StringBuilder();
            AppendCount(sb, byActivity, ActivityKind.Sleep, "sleep");
            AppendCount(sb, byActivity, ActivityKind.Work, "work");
            AppendCount(sb, byActivity, ActivityKind.EatHome, "eat-home");
            AppendCount(sb, byActivity, ActivityKind.EatTavern, "eat-tav");
            AppendCount(sb, byActivity, ActivityKind.Socialize, "social");
            AppendCount(sb, byActivity, ActivityKind.Visit, "visit");
            AppendCount(sb, byActivity, ActivityKind.Wander, "wander");
            AppendCount(sb, byActivity, ActivityKind.Idle, "idle");
            return sb.ToString();
        }

        static void AppendCount(StringBuilder sb, Dictionary<ActivityKind, int> d, ActivityKind k, string label)
        {
            d.TryGetValue(k, out var n);
            sb.Append(label).Append(' ').Append(n.ToString().PadRight(5));
        }

        static char ActivityChar(ActivityKind kind)
        {
            switch (kind)
            {
                case ActivityKind.Sleep: return 'z';
                case ActivityKind.Work: return 'w';
                case ActivityKind.EatHome:
                case ActivityKind.EatTavern: return 'e';
                case ActivityKind.Socialize: return 's';
                case ActivityKind.Visit: return 'v';
                case ActivityKind.Chat: return 'c';
                case ActivityKind.SeekHelp: return '!';
                default: return '.';
            }
        }

        static string ActivityColor(ActivityKind kind)
        {
            switch (kind)
            {
                case ActivityKind.Sleep: return "\x1b[34m";       // blue
                case ActivityKind.Work: return "\x1b[33m";        // yellow
                case ActivityKind.EatHome:
                case ActivityKind.EatTavern: return "\x1b[32m";   // green
                case ActivityKind.Socialize: return "\x1b[35m";   // magenta
                case ActivityKind.Visit: return "\x1b[36m";       // cyan
                case ActivityKind.Chat: return "\x1b[95m";        // bright magenta
                case ActivityKind.SeekHelp: return "\x1b[91m";    // bright red
                default: return "\x1b[37m";                       // light gray
            }
        }

        static BuildingGlyph GlyphFor(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Tavern:       return new BuildingGlyph { Ch = 'T', Color = "\x1b[93m", Priority = 9 };
                case BuildingKind.Temple:       return new BuildingGlyph { Ch = '+', Color = "\x1b[96m", Priority = 8 };
                case BuildingKind.GuildHall:    return new BuildingGlyph { Ch = 'G', Color = "\x1b[95m", Priority = 8 };
                case BuildingKind.Bank:         return new BuildingGlyph { Ch = 'B', Color = "\x1b[92m", Priority = 8 };
                case BuildingKind.Palace:       return new BuildingGlyph { Ch = 'P', Color = "\x1b[97m", Priority = 8 };
                case BuildingKind.Alchemist:
                case BuildingKind.Armorer:
                case BuildingKind.Bookseller:
                case BuildingKind.ClothingStore:
                case BuildingKind.FurnitureStore:
                case BuildingKind.GemStore:
                case BuildingKind.GeneralStore:
                case BuildingKind.Library:
                case BuildingKind.PawnShop:
                case BuildingKind.WeaponSmith:  return new BuildingGlyph { Ch = '$', Color = "\x1b[36m", Priority = 7 };
                case BuildingKind.House1:
                case BuildingKind.House2:
                case BuildingKind.House3:
                case BuildingKind.House4:
                case BuildingKind.House5:
                case BuildingKind.House6:
                case BuildingKind.HouseForSale: return new BuildingGlyph { Ch = '#', Color = "\x1b[90m", Priority = 3 };
                case BuildingKind.Town23:
                case BuildingKind.None:         return new BuildingGlyph { Ch = '░', Color = "\x1b[90m", Priority = 1 };
                default:                        return new BuildingGlyph { Ch = '?', Color = "\x1b[90m", Priority = 2 };
            }
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
