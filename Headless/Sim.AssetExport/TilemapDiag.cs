// Diagnostic: composite a terrain tile's painted tilemap into a top-down PNG, the
// same way the web client builds the ground mesh — so autotiling orientation can be
// SEEN in isolation (no heightfield, camera, or lighting). Renders the current client
// logic and an 8-way dihedral grid of a high-transition crop, so the orientation that
// makes coastlines/roads connect is immediately visible.

using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace Sim.AssetExport
{
    internal static class TilemapDiag
    {
        const int TDim = 128;          // tilemap cells per tile (matches TerrainTile)
        const int TS = 64;             // ground tile record size

        // Build the 4 orientation bands for each ground record, EXACTLY as
        // AssetService.GetGroundAtlas / the client: band o = Rotate90 applied o times.
        static byte[][][] BuildOrientedTiles(string arena2, int archive, out int count, bool flipBase = false)
        {
            var tex = new TextureFile(Path.Combine(arena2, TextureFile.IndexToFileName(archive)), FileUsage.UseMemory, true);
            tex.LoadPalette(Path.Combine(arena2, tex.PaletteName));
            count = Math.Min(56, tex.RecordCount);
            var tiles = new byte[count][][];     // [rec][o] -> TS*TS*4
            for (int rec = 0; rec < count; rec++)
            {
                tiles[rec] = new byte[4][];
                DFBitmap bmp;
                try { bmp = tex.GetDFBitmap(rec, 0); } catch { tiles[rec] = null; continue; }
                if (bmp?.Data == null || bmp.Width == 0) { tiles[rec] = null; continue; }
                var rgba = TextureDecode.Rgba(bmp, tex, out int w, out int h);
                var baseTile = new byte[TS * TS * 4];
                for (int y = 0; y < TS; y++)
                {
                    int sy = h == TS ? y : y * h / TS;
                    for (int x = 0; x < TS; x++)
                    {
                        int sx = w == TS ? x : x * w / TS;
                        int s = (sy * w + sx) * 4, d = (y * TS + x) * 4;
                        baseTile[d] = rgba[s]; baseTile[d + 1] = rgba[s + 1];
                        baseTile[d + 2] = rgba[s + 2]; baseTile[d + 3] = rgba[s + 3];
                    }
                }
                // DFU's GetColor32 V-flips the bitmap (Unity bottom-up) BEFORE baking the
                // rotation variants; our atlas keeps it top-down. Replicate the flip to test.
                if (flipBase) baseTile = FlipVtile(baseTile, TS);
                var cur = baseTile;
                for (int o = 0; o < 4; o++) { tiles[rec][o] = cur; cur = Rotate90(cur, TS); }
            }
            return tiles;
        }

        static byte[] FlipVtile(byte[] src, int n)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int s = ((n - 1 - y) * n + x) * 4, d = (y * n + x) * 4;
                    dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
                }
            return dst;
        }

        // dst(x,y) = src(y, N-1-x)  — matches AssetService.Rotate90 / DFU RotateColors.
        static byte[] Rotate90(byte[] src, int n)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = y, sy = n - 1 - x;
                    int s = (sy * n + sx) * 4, d = (y * n + x) * 4;
                    dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
                }
            return dst;
        }

        // Apply one of the 8 dihedral symmetries to an NxN RGBA tile (for the variant grid).
        // mode: 0=id 1=rot90 2=rot180 3=rot270 4=flipU 5=flipV 6=transpose 7=anti-transpose
        static byte[] Dihedral(byte[] src, int n, int mode)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx, sy;
                    switch (mode)
                    {
                        case 1: sx = y; sy = n - 1 - x; break;             // rot90  (dst=src(y,N-1-x))
                        case 2: sx = n - 1 - x; sy = n - 1 - y; break;     // rot180
                        case 3: sx = n - 1 - y; sy = x; break;             // rot270
                        case 4: sx = n - 1 - x; sy = y; break;             // flip horizontal (u)
                        case 5: sx = x; sy = n - 1 - y; break;             // flip vertical (v)
                        case 6: sx = y; sy = x; break;                     // transpose (swap u,v)
                        case 7: sx = n - 1 - y; sy = n - 1 - x; break;     // anti-transpose
                        default: sx = x; sy = y; break;                    // identity
                    }
                    int s = (sy * n + sx) * 4, d = (y * n + x) * 4;
                    dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
                }
            return dst;
        }

        static readonly string[] ModeName = { "identity(current)", "rot90", "rot180", "rot270", "flipU", "flipV", "transpose", "anti-transpose" };

        // Composite a tilemap crop into `dst` (dstW wide) at pixel offset (ox,oy),
        // `cellPx` per cell, applying the current client orientation (band o = rot +
        // 2*flip) PLUS an optional extra dihedral transform `mode` (mode 0 = exactly
        // the current client). Cell (cx,cy) -> block (cx,cy): cx is X (right), cy is Z
        // (down), matching buildTileMesh.
        static void CompositeInto(byte[] dst, int dstW, int ox, int oy,
            byte[] tilemap, byte[][][] tiles, int cellPx, int mode, int cx0, int cy0, int cw, int ch)
        {
            for (int j = 0; j < ch; j++)
                for (int i = 0; i < cw; i++)
                {
                    int cx = cx0 + i, cy = cy0 + j;
                    byte b = tilemap[cx * TDim + cy];
                    int rec = b & 63, o = ((b & 64) != 0 ? 1 : 0) + ((b & 128) != 0 ? 2 : 0);
                    byte[] src = rec < tiles.Length && tiles[rec] != null ? tiles[rec][o] : null;
                    if (src != null && mode != 0) src = Dihedral(src, TS, mode);
                    for (int py = 0; py < cellPx; py++)
                    {
                        int ty = py * TS / cellPx;
                        for (int px = 0; px < cellPx; px++)
                        {
                            int tx = px * TS / cellPx;
                            int d = ((oy + j * cellPx + py) * dstW + (ox + i * cellPx + px)) * 4;
                            if (src == null) { dst[d + 3] = 255; continue; }   // black (missing rec)
                            int s = (ty * TS + tx) * 4;
                            dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = 255;
                        }
                    }
                }
        }

        static byte[] Composite(byte[] tilemap, byte[][][] tiles, int cellPx, int mode,
            int cx0, int cy0, int cw, int ch)
        {
            int W = cw * cellPx, H = ch * cellPx;
            var img = new byte[W * H * 4];
            CompositeInto(img, W, 0, 0, tilemap, tiles, cellPx, mode, cx0, cy0, cw, ch);
            return Png.Encode(W, H, img);
        }

        // Two crops side by side (left = tilesA, right = tilesB), both mode 0.
        static void CompareSideBySide(string path, byte[] tilemap, byte[][][] tilesA, byte[][][] tilesB,
            int cellPx, int cx0, int cy0, int cw, int ch)
        {
            int pw = cw * cellPx, ph = ch * cellPx, gap = 10;
            int W = pw * 2 + gap, H = ph;
            var img = new byte[W * H * 4];
            for (int i = 0; i < img.Length; i += 4) { img[i] = 40; img[i + 1] = 40; img[i + 2] = 48; img[i + 3] = 255; }
            CompositeInto(img, W, 0, 0, tilemap, tilesA, cellPx, 0, cx0, cy0, cw, ch);
            CompositeInto(img, W, pw + gap, 0, tilemap, tilesB, cellPx, 0, cx0, cy0, cw, ch);
            File.WriteAllBytes(path, Png.Encode(W, H, img));
        }

        // The final rendered 64x64 for one cell: oriented band (rot+2*flip) + extra mode.
        static byte[] CellTile(byte[] tilemap, byte[][][] tiles, int cx, int cy, int mode)
        {
            byte b = tilemap[cx * TDim + cy];
            int rec = b & 63, o = ((b & 64) != 0 ? 1 : 0) + ((b & 128) != 0 ? 2 : 0);
            byte[] src = rec < tiles.Length && tiles[rec] != null ? tiles[rec][o] : null;
            if (src != null && mode != 0) src = Dihedral(src, TS, mode);
            return src;
        }

        // Edge-continuity score for a mode: sum of |RGB| mismatch across every shared
        // edge between adjacent rendered cells. Two adjacent cells are defined by the
        // SAME pair of corner classifications, so a correct autotiling places the
        // transition at the same spot on the shared edge -> matching edge pixels ->
        // low score. A wrong rotation/reflection misaligns them -> high score. The
        // minimum over modes is the orientation that actually connects.
        static long ScoreMode(byte[] tilemap, byte[][][] tiles, int mode)
        {
            long sum = 0;
            for (int cx = 0; cx < TDim; cx++)
                for (int cy = 0; cy < TDim; cy++)
                {
                    var a = CellTile(tilemap, tiles, cx, cy, mode);
                    if (a == null) continue;
                    if (cx + 1 < TDim)   // right neighbour: a's right column vs b's left column
                    {
                        var b = CellTile(tilemap, tiles, cx + 1, cy, mode);
                        if (b != null)
                            for (int y = 0; y < TS; y++)
                            {
                                int ia = (y * TS + (TS - 1)) * 4, ib = (y * TS + 0) * 4;
                                sum += Math.Abs(a[ia] - b[ib]) + Math.Abs(a[ia + 1] - b[ib + 1]) + Math.Abs(a[ia + 2] - b[ib + 2]);
                            }
                    }
                    if (cy + 1 < TDim)   // bottom neighbour: a's bottom row vs b's top row
                    {
                        var b = CellTile(tilemap, tiles, cx, cy + 1, mode);
                        if (b != null)
                            for (int x = 0; x < TS; x++)
                            {
                                int ia = ((TS - 1) * TS + x) * 4, ib = (0 * TS + x) * 4;
                                sum += Math.Abs(a[ia] - b[ib]) + Math.Abs(a[ia + 1] - b[ib + 1]) + Math.Abs(a[ia + 2] - b[ib + 2]);
                            }
                    }
                }
            return sum;
        }

        // Pick a cw x ch cell window with the most distinct rec ids (= most transitions).
        static (int cx, int cy) FindBusyCrop(byte[] tilemap, int cw, int ch)
        {
            int bestCx = 0, bestCy = 0, best = -1;
            for (int cy = 0; cy + ch < TDim; cy += 4)
                for (int cx = 0; cx + cw < TDim; cx += 4)
                {
                    var seen = new bool[256];
                    int distinct = 0;
                    for (int j = 0; j < ch; j++)
                        for (int i = 0; i < cw; i++)
                        {
                            int b = tilemap[(cx + i) * TDim + (cy + j)];
                            if (!seen[b]) { seen[b] = true; distinct++; }
                        }
                    if (distinct > best) { best = distinct; bestCx = cx; bestCy = cy; }
                }
            return (bestCx, bestCy);
        }

        // --- Verbatim marching-squares lookup (copied from TerrainTile, which is a
        // verbatim port of DFU CreateLookupTable) so the synthetic test runs the exact
        // same byte production the real pipeline uses. ---
        static readonly byte[] Lookup = BuildLookup();
        static byte Mk(int i, bool r, bool f) { if (r) i += 64; if (f) i += 128; return (byte)i; }
        static byte[] BuildLookup()
        {
            var t = new byte[64];
            AddRange(t, 0, 1, 5, 48, false, 0); AddRange(t, 2, 1, 10, 51, true, 16);
            AddRange(t, 2, 3, 15, 53, false, 32); AddRange(t, 3, 3, 15, 53, true, 48);
            return t;
        }
        static void AddRange(byte[] t, int bs, int be, int ss, int sad, bool rev, int o)
        {
            if (rev) {
                t[o]=Mk(bs,false,false); t[o+1]=Mk(ss+2,true,true); t[o+2]=Mk(ss+2,false,false); t[o+3]=Mk(ss+1,true,true);
                t[o+4]=Mk(ss+2,false,true); t[o+5]=Mk(ss+1,false,true); t[o+6]=Mk(sad,true,false); t[o+7]=Mk(ss,true,true);
                t[o+8]=Mk(ss+2,true,false); t[o+9]=Mk(sad,false,false); t[o+10]=Mk(ss+1,false,false); t[o+11]=Mk(ss,false,false);
                t[o+12]=Mk(ss+1,true,false); t[o+13]=Mk(ss,false,true); t[o+14]=Mk(ss,true,false); t[o+15]=Mk(be,false,false);
            } else {
                t[o]=Mk(bs,false,false); t[o+1]=Mk(ss,true,false); t[o+2]=Mk(ss,false,true); t[o+3]=Mk(ss+1,true,false);
                t[o+4]=Mk(ss,false,false); t[o+5]=Mk(ss+1,false,false); t[o+6]=Mk(sad,false,false); t[o+7]=Mk(ss+2,true,false);
                t[o+8]=Mk(ss,true,true); t[o+9]=Mk(sad,true,false); t[o+10]=Mk(ss+1,false,true); t[o+11]=Mk(ss+2,false,true);
                t[o+12]=Mk(ss+1,true,true); t[o+13]=Mk(ss+2,false,false); t[o+14]=Mk(ss+2,true,true); t[o+15]=Mk(be,false,false);
            }
        }

        // Concentric-ring base field marched through the verbatim lookup -> tilemap.
        public static byte[] BuildSyntheticTilemap()
        {
            var baseType = new byte[TDim * TDim];
            double c = (TDim - 1) / 2.0;
            for (int cx = 0; cx < TDim; cx++)
                for (int cy = 0; cy < TDim; cy++)
                {
                    double r = Math.Sqrt((cx - c) * (cx - c) + (cy - c) * (cy - c));
                    baseType[cx * TDim + cy] = (byte)(r < 20 ? 0 : r < 38 ? 1 : r < 54 ? 2 : 3);
                }
            var tm = new byte[TDim * TDim];
            for (int cx = 0; cx < TDim; cx++)
                for (int cy = 0; cy < TDim; cy++)
                {
                    int b0 = baseType[cx * TDim + cy];
                    int b1 = baseType[Math.Min(TDim - 1, cx + 1) * TDim + cy];
                    int b2 = baseType[cx * TDim + Math.Min(TDim - 1, cy + 1)];
                    int b3 = baseType[Math.Min(TDim - 1, cx + 1) * TDim + Math.Min(TDim - 1, cy + 1)];
                    int shape = (b0 & 1) | (b1 & 1) << 1 | (b2 & 1) << 2 | (b3 & 1) << 3;
                    int ring = (b0 + b1 + b2 + b3) >> 2;
                    tm[cx * TDim + cy] = Lookup[shape | (ring << 4)];
                }
            return tm;
        }

        // Synthetic concentric rings (water<dirt<grass<stone) marched into a tilemap.
        // Correct orientation -> smooth circular bands; wrong -> jagged pinwheel.
        static void SyntheticTest(byte[][][] tiles, string outDir)
        {
            var tm = BuildSyntheticTilemap();
            // 8-way variant grid of the whole synthetic tile (5 px/cell -> 640 per panel).
            const int CELL = 5, PAD = 6, V = TDim * CELL;
            int gw = 4 * V + 5 * PAD, gh = 2 * V + 3 * PAD;
            var grid = new byte[gw * gh * 4];
            for (int i = 0; i < grid.Length; i += 4) { grid[i] = 20; grid[i + 1] = 24; grid[i + 2] = 30; grid[i + 3] = 255; }
            for (int mode = 0; mode < 8; mode++)
            {
                int gx = (mode % 4) * (V + PAD) + PAD, gy = (mode / 4) * (V + PAD) + PAD;
                CompositeInto(grid, gw, gx, gy, tm, tiles, CELL, mode, 0, 0, TDim, TDim);
            }
            File.WriteAllBytes(Path.Combine(outDir, "synthetic.png"), Png.Encode(gw, gh, grid));
            // Also a single big top-left quadrant of mode 0 to see the curve clearly.
            File.WriteAllBytes(Path.Combine(outDir, "synthetic_mode0.png"), Composite(tm, tiles, 16, 0, 0, 0, 64, 64));
            Console.WriteLine("wrote synthetic.png (8 variants of concentric rings; [0]=current client) + synthetic_mode0.png");
        }

        public static int Run(string arena2, string region, string location, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var maps = new MapsFile(Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var woods = new WoodsFile(Path.Combine(arena2, "WOODS.WLD"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);

            DFLocation loc = maps.GetLocation(region, location);
            if (!loc.Loaded) { Console.Error.WriteLine($"location not found: {region}/{location}"); return 1; }
            var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
            int worldClimate = maps.GetClimateIndex(pix.X, pix.Y);
            int groundArchive = MapsFile.GetWorldClimateSettings(worldClimate).GroundArchive;
            Console.WriteLine($"{region}/{location}  pixel=({pix.X},{pix.Y})  groundArchive={groundArchive}  loc={loc.Exterior.ExteriorData.Width}x{loc.Exterior.ExteriorData.Height} blocks");

            var tiles = BuildOrientedTiles(arena2, groundArchive, out int recCount);
            Console.WriteLine($"ground records: {recCount}");

            SyntheticTest(tiles, outDir);

            // Town tile (roads + flatten) and a wilderness neighbour (natural transitions).
            var town = TerrainTile.Generate(woods, pix.X, pix.Y, groundArchive,
                loc.Exterior.ExteriorData.Width, loc.Exterior.ExteriorData.Height, blocks, loc.Exterior.ExteriorData.BlockNames);
            var wild = TerrainTile.Generate(woods, pix.X + 1, pix.Y + 1, groundArchive, 0, 0, null, null, town.Floor);

            // Whole-tile overviews (current client logic), 8 px/cell.
            File.WriteAllBytes(Path.Combine(outDir, "town_art.png"), Composite(town.Tilemap, tiles, 8, 0, 0, 0, TDim, TDim));
            File.WriteAllBytes(Path.Combine(outDir, "wild_art.png"), Composite(wild.Tilemap, tiles, 8, 0, 0, 0, TDim, TDim));
            Console.WriteLine("wrote town_art.png, wild_art.png (whole tile, current client orientation)");

            // 8-way dihedral variant grid over the busiest crop of the wilderness tile.
            const int CW = 20, CH = 20, CELL = 26, PAD = 8;
            var (bx, by) = FindBusyCrop(wild.Tilemap, CW, CH);
            Console.WriteLine($"busy crop at cell ({bx},{by})");
            int vw = CW * CELL, vh = CH * CELL;
            int gridW = 4 * vw + 5 * PAD, gridH = 2 * (vh + 18) + PAD;
            var grid = new byte[gridW * gridH * 4];
            for (int i = 0; i < grid.Length; i += 4) { grid[i] = 24; grid[i + 1] = 28; grid[i + 2] = 36; grid[i + 3] = 255; }
            for (int mode = 0; mode < 8; mode++)
            {
                int gx = (mode % 4) * (vw + PAD) + PAD, gy = (mode / 4) * (vh + 18) + PAD;
                CompositeInto(grid, gridW, gx, gy, wild.Tilemap, tiles, CELL, mode, bx, by, CW, CH);
            }
            // Quantitative edge-continuity score per mode (lower = transitions connect).
            Console.WriteLine("\nedge-continuity score per within-tile transform (LOWER = more continuous = correct):");
            long bestScore = long.MaxValue; int bestMode = 0;
            long townBest = long.MaxValue; int townBestMode = 0;
            for (int mode = 0; mode < 8; mode++)
            {
                long sWild = ScoreMode(wild.Tilemap, tiles, mode);
                long sTown = ScoreMode(town.Tilemap, tiles, mode);
                Console.WriteLine($"  [{mode}] {ModeName[mode],-16}  wilderness={sWild,12:n0}   town={sTown,12:n0}");
                if (sWild < bestScore) { bestScore = sWild; bestMode = mode; }
                if (sTown < townBest) { townBest = sTown; townBestMode = mode; }
            }
            Console.WriteLine($"=> wilderness best: [{bestMode}] {ModeName[bestMode]};  town best: [{townBestMode}] {ModeName[townBestMode]}");
            Console.WriteLine("   (mode 0 = current client. If 0 wins, marching-squares orientation is already correct.)");

            File.WriteAllBytes(Path.Combine(outDir, "variants.png"), Png.Encode(gridW, gridH, grid));

            // Town cobblestone close-up, 8 dihedral variants. Cobblestone is DIRECTIONAL,
            // so unlike the symmetric terrain rings these panels differ visibly — the one
            // where brick lines run along the roads / road edges look right reveals whether
            // the (directional) location tiles need a transform the terrain doesn't.
            int tpx = (128 - loc.Exterior.ExteriorData.Width * 16) / 2;
            int tpy = (128 - loc.Exterior.ExteriorData.Height * 16) / 2;
            int TCW = 16, TCH = 16, TCELL = 36, tbx = tpx + 8, tby = tpy + 8;
            int tvw = TCW * TCELL, tvh = TCH * TCELL;
            int tgw = 4 * tvw + 5 * PAD, tgh = 2 * (tvh + 18) + PAD;
            var tgrid = new byte[tgw * tgh * 4];
            for (int i = 0; i < tgrid.Length; i += 4) { tgrid[i] = 24; tgrid[i + 1] = 28; tgrid[i + 2] = 36; tgrid[i + 3] = 255; }
            for (int mode = 0; mode < 8; mode++)
            {
                int gx = (mode % 4) * (tvw + PAD) + PAD, gy = (mode / 4) * (tvh + 18) + PAD;
                CompositeInto(tgrid, tgw, gx, gy, town.Tilemap, tiles, TCELL, mode, tbx, tby, TCW, TCH);
            }
            File.WriteAllBytes(Path.Combine(outDir, "town_variants.png"), Png.Encode(tgw, tgh, tgrid));
            Console.WriteLine($"wrote town_variants.png (cobblestone crop at cell ({tbx},{tby}); [0]=identity/current, [1]rot90 [2]rot180 [3]rot270 [4]flipU [5]flipV [6]transpose [7]anti-transpose)");

            // --- base-V-flip comparison (replicates DFU GetColor32) ---
            // Build a tile set with the base texture V-flipped before baking rotations,
            // then compare current (left) vs DFU-style flipped (right) side by side, for
            // both the synthetic rings (connectivity) and the town cobblestone (direction).
            var tilesF = BuildOrientedTiles(arena2, groundArchive, out _, flipBase: true);
            var synthTm = BuildSyntheticTilemap();
            CompareSideBySide(Path.Combine(outDir, "cmp_synth.png"), synthTm, tiles, tilesF, 12, 0, 0, 64, 64);
            CompareSideBySide(Path.Combine(outDir, "cmp_town.png"), town.Tilemap, tiles, tilesF, TCELL, tbx, tby, TCW, TCH);
            Console.WriteLine("wrote cmp_synth.png + cmp_town.png  (LEFT = current top-down atlas, RIGHT = DFU-style base-V-flip)");
            Console.WriteLine("wrote variants.png  (8 dihedral within-tile transforms; panel order:");
            Console.WriteLine("  [0]" + string.Join("  [", new[] { ModeName[0], "1]" + ModeName[1], "2]" + ModeName[2], "3]" + ModeName[3] }));
            Console.WriteLine("  [4]" + string.Join("  [", new[] { ModeName[4], "5]" + ModeName[5], "6]" + ModeName[6], "7]" + ModeName[7] }) + " )");
            return 0;
        }
    }
}
