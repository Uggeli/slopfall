using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DaggerfallWorkshop.Sim.Net
{
    /// Static world data sent once at connect — the client-side mirror of
    /// what TownViewer fetches from registries in-process.
    public sealed class WorldStatic
    {
        public string RegionName;
        public string Name;
        public int BlocksWide, BlocksHigh;
        public List<KeyValuePair<int, BuildingRow>> Buildings = new List<KeyValuePair<int, BuildingRow>>();
    }

    /// Length-prefixed binary frames over any Stream.
    ///
    ///   handshake: "DFSIM" + version byte (server -> client, once)
    ///   frame:     [int32 payloadLength][byte type][payload]
    ///
    /// Frame types: 1 = WorldStatic, 2 = Snapshot. The protocol is
    /// deliberately dumb — no compression, no delta encoding, no interest
    /// management. Each of those has an obvious seam when it matters.
    public static class Protocol
    {
        public const byte Version = 1;
        public const byte FrameWorldStatic = 1;
        public const byte FrameSnapshot = 2;

        static readonly byte[] Magic = { (byte)'D', (byte)'F', (byte)'S', (byte)'I', (byte)'M' };

        // ---- Handshake ----

        public static void WriteHandshake(Stream s)
        {
            s.Write(Magic, 0, Magic.Length);
            s.WriteByte(Version);
        }

        public static void ReadHandshake(Stream s)
        {
            var buf = ReadExact(s, Magic.Length + 1);
            for (int i = 0; i < Magic.Length; i++)
                if (buf[i] != Magic[i])
                    throw new InvalidDataException("not a DFSIM server");
            if (buf[Magic.Length] != Version)
                throw new InvalidDataException("protocol version mismatch: server " + buf[Magic.Length] + ", client " + Version);
        }

        // ---- WorldStatic ----

        public static void WriteWorldStatic(Stream s, WorldStatic world)
        {
            WriteFrame(s, FrameWorldStatic, w =>
            {
                WriteString(w, world.RegionName);
                WriteString(w, world.Name);
                w.Write(world.BlocksWide);
                w.Write(world.BlocksHigh);
                w.Write(world.Buildings.Count);
                for (int i = 0; i < world.Buildings.Count; i++)
                {
                    var b = world.Buildings[i];
                    w.Write(b.Key);
                    w.Write((int)b.Value.Kind);
                    w.Write(b.Value.FactionId);
                    w.Write(b.Value.Quality);
                    w.Write(b.Value.NameSeed);
                    w.Write(b.Value.X);
                    w.Write(b.Value.Z);
                    w.Write(b.Value.YRotation);
                    w.Write(b.Value.BlockX);
                    w.Write(b.Value.BlockY);
                    w.Write(b.Value.RecordIndex);
                }
            });
        }

        public static WorldStatic ReadWorldStatic(BinaryReader r)
        {
            var world = new WorldStatic
            {
                RegionName = ReadString(r),
                Name = ReadString(r),
                BlocksWide = r.ReadInt32(),
                BlocksHigh = r.ReadInt32(),
            };
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int key = r.ReadInt32();
                var row = new BuildingRow
                {
                    Kind = (BuildingKind)r.ReadInt32(),
                    FactionId = r.ReadInt32(),
                    Quality = r.ReadInt32(),
                    NameSeed = r.ReadInt32(),
                    X = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    YRotation = r.ReadSingle(),
                    BlockX = r.ReadInt32(),
                    BlockY = r.ReadInt32(),
                    RecordIndex = r.ReadInt32(),
                };
                world.Buildings.Add(new KeyValuePair<int, BuildingRow>(key, row));
            }
            return world;
        }

        // ---- Snapshot ----

        public static void WriteSnapshot(Stream s, RenderSnapshot snap)
        {
            WriteFrame(s, FrameSnapshot, w =>
            {
                w.Write(snap.Tick);
                w.Write(snap.SimSeconds);
                w.Write(snap.WallClockSeconds);
                w.Write((byte)snap.Hour);
                w.Write((byte)snap.Minute);
                w.Write(snap.IsNight);
                w.Write(snap.SunIntensity);
                w.Write((byte)snap.Weather);
                w.Write(snap.Entities.Length);
                for (int i = 0; i < snap.Entities.Length; i++)
                {
                    var e = snap.Entities[i];
                    w.Write(e.Id);
                    w.Write(e.X);
                    w.Write(e.Y);
                    w.Write(e.Z);
                    w.Write(e.Yaw);
                    w.Write((byte)e.Kind);
                    w.Write((byte)e.Activity);
                    w.Write((byte)e.Phase);
                }
            });
        }

        public static RenderSnapshot ReadSnapshot(BinaryReader r)
        {
            var snap = new RenderSnapshot
            {
                Tick = r.ReadInt64(),
                SimSeconds = r.ReadDouble(),
                WallClockSeconds = r.ReadDouble(),
                Hour = r.ReadByte(),
                Minute = r.ReadByte(),
                IsNight = r.ReadBoolean(),
                SunIntensity = r.ReadSingle(),
                Weather = (WeatherKind)r.ReadByte(),
            };
            int count = r.ReadInt32();
            var entities = new EntitySnap[count];
            for (int i = 0; i < count; i++)
            {
                entities[i] = new EntitySnap
                {
                    Id = r.ReadInt32(),
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    Yaw = r.ReadSingle(),
                    Kind = (EntityKind)r.ReadByte(),
                    Activity = (ActivityKind)r.ReadByte(),
                    Phase = (ActivityPhase)r.ReadByte(),
                };
            }
            snap.Entities = entities;
            return snap;
        }

        // ---- Frame plumbing ----

        /// Reads the next frame; returns its type and a reader positioned at
        /// the payload. Blocks until a full frame arrives.
        public static byte ReadFrame(Stream s, out BinaryReader payload)
        {
            var header = ReadExact(s, 5);
            int length = BitConverter.ToInt32(header, 0);
            byte type = header[4];
            if (length < 0 || length > 64 * 1024 * 1024)
                throw new InvalidDataException("bad frame length " + length);
            var body = ReadExact(s, length);
            payload = new BinaryReader(new MemoryStream(body, false), Encoding.UTF8);
            return type;
        }

        static void WriteFrame(Stream s, byte type, Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                    write(w);
                var payload = ms.ToArray();
                var header = new byte[5];
                BitConverter.GetBytes(payload.Length).CopyTo(header, 0);
                header[4] = type;
                s.Write(header, 0, header.Length);
                s.Write(payload, 0, payload.Length);
            }
        }

        static void WriteString(BinaryWriter w, string value) => w.Write(value ?? string.Empty);
        static string ReadString(BinaryReader r) => r.ReadString();

        static byte[] ReadExact(Stream s, int count)
        {
            var buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) throw new EndOfStreamException("connection closed");
                got += n;
            }
            return buf;
        }
    }
}
