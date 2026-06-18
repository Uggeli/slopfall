// Per-map-pixel terrain tile (render plane 2, server side). Ports DFU's
// DefaultTerrainSampler heightfield generation as plain C# (no Burst/jobs/
// NativeArray) and flattens the ground under a location to match the sim's
// flat town at y=0. Emits a heightfield the client turns into a grid mesh.
//
// The map pixel is the natural streaming unit (one DFU terrain tile = one map
// pixel = 32768 classic units = 819.2 m = 8x8 RMB blocks), so this scales to
// region rendering by serving more tiles.
//
// FIDELITY NOTE: DFU's extra detail noise uses Unity's Mathf.PerlinNoise. Terrain
// here is render-only (not sim state — no replay determinism needed), so we use a
// standard Perlin for plausible fine detail; large-scale shape comes verbatim from
// the WOODS bicubic, which dominates.

using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace Sim.AssetExport
{
    public sealed class TerrainTileData
    {
        public int MapPixelX, MapPixelY;
        public int Dim;                 // HeightmapDimension (129)
        public float WorldSize;         // tile extent in metres (819.2)
        public float MaxHeight;         // 1539
        public float[] Heights;         // Dim*Dim, WORLD metres, offset so the town floor = 0
        public int GroundArchive;       // climate ground texture archive
        public int TileDim;             // tilemap resolution (128)
        public byte[] Tilemap;          // TileDim*TileDim lookup bytes: index=b&63, rot=b&64, flip=b&128
        public bool HasLocation;        // town tile? (flatten applied)
        public float Floor;             // this tile's mean normalised height (the shared datum)
        // Sim-world position of this tile's (x=0,y=0) corner, so the centered
        // location aligns to the sim's town origin.
        public float OriginX, OriginZ;
    }

    public static class TerrainTile
    {
        public const int HDim = 129;
        public const float GlobalScale = 0.025f;
        public const float TileWorldSize = 32768f * GlobalScale;   // 819.2 m
        public const float MaxTerrainHeight = 1539f;

        const float baseHeightScale = 8f;
        const float noiseMapScale = 4f;
        const float extraNoiseScale = 10f;
        const float scaledOceanElevation = 3.4f * baseHeightScale;
        const int RMBTilesPerBlock = 16;
        const int RMBTilesPerTerrain = 128;   // 8 blocks * 16 tiles
        const float BlockWorld = 4096f * GlobalScale;   // 102.4 m

        /// Build the heightfield for a map pixel. If a location sits here, pass its
        /// block width/height to flatten the city footprint to y=0 (matching the sim).
        public static TerrainTileData Generate(WoodsFile woods, int mx, int my, int groundArchive,
            int locWidth = 0, int locHeight = 0, BlocksFile blocks = null, string[] blockNames = null,
            float datumNorm = float.NaN)
        {
            int hDim = HDim;
            float div = (hDim - 1) / 3f;

            byte[] shm = woods.GetHeightMapValuesRange1Dim(mx - 2, my - 2, 4);
            byte[,] lhm2 = woods.GetLargeHeightMapValuesRange(mx - 1, my, 3);
            int ld = lhm2.GetLength(0);
            byte[] lhm = new byte[lhm2.Length];
            int li = 0;
            for (int yy = 0; yy < ld; yy++)
                for (int xx = 0; xx < ld; xx++)
                    lhm[li++] = lhm2[xx, yy];

            var norm = new float[hDim * hDim];   // normalised [0,1], DFU index = x*hDim + y
            double sum = 0;
            for (int x = 0; x < hDim; x++)
            {
                for (int y = 0; y < hDim; y++)
                {
                    float rx = x / div, ry = y / div;
                    int ix = (int)Math.Floor(rx), iy = (int)Math.Floor(ry);
                    float sfracx = (float)x / (hDim - 1), sfracy = (float)y / (hDim - 1);
                    float fracx = (x - ix * div) / div, fracy = (y - iy * div) / div;
                    float scaledHeight = 0;

                    float x1 = Cubic(S(shm, 0, 3), S(shm, 1, 3), S(shm, 2, 3), S(shm, 3, 3), sfracx);
                    float x2 = Cubic(S(shm, 0, 2), S(shm, 1, 2), S(shm, 2, 2), S(shm, 3, 2), sfracx);
                    float x3 = Cubic(S(shm, 0, 1), S(shm, 1, 1), S(shm, 2, 1), S(shm, 3, 1), sfracx);
                    float x4 = Cubic(S(shm, 0, 0), S(shm, 1, 0), S(shm, 2, 0), S(shm, 3, 0), sfracx);
                    scaledHeight += Cubic(x1, x2, x3, x4, sfracy) * baseHeightScale;

                    x1 = Cubic(L(lhm, ix, iy + 0, ld), L(lhm, ix + 1, iy + 0, ld), L(lhm, ix + 2, iy + 0, ld), L(lhm, ix + 3, iy + 0, ld), fracx);
                    x2 = Cubic(L(lhm, ix, iy + 1, ld), L(lhm, ix + 1, iy + 1, ld), L(lhm, ix + 2, iy + 1, ld), L(lhm, ix + 3, iy + 1, ld), fracx);
                    x3 = Cubic(L(lhm, ix, iy + 2, ld), L(lhm, ix + 1, iy + 2, ld), L(lhm, ix + 2, iy + 2, ld), L(lhm, ix + 3, iy + 2, ld), fracx);
                    x4 = Cubic(L(lhm, ix, iy + 3, ld), L(lhm, ix + 1, iy + 3, ld), L(lhm, ix + 2, iy + 3, ld), L(lhm, ix + 3, iy + 3, ld), fracx);
                    scaledHeight += Cubic(x1, x2, x3, x4, fracy) * noiseMapScale;

                    int noisex = mx * (hDim - 1) + x;
                    int noisey = (MapsFile.MaxMapPixelY - my) * (hDim - 1) + y;
                    float lowFreq = Noise.Get(noisex, noisey, 0.3f, 0.5f, 0.5f, 1);
                    float highFreq = Noise.Get(noisex, noisey, 0.9f, 0.5f, 0.5f, 1);
                    scaledHeight += (lowFreq * highFreq) * extraNoiseScale;

                    if (scaledHeight < scaledOceanElevation)
                        scaledHeight = scaledOceanElevation;

                    float h = Clamp01(scaledHeight / MaxTerrainHeight);
                    norm[x * hDim + y] = h;
                    sum += h;
                }
            }

            // NOTE: norm is kept in DFU's native frame (second axis y runs north-up,
            // matching DefaultTerrainSampler). No reflection here — the world places
            // tiles with +Z = north so tile-internal y and placement agree, and the
            // marching-squares autotiling (rotations only) stays correct.
            bool hasLoc = locWidth > 0 && locHeight > 0;
            float floor = (float)(sum / norm.Length);   // mean height = the flattened city floor

            // Centered location footprint in heightmap-sample space, with a clearance
            // skirt, flattened to `floor` and blended over a few samples.
            float originX = 0, originZ = 0;
            if (hasLoc)
            {
                int tilePosX = (RMBTilesPerTerrain - locWidth * RMBTilesPerBlock) / 2;
                int tilePosY = (RMBTilesPerTerrain - locHeight * RMBTilesPerBlock) / 2;
                int clr = 3;
                // tilemap-cell rect -> [0,1] over the tile
                float u0 = (tilePosX - clr) / (float)RMBTilesPerTerrain;
                float u1 = (tilePosX + locWidth * RMBTilesPerBlock + clr) / (float)RMBTilesPerTerrain;
                float v0 = (tilePosY - clr) / (float)RMBTilesPerTerrain;
                float v1 = (tilePosY + locHeight * RMBTilesPerBlock + clr) / (float)RMBTilesPerTerrain;
                float blend = 0.06f;   // ~8 samples of skirt

                for (int x = 0; x < hDim; x++)
                {
                    float u = (float)x / (hDim - 1);
                    for (int y = 0; y < hDim; y++)
                    {
                        float v = (float)y / (hDim - 1);
                        float s = FlattenStrength(u, v, u0, u1, v0, v1, blend);
                        if (s > 0)
                        {
                            int idx = x * hDim + y;
                            norm[idx] = norm[idx] + (floor - norm[idx]) * s;
                        }
                    }
                }

                // Place the tile so the centered location's block (0,0) lands on the
                // sim town origin (0,0). tilePos is in tiles; 16 tiles = one block.
                originX = -(tilePosX / (float)RMBTilesPerBlock) * BlockWorld;
                originZ = -(tilePosY / (float)RMBTilesPerBlock) * BlockWorld;
            }

            // Per-tile texture painting (classify + marching squares) on the
            // flattened normalised heights (absolute elevation drives ocean/beach).
            byte[] tilemap = BuildTilemap(norm, hDim, mx, my);

            // Overlay the location's own ground tiles (roads/courtyards/cobble from
            // the RMB blocks) over the city footprint — DFU's SetLocationTiles. Same
            // ground atlas; centering matches the heightfield + buildings.
            if (hasLoc && blocks != null && blockNames != null)
                PaintLocationTiles(tilemap, locWidth, locHeight, blocks, blockNames);

            // To world metres, offset by the datum so the town floor sits at y=0.
            // Neighbour tiles pass the centre tile's floor as datum so heights are
            // continuous across the shared seam (no per-tile re-levelling).
            float datum = float.IsNaN(datumNorm) ? floor : datumNorm;
            var heights = new float[norm.Length];
            for (int i = 0; i < norm.Length; i++)
                heights[i] = (norm[i] - datum) * MaxTerrainHeight;

            return new TerrainTileData
            {
                MapPixelX = mx,
                MapPixelY = my,
                Dim = hDim,
                WorldSize = TileWorldSize,
                MaxHeight = MaxTerrainHeight,
                Heights = heights,
                GroundArchive = groundArchive,
                TileDim = TDim,
                Tilemap = tilemap,
                HasLocation = hasLoc,
                Floor = floor,
                OriginX = originX,
                OriginZ = originZ,
            };
        }

        // --- Per-tile painting (ports DefaultTerrainTexturing) ---
        const int TDim = 128;                 // MapsFile.WorldMapTileDim
        const float scaledBeachElevation = 5.0f * baseHeightScale;   // 40
        const int NoiseSeed = 417028;
        const byte Water = 0, Dirt = 1, Grass = 2, Stone = 3;

        private static byte[] BuildTilemap(float[] norm, int hDim, int mx, int my)
        {
            // 1) Classify each cell into a base type (water/dirt/grass/stone).
            var baseType = new byte[TDim * TDim];   // index = cx*TDim + cy
            for (int cx = 0; cx < TDim; cx++)
            {
                int hx = Math.Min(hDim - 1, (int)(hDim * ((float)cx / TDim)));
                int lat = mx * TDim + cx;
                for (int cy = 0; cy < TDim; cy++)
                {
                    int hy = Math.Min(hDim - 1, (int)(hDim * ((float)cy / TDim)));
                    float height = norm[hx * hDim + hy] * MaxTerrainHeight;

                    byte t;
                    if (height <= scaledOceanElevation) t = Water;
                    else if (height <= scaledBeachElevation + Jitter(cx * TDim + cy)) t = Dirt;
                    else
                    {
                        int lon = (MapsFile.MaxMapPixelY - my) * TDim + cy;
                        float w = Clamp01f(Noise.Get(lat, lon, 0.05f, 0.9f, 0.4f, 3, NoiseSeed));
                        t = w < 0.5f ? Dirt : (w > 0.95f ? Stone : Grass);
                    }
                    baseType[cx * TDim + cy] = t;
                }
            }

            // 2) Marching squares over the 2x2 neighbourhood -> lookup byte.
            var tilemap = new byte[TDim * TDim];
            for (int cx = 0; cx < TDim; cx++)
            {
                for (int cy = 0; cy < TDim; cy++)
                {
                    int b0 = baseType[cx * TDim + cy];
                    int b1 = baseType[Math.Min(TDim - 1, cx + 1) * TDim + cy];
                    int b2 = baseType[cx * TDim + Math.Min(TDim - 1, cy + 1)];
                    int b3 = baseType[Math.Min(TDim - 1, cx + 1) * TDim + Math.Min(TDim - 1, cy + 1)];
                    int shape = (b0 & 1) | (b1 & 1) << 1 | (b2 & 1) << 2 | (b3 & 1) << 3;
                    int ring = (b0 + b1 + b2 + b3) >> 2;
                    tilemap[cx * TDim + cy] = LookupTable[shape | (ring << 4)];
                }
            }
            return tilemap;
        }

        // Overlay the location's RMB ground tiles (roads/courtyards/cobble) onto the
        // centered city footprint — ports DFU's TerrainHelper.SetLocationTiles. The
        // tile's record/rotation/flip re-encode into our lookup-byte format and index
        // the same ground atlas. Cell (xpos,ypos) -> tilemap[xpos*TDim + ypos], the
        // same convention the heightfield + buildings use (verified aligned).
        private static void PaintLocationTiles(byte[] tilemap, int width, int height, BlocksFile blocks, string[] blockNames)
        {
            int tilePosX = (RMBTilesPerTerrain - width * RMBTilesPerBlock) / 2;
            int tilePosY = (RMBTilesPerTerrain - height * RMBTilesPerBlock) / 2;

            for (int blockY = 0; blockY < height; blockY++)
            {
                for (int blockX = 0; blockX < width; blockX++)
                {
                    string name = blockNames[blockY * width + blockX];
                    var block = blocks.GetBlock(name);
                    if (block.Type != DFBlock.BlockTypes.Rmb) continue;
                    var ground = block.RmbBlock.FldHeader.GroundData.GroundTiles;
                    if (ground == null) continue;

                    for (int tileY = 0; tileY < RMBTilesPerBlock; tileY++)
                    {
                        for (int tileX = 0; tileX < RMBTilesPerBlock; tileX++)
                        {
                            var t = ground[tileX, (RMBTilesPerBlock - 1) - tileY];
                            if (t.TextureRecord < 0 || t.TextureRecord >= 56) continue;
                            int xpos = tilePosX + blockX * RMBTilesPerBlock + tileX;
                            int ypos = tilePosY + blockY * RMBTilesPerBlock + tileY;
                            if (xpos < 0 || xpos >= TDim || ypos < 0 || ypos >= TDim) continue;
                            byte b = (byte)(t.TextureRecord | (t.IsRotated ? 64 : 0) | (t.IsFlipped ? 128 : 0));
                            tilemap[xpos * TDim + ypos] = b;
                        }
                    }
                }
            }
        }

        // Deterministic +/-1.5 jitter on the beach line (replaces Unity.Mathematics.Random).
        private static float Jitter(int index)
        {
            uint h = (uint)index * 2654435761u;
            h ^= h >> 15;
            return ((h & 0xffff) / 65535f) * 3f - 1.5f;
        }

        private static float Clamp01f(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // 64-byte marching-squares lookup, built verbatim from DFU's
        // CreateLookupTable/AddLookupRange/MakeLookup (TerrainTexturing.cs:244-307).
        private static readonly byte[] LookupTable = BuildLookup();
        private static byte[] BuildLookup()
        {
            var t = new byte[64];
            AddRange(t, 0, 1, 5, 48, false, 0);
            AddRange(t, 2, 1, 10, 51, true, 16);
            AddRange(t, 2, 3, 15, 53, false, 32);
            AddRange(t, 3, 3, 15, 53, true, 48);
            return t;
        }
        private static byte Mk(int index, bool rotate, bool flip)
        {
            if (rotate) index += 64;
            if (flip) index += 128;
            return (byte)index;
        }
        private static void AddRange(byte[] t, int baseStart, int baseEnd, int shapeStart, int saddle, bool reverse, int o)
        {
            if (reverse)
            {
                t[o] = Mk(baseStart, false, false); t[o + 1] = Mk(shapeStart + 2, true, true);
                t[o + 2] = Mk(shapeStart + 2, false, false); t[o + 3] = Mk(shapeStart + 1, true, true);
                t[o + 4] = Mk(shapeStart + 2, false, true); t[o + 5] = Mk(shapeStart + 1, false, true);
                t[o + 6] = Mk(saddle, true, false); t[o + 7] = Mk(shapeStart, true, true);
                t[o + 8] = Mk(shapeStart + 2, true, false); t[o + 9] = Mk(saddle, false, false);
                t[o + 10] = Mk(shapeStart + 1, false, false); t[o + 11] = Mk(shapeStart, false, false);
                t[o + 12] = Mk(shapeStart + 1, true, false); t[o + 13] = Mk(shapeStart, false, true);
                t[o + 14] = Mk(shapeStart, true, false); t[o + 15] = Mk(baseEnd, false, false);
            }
            else
            {
                t[o] = Mk(baseStart, false, false); t[o + 1] = Mk(shapeStart, true, false);
                t[o + 2] = Mk(shapeStart, false, true); t[o + 3] = Mk(shapeStart + 1, true, false);
                t[o + 4] = Mk(shapeStart, false, false); t[o + 5] = Mk(shapeStart + 1, false, false);
                t[o + 6] = Mk(saddle, false, false); t[o + 7] = Mk(shapeStart + 2, true, false);
                t[o + 8] = Mk(shapeStart, true, true); t[o + 9] = Mk(saddle, true, false);
                t[o + 10] = Mk(shapeStart + 1, false, true); t[o + 11] = Mk(shapeStart + 2, false, true);
                t[o + 12] = Mk(shapeStart + 1, true, true); t[o + 13] = Mk(shapeStart + 2, false, false);
                t[o + 14] = Mk(shapeStart + 2, true, true); t[o + 15] = Mk(baseEnd, false, false);
            }
        }

        // Flatten strength 1 inside the rect, ramping to 0 across a blend skirt.
        private static float FlattenStrength(float u, float v, float u0, float u1, float v0, float v1, float b)
        {
            float fu = Ramp(u, u0, u1, b);
            float fv = Ramp(v, v0, v1, b);
            return Math.Min(fu, fv);
        }

        private static float Ramp(float t, float lo, float hi, float b)
        {
            if (t >= lo && t <= hi) return 1f;
            if (t < lo) return t < lo - b ? 0f : 1f - (lo - t) / b;
            return t > hi + b ? 0f : 1f - (t - hi) / b;
        }

        private static float S(byte[] shm, int a, int bb) => shm[a + bb * 4];           // sd = 4
        private static float L(byte[] lhm, int a, int bb, int ld) => lhm[a + bb * ld];
        private static float Cubic(float v0, float v1, float v2, float v3, float f)
        {
            float A = (v3 - v2) - (v0 - v1);
            float B = (v0 - v1) - A;
            float C = v2 - v0;
            float D = v1;
            return A * (f * f * f) + B * (f * f) + C * f + D;
        }
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
