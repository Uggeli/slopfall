// Resolves a town's exterior into a flat list of model placements (render plane 1,
// layout side). Walks the location's RMB block grid exactly as the sim's TownLoader
// does (same BlocksFile + constants), so rendered models land on the sim's building
// and agent coordinates. Emits one column-major 4x4 world matrix per model instance
// for the client to apply directly (no Euler reinterpretation).
//
// Transform chain mirrors RMBLayout.AddModels:
//   world = blockOffset(bx,by) * subRecordTRS(XPos,4096-ZPos, -YRot) * objectTRS(XPos,-YPos,ZPos, -YRot, scale)
// All rotations here are about Y (the dominant case); rare per-object X/Z tilts are
// not yet applied. Block-level misc objects (walls etc.) are a follow-up.

using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace Sim.AssetExport
{
    public sealed class TownLayout
    {
        public const float GlobalScale = 0.025f;

        public struct Placement
        {
            public uint ModelId;
            public float[] Matrix;   // column-major 4x4, ready for THREE.Matrix4.fromArray
        }

        public sealed class TownData
        {
            public string Region;
            public string Location;
            public int ClimateBase;   // texture-archive climate offset (0/100/300/400)
            public int Season;        // 0 = normal/summer
            public float BlockSide;
            public List<Placement> Placements = new();
            public List<uint> ModelIds = new();   // unique model ids the client must fetch
            public float[] Min = new float[3];
            public float[] Max = new float[3];
        }

        public static TownData Resolve(string arena2, string region, string location)
        {
            var maps = new MapsFile(Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            DFLocation loc = maps.GetLocation(region, location);

            float blockSide = BlocksFile.RMBDimension * GlobalScale;
            float rd = BlocksFile.RotationDivisor;

            var data = new TownData
            {
                Region = region,
                Location = location,
                BlockSide = blockSide,
                ClimateBase = ClimateSwap.ClimateBasesOf(maps, loc),
                Season = 0,
            };

            var seen = new HashSet<uint>();
            float minx = 1e30f, miny = 1e30f, minz = 1e30f, maxx = -1e30f, maxy = -1e30f, maxz = -1e30f;

            int width = loc.Exterior.ExteriorData.Width;
            int height = loc.Exterior.ExteriorData.Height;
            for (int by = 0; by < height; by++)
            {
                for (int bx = 0; bx < width; bx++)
                {
                    string name = loc.Exterior.ExteriorData.BlockNames[by * width + bx];
                    var block = blocks.GetBlock(name);
                    if (block.Type != DFBlock.BlockTypes.Rmb || block.RmbBlock.SubRecords == null)
                        continue;

                    float[] blockM = Mat.Translate(bx * blockSide, 0f, by * blockSide);

                    foreach (var sub in block.RmbBlock.SubRecords)
                    {
                        float sx = sub.XPos * GlobalScale;
                        float sz = (BlocksFile.RMBDimension - sub.ZPos) * GlobalScale;
                        float sAng = Deg2Rad(-sub.YRotation / rd);
                        float[] subM = Mat.Mul(Mat.Translate(sx, 0f, sz), Mat.RotateY(sAng));

                        if (sub.Exterior.Block3dObjectRecords == null)
                            continue;
                        foreach (var obj in sub.Exterior.Block3dObjectRecords)
                        {
                            float ox = obj.XPos * GlobalScale;
                            float oy = -obj.YPos * GlobalScale;
                            float oz = obj.ZPos * GlobalScale;
                            float oAng = Deg2Rad(-obj.YRotation / rd);
                            float[] objM = Mat.Mul(Mat.Mul(Mat.Translate(ox, oy, oz), Mat.RotateY(oAng)), ScaleOf(obj));

                            float[] m = Mat.Mul(Mat.Mul(blockM, subM), objM);
                            data.Placements.Add(new Placement { ModelId = obj.ModelIdNum, Matrix = m });
                            if (seen.Add(obj.ModelIdNum))
                                data.ModelIds.Add(obj.ModelIdNum);

                            float wx = m[12], wy = m[13], wz = m[14];
                            if (wx < minx) minx = wx; if (wx > maxx) maxx = wx;
                            if (wy < miny) miny = wy; if (wy > maxy) maxy = wy;
                            if (wz < minz) minz = wz; if (wz > maxz) maxz = wz;
                        }
                    }
                }
            }

            data.Min = new[] { minx, miny, minz };
            data.Max = new[] { maxx, maxy, maxz };
            return data;
        }

        private static float[] ScaleOf(DFBlock.RmbBlock3dObjectRecord o)
        {
            float sx = o.XScale == 0f ? 1f : o.XScale;
            float sy = o.YScale == 0f ? 1f : o.YScale;
            float sz = o.ZScale == 0f ? 1f : o.ZScale;
            return Mat.Scale(sx, sy, sz);
        }

        private static float Deg2Rad(float d) => d * (float)Math.PI / 180f;
    }

    /// Column-major 4x4 helpers (THREE.Matrix4 layout: index = col*4 + row).
    internal static class Mat
    {
        public static float[] Translate(float x, float y, float z) =>
            new[] { 1f, 0, 0, 0,  0, 1f, 0, 0,  0, 0, 1f, 0,  x, y, z, 1f };

        public static float[] Scale(float x, float y, float z) =>
            new[] { x, 0, 0, 0,  0, y, 0, 0,  0, 0, z, 0,  0, 0, 0, 1f };

        public static float[] RotateY(float a)
        {
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new[] { c, 0, -s, 0,  0, 1f, 0, 0,  s, 0, c, 0,  0, 0, 0, 1f };
        }

        public static float[] Mul(float[] a, float[] b)
        {
            var r = new float[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    float sum = 0f;
                    for (int k = 0; k < 4; k++)
                        sum += a[k * 4 + row] * b[col * 4 + k];
                    r[col * 4 + row] = sum;
                }
            return r;
        }
    }
}
