using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Net;
using Xunit;

namespace Sim.Tests
{
    /// The wire format IS the client contract — round-trips must be exact.
    public class ProtocolTests
    {
        static WorldStatic SampleWorld()
        {
            var world = new WorldStatic
            {
                RegionName = "Daggerfall",
                Name = "Gothway Garden",
                BlocksWide = 4,
                BlocksHigh = 3,
            };
            world.Buildings.Add(new KeyValuePair<int, BuildingRow>(0, new BuildingRow
            {
                Kind = BuildingKind.Tavern, FactionId = 510, Quality = 12, NameSeed = 4242,
                X = 51.2f, Z = 102.4f, YRotation = -90f, BlockX = 1, BlockY = 2, RecordIndex = 7,
            }));
            world.Buildings.Add(new KeyValuePair<int, BuildingRow>(173, new BuildingRow
            {
                Kind = BuildingKind.House2, X = 12.8f, Z = 64.0f,
            }));
            return world;
        }

        static RenderSnapshot SampleSnapshot()
        {
            return new RenderSnapshot
            {
                Tick = 123456789L,
                SimSeconds = 12345.678,
                WallClockSeconds = 99.5,
                Hour = 18, Minute = 42, IsNight = false,
                SunIntensity = 0.37f,
                Weather = WeatherKind.Rain,
                Entities = new[]
                {
                    new EntitySnap { Id = 1, X = 1.5f, Y = 0f, Z = 2.5f, Yaw = 180f,
                        Kind = EntityKind.CivilianNPC, Activity = ActivityKind.Socialize, Phase = ActivityPhase.Doing },
                    new EntitySnap { Id = 337, X = 300f, Y = 0f, Z = 250f, Yaw = -45f,
                        Kind = EntityKind.CivilianNPC, Activity = ActivityKind.Visit, Phase = ActivityPhase.Moving },
                },
            };
        }

        [Fact]
        public void WorldStatic_RoundTripsExactly()
        {
            var ms = new MemoryStream();
            Protocol.WriteWorldStatic(ms, SampleWorld());
            ms.Position = 0;

            byte type = Protocol.ReadFrame(ms, out var payload);
            Assert.Equal(Protocol.FrameWorldStatic, type);
            var world = Protocol.ReadWorldStatic(payload);

            Assert.Equal("Gothway Garden", world.Name);
            Assert.Equal(4, world.BlocksWide);
            Assert.Equal(2, world.Buildings.Count);
            Assert.Equal(0, world.Buildings[0].Key);
            Assert.Equal(BuildingKind.Tavern, world.Buildings[0].Value.Kind);
            Assert.Equal(510, world.Buildings[0].Value.FactionId);
            Assert.Equal(4242, world.Buildings[0].Value.NameSeed);
            Assert.Equal(51.2f, world.Buildings[0].Value.X);
            Assert.Equal(-90f, world.Buildings[0].Value.YRotation);
            Assert.Equal(173, world.Buildings[1].Key);
        }

        [Fact]
        public void Snapshot_RoundTripsExactly()
        {
            var ms = new MemoryStream();
            Protocol.WriteSnapshot(ms, SampleSnapshot());
            ms.Position = 0;

            byte type = Protocol.ReadFrame(ms, out var payload);
            Assert.Equal(Protocol.FrameSnapshot, type);
            var snap = Protocol.ReadSnapshot(payload);

            Assert.Equal(123456789L, snap.Tick);
            Assert.Equal(12345.678, snap.SimSeconds, 6);
            Assert.Equal(18, snap.Hour);
            Assert.Equal(42, snap.Minute);
            Assert.Equal(WeatherKind.Rain, snap.Weather);
            Assert.Equal(0.37f, snap.SunIntensity);
            Assert.Equal(2, snap.Entities.Length);
            Assert.Equal(337, snap.Entities[1].Id);
            Assert.Equal(ActivityKind.Visit, snap.Entities[1].Activity);
            Assert.Equal(ActivityPhase.Moving, snap.Entities[1].Phase);
            Assert.Equal(-45f, snap.Entities[1].Yaw);
        }

        [Fact]
        public void Handshake_RejectsWrongMagic()
        {
            var ms = new MemoryStream(new byte[] { (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/', 1 });
            Assert.Throws<InvalidDataException>(() => Protocol.ReadHandshake(ms));
        }

        [Fact]
        public async Task TcpLoopback_HandshakeStaticAndSnapshots_Flow()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var serverTask = Task.Run(() =>
            {
                using (var server = listener.AcceptTcpClient())
                {
                    var s = server.GetStream();
                    Protocol.WriteHandshake(s);
                    Protocol.WriteWorldStatic(s, SampleWorld());
                    Protocol.WriteSnapshot(s, SampleSnapshot());
                    var second = SampleSnapshot();
                    second.Tick++;
                    Protocol.WriteSnapshot(s, second);
                    s.Flush();
                }
            });

            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var s = client.GetStream();

                Protocol.ReadHandshake(s);
                Assert.Equal(Protocol.FrameWorldStatic, Protocol.ReadFrame(s, out var p1));
                var world = Protocol.ReadWorldStatic(p1);
                Assert.Equal("Gothway Garden", world.Name);

                Assert.Equal(Protocol.FrameSnapshot, Protocol.ReadFrame(s, out var p2));
                var snapA = Protocol.ReadSnapshot(p2);
                Assert.Equal(Protocol.FrameSnapshot, Protocol.ReadFrame(s, out var p3));
                var snapB = Protocol.ReadSnapshot(p3);
                Assert.Equal(snapA.Tick + 1, snapB.Tick);
            }

            await serverTask;
            listener.Stop();
        }
    }
}
