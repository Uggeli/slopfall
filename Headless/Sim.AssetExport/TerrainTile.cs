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
        public int GroundArchive;       // climate ground texture archive (single-texture M3b-1)
        public bool HasLocation;        // town tile? (flatten applied)
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
            int locWidth = 0, int locHeight = 0)
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

            // To world metres, offset so the city floor sits at y=0 (sim's flat town).
            var heights = new float[norm.Length];
            for (int i = 0; i < norm.Length; i++)
                heights[i] = (norm[i] - floor) * MaxTerrainHeight;

            return new TerrainTileData
            {
                MapPixelX = mx,
                MapPixelY = my,
                Dim = hDim,
                WorldSize = TileWorldSize,
                MaxHeight = MaxTerrainHeight,
                Heights = heights,
                GroundArchive = groundArchive,
                HasLocation = hasLoc,
                OriginX = originX,
                OriginZ = originZ,
            };
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
