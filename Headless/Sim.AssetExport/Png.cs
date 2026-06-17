// Minimal, dependency-free PNG encoder (8-bit RGBA, color type 6).
// Scanline filter 0 (None); IDAT compressed with ZLibStream (zlib-framed
// deflate, exactly what PNG wants). CRC32 implemented inline.

using System;
using System.IO;
using System.IO.Compression;

namespace Sim.AssetExport
{
    public static class Png
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Write top-down RGBA pixels (length = w*h*4) as a PNG.</summary>
        public static void Write(string path, int w, int h, byte[] rgba)
        {
            if (rgba == null || rgba.Length != w * h * 4)
                throw new ArgumentException($"rgba length {rgba?.Length} != {w}*{h}*4");

            using var fs = File.Create(path);
            fs.Write(Signature, 0, Signature.Length);

            // IHDR
            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)w);
            WriteBE(ihdr, 4, (uint)h);
            ihdr[8] = 8;   // bit depth
            ihdr[9] = 6;   // color type RGBA
            ihdr[10] = 0;  // compression
            ihdr[11] = 0;  // filter
            ihdr[12] = 0;  // interlace
            Chunk(fs, "IHDR", ihdr);

            // IDAT: each scanline prefixed with a filter-type byte (0 = None)
            int stride = w * 4;
            var raw = new byte[h * (1 + stride)];
            int o = 0;
            for (int y = 0; y < h; y++)
            {
                raw[o++] = 0;
                Array.Copy(rgba, y * stride, raw, o, stride);
                o += stride;
            }

            byte[] comp;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    z.Write(raw, 0, raw.Length);
                comp = ms.ToArray();
            }
            Chunk(fs, "IDAT", comp);
            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBE(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);

            var typeBytes = new byte[4];
            for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);

            uint crc = Crc32(0xffffffff, typeBytes, typeBytes.Length);
            crc = Crc32(crc, data, data.Length) ^ 0xffffffff;
            var crcBytes = new byte[4];
            WriteBE(crcBytes, 0, crc);
            s.Write(crcBytes, 0, 4);
        }

        private static void WriteBE(byte[] buf, int offset, uint v)
        {
            buf[offset] = (byte)(v >> 24);
            buf[offset + 1] = (byte)(v >> 16);
            buf[offset + 2] = (byte)(v >> 8);
            buf[offset + 3] = (byte)v;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc32(uint crc, byte[] data, int len)
        {
            for (int i = 0; i < len; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xff] ^ (crc >> 8);
            return crc;
        }
    }
}
