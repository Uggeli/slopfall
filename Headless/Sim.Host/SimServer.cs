using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DaggerfallWorkshop.Sim.Net;

namespace DaggerfallWorkshop.Sim.Host
{
    /// The dedicated server, embryo edition: town sim on its own thread, TCP
    /// fan-out of RenderSnapshots to every connected client. Connect handshake
    /// sends the static world once; after that it's snapshots only. No input
    /// path yet — clients are spectators until player verbs exist.
    public static class SimServer
    {
        const int SendIntervalMs = 50;

        static int _clients;

        public static int Run(string regionName, string locationName, float timeScale, int port)
        {
            var boot = TownBoot.Create(regionName, locationName, timeScale);
            var world = BuildWorldStatic(boot);

            var publisher = new SnapshotPublisher();
            var thread = new SimThread(boot.Loop, boot.Ctx, publisher);
            thread.Start();

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine("serving " + world.RegionName + " / " + world.Name
                + " on port " + port + " — " + boot.Town.Civilians + " civilians, q to stop");

            var acceptThread = new Thread(() => AcceptLoop(listener, world, publisher)) { IsBackground = true };
            acceptThread.Start();

            bool canReadKeys = !Console.IsInputRedirected;
            while (true)
            {
                if (canReadKeys && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q || key == ConsoleKey.Escape) break;
                }
                if (thread.LastException != null)
                {
                    Console.Error.WriteLine("sim thread crashed: " + thread.LastException);
                    return 1;
                }
                var snap = publisher.Latest;
                Console.Write("\rtick " + (snap != null ? snap.Tick : 0)
                    + "  clock " + (snap != null ? snap.Hour.ToString("00") + ":" + snap.Minute.ToString("00") : "--:--")
                    + "  clients " + Volatile.Read(ref _clients) + "   ");
                Thread.Sleep(250);
            }

            listener.Stop();
            thread.Stop();
            Console.WriteLine();
            return 0;
        }

        public static WorldStatic BuildWorldStatic(TownBoot.Boot boot)
        {
            var world = new WorldStatic
            {
                RegionName = boot.Town.RegionName,
                Name = boot.Town.Name,
                BlocksWide = boot.Town.BlocksWide,
                BlocksHigh = boot.Town.BlocksHigh,
            };
            world.Buildings.AddRange(boot.Ctx.Buildings.All);
            world.Buildings.Sort((a, b) => a.Key.CompareTo(b.Key));
            return world;
        }

        static void AcceptLoop(TcpListener listener, WorldStatic world, SnapshotPublisher publisher)
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }     // listener stopped

                var clientThread = new Thread(() => ServeClient(client, world, publisher)) { IsBackground = true };
                clientThread.Start();
            }
        }

        static void ServeClient(TcpClient client, WorldStatic world, SnapshotPublisher publisher)
        {
            var endpoint = client.Client.RemoteEndPoint;
            Interlocked.Increment(ref _clients);
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                Protocol.WriteHandshake(stream);
                Protocol.WriteWorldStatic(stream, world);

                long lastSent = -1;
                while (true)
                {
                    var snap = publisher.Latest;
                    if (snap != null && snap.Tick != lastSent)
                    {
                        Protocol.WriteSnapshot(stream, snap);
                        lastSent = snap.Tick;
                    }
                    Thread.Sleep(SendIntervalMs);
                }
            }
            catch (Exception)
            {
                // Disconnect, however it happened — drop the client quietly.
            }
            finally
            {
                Interlocked.Decrement(ref _clients);
                client.Close();
            }
        }
    }
}
