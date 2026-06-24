// Mobile NPC sprites (render plane 3, the live-agent layer).
// Packs all animation records of a person/monster texture archive into one
// transparent sheet PNG, with metadata the client uses to pick a cell per
// (record, frame) and size the billboard.
//
// Universal mobile layout (20 records): 0-4 walk, 5-9 attack, 10-14 hurt, 15-19 idle.
// Direction model (DFU MobilePersonBillboard): records 0..4 = facing
// S/SW/W/NW/N; directions NE/E/SE reuse records 3/2/1 mirrored (client UV flip).
// World size = (W + W*scale/256) * 0.025 m.

using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace Sim.AssetExport
{
    public sealed class SpriteMeta
    {
        public int Archive;
        public int CellW, CellH;     // sheet cell size (px)
        public int Cols, Rows;       // Cols = max frames across records; Rows = record count (<=20)
        public int[] Frames;         // frame count per record row
        public float WorldW, WorldH; // billboard size in metres (record 0; legacy/fallback)
        public int[] RecW, RecH;     // native sprite pixel size per record row
        public float[] RecWorldW, RecWorldH; // billboard size in metres, per record
    }

    public static class SpritePerson
    {
        public const float GlobalScale = 0.025f;
        const int MaxRows = 20;      // records 0-4 walk, 5-9 attack, 10-14 hurt, 15-19 idle (universal mobile layout)

        // Civilian archives (race x gender x 4 outfit variants), guard excluded.
        // The client derives a person's archive by hashing their entity id.
        public static readonly int[] CivilianArchives =
        {
            381, 382, 383, 384,   // Redguard M
            395, 396, 397, 398,   // Redguard F
            387, 388, 389, 390,   // Nord M
            392, 393, 451, 452,   // Nord F
            385, 386, 391, 394,   // Breton M
            453, 454, 455, 456,   // Breton F
        };

        // City Watch guard mobile (archive 399) — uses the combat record layout
        // (walk 0-4, attack 5-9, idle 15-19), same as monsters.
        public static readonly int[] GuardArchives = { 399 };

        // Grounded creatures with the standard walk/attack/idle layout. Excludes
        // flyers and special-animation mobiles (imp/bat/harpy, ghost/wraith,
        // slaughterfish, seducer) whose motion or record layout differs. The client
        // picks one per creature by id-hash (until the sim specifies a creature type).
        public static readonly int[] MonsterArchives =
        {
            255,  // Rat
            257,  // Spriggan
            259,  // Grizzly Bear
            260,  // Sabertooth Tiger
            261,  // Giant Spider
            262,  // Orc
            263,  // Centaur
            270,  // Skeletal Warrior
            272,  // Zombie
            274,  // Mummy
        };

        /// <summary>Build the packed sheet PNG + metadata for a person archive.</summary>
        public static (byte[] png, SpriteMeta meta) Build(string arena2, int archive)
        {
            string path = Path.Combine(arena2, TextureFile.IndexToFileName(archive));
            var tex = new TextureFile(path, FileUsage.UseMemory, true);
            tex.LoadPalette(Path.Combine(arena2, tex.PaletteName));

            int rows = Math.Min(MaxRows, tex.RecordCount);
            var frames = new int[rows];
            var recW = new int[rows];
            var recH = new int[rows];
            var recWorldW = new float[rows];
            var recWorldH = new float[rows];
            int cellW = 1, cellH = 1, cols = 1;
            for (int r = 0; r < rows; r++)
            {
                int fc = Math.Max(1, tex.GetFrameCount(r));
                frames[r] = fc;
                cols = Math.Max(cols, fc);
                var sz = tex.GetSize(r);
                var sc = tex.GetScale(r);
                recW[r] = sz.Width;
                recH[r] = sz.Height;
                // Each record carries its own native size AND its own DF scale factor, so
                // the billboard for that record must use them (idle vs walk differ in both).
                recWorldW[r] = (sz.Width + sz.Width * sc.Width / 256f) * GlobalScale;
                recWorldH[r] = (sz.Height + sz.Height * sc.Height / 256f) * GlobalScale;
                cellW = Math.Max(cellW, sz.Width);
                cellH = Math.Max(cellH, sz.Height);
            }

            int aw = cols * cellW, ah = rows * cellH;
            var atlas = new byte[aw * ah * 4];   // transparent

            for (int r = 0; r < rows; r++)
            {
                for (int f = 0; f < frames[r]; f++)
                {
                    DFBitmap bmp;
                    try { bmp = tex.GetDFBitmap(r, f); } catch { continue; }
                    if (bmp?.Data == null || bmp.Width == 0) continue;
                    var rgba = TextureDecode.Rgba(bmp, tex, out int w, out int h, transparentIndex0: true);
                    // bottom-align in the cell (feet on the cell floor), centre horizontally
                    int ox = f * cellW + (cellW - w) / 2;
                    int oy = r * cellH + (cellH - h);
                    for (int y = 0; y < h; y++)
                    {
                        int dy = oy + y;
                        if (dy < 0 || dy >= ah) continue;
                        for (int x = 0; x < w; x++)
                        {
                            int dx = ox + x;
                            if (dx < 0 || dx >= aw) continue;
                            int s = (y * w + x) * 4, d = (dy * aw + dx) * 4;
                            atlas[d] = rgba[s]; atlas[d + 1] = rgba[s + 1];
                            atlas[d + 2] = rgba[s + 2]; atlas[d + 3] = rgba[s + 3];
                        }
                    }
                }
            }

            var meta = new SpriteMeta
            {
                Archive = archive,
                CellW = cellW, CellH = cellH, Cols = cols, Rows = rows,
                Frames = frames,
                WorldW = recWorldW[0], WorldH = recWorldH[0],
                RecW = recW, RecH = recH,
                RecWorldW = recWorldW, RecWorldH = recWorldH,
            };
            return (Png.Encode(aw, ah, atlas), meta);
        }
    }
}
