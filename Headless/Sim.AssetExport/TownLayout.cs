// Resolves a town's exterior into a flat list of model placements (render plane 1,
// layout side). Walks the location's RMB block grid exactly as the sim's TownLoader
// does (same BlocksFile + constants), so rendered models land on the sim's building
// and agent coordinates. Emits one column-major 4x4 world matrix per model instance
// for the client to apply directly (no Euler reinterpretation).
//
// Transform chain mirrors RMBLayout.AddModels:
//   world = blockOffset(bx,by) * subRecordTRS(XPos,4096-ZPos, -YRot) * objectTRS(XPos,-YPos,ZPos, -YRot, scale)
// All rotations here are about Y (the dominant case); rare per-object X/Z tilts are
// not yet applied. Block-level misc objects (walls, gates, props) are emitted
// after the subrecord models, using the AddProps transform (no subrecord
// wrapper; ZPos + RMBDimension; -4 props Y offset).

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

        public struct Flat
        {
            public int Archive;
            public int Record;
            public float X, Y, Z;      // world position (billboard base)
            public float WorldW, WorldH;
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
            public List<Flat> Flats = new();
            public List<int> FlatArchives = new();   // unique flat archives the client must fetch
            public float[] Min = new float[3];
            public float[] Max = new float[3];
        }

        public static TownData Resolve(string arena2, string region, string location)
        {
            _flatArena2 = arena2;
            var maps = new MapsFile(Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            DFLocation loc = maps.GetLocation(region, location);

            var data = new TownData
            {
                Region = region,
                Location = location,
                BlockSide = BlocksFile.RMBDimension * GlobalScale,
                ClimateBase = ClimateSwap.ClimateBasesOf(maps, loc),
                Season = 0,
            };

            var seen = new HashSet<uint>();
            var flatSeen = new HashSet<int>();
            var b = new Bounds();
            AddLocation(blocks, loc, 0f, 0f, 0f, data, seen, flatSeen, b);
            b.WriteInto(data);
            return data;
        }

        /// Resolves a whole region into one placement list: every settlement's exterior,
        /// each offset to its packed origin in the combined grid (the same origins the
        /// sim's RegionLoader assigns), so the rendered towns land exactly on the agent
        /// coordinates the snapshot stream carries. Maps/blocks open once, shared across
        /// settlements. ClimateBase is taken from the first settlement — uniform-climate
        /// regions (e.g. Betony) are exact; mixed-climate regions mis-texture until
        /// placements carry per-settlement climate (a follow-up).
        public static TownData ResolveRegion(string arena2, string region,
            IReadOnlyList<(string name, float ox, float oy, float oz)> settlements)
        {
            _flatArena2 = arena2;
            var maps = new MapsFile(Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);

            var data = new TownData
            {
                Region = region,
                Location = settlements.Count + " settlements",
                BlockSide = BlocksFile.RMBDimension * GlobalScale,
                Season = 0,
            };

            var seen = new HashSet<uint>();
            var flatSeen = new HashSet<int>();
            var b = new Bounds();
            bool climateSet = false;
            foreach (var (name, ox, oy, oz) in settlements)
            {
                DFLocation loc = maps.GetLocation(region, name);
                if (!loc.Loaded) continue;
                if (!climateSet) { data.ClimateBase = ClimateSwap.ClimateBasesOf(maps, loc); climateSet = true; }
                AddLocation(blocks, loc, ox, oy, oz, data, seen, flatSeen, b);
            }
            b.WriteInto(data);
            return data;
        }

        /// Appends one location's model placements, each translated by (offX, offY, offZ)
        /// so a settlement lands at its combined-grid origin and its terrain pad height.
        /// Mirrors RMBLayout.AddModels.
        static void AddLocation(BlocksFile blocks, DFLocation loc, float offX, float offY, float offZ,
            TownData data, HashSet<uint> seen, HashSet<int> flatSeen, Bounds b)
        {
            float blockSide = BlocksFile.RMBDimension * GlobalScale;
            float rd = BlocksFile.RotationDivisor;

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

                    float[] blockM = Mat.Translate(offX + bx * blockSide, offY, offZ + by * blockSide);

                    foreach (var sub in block.RmbBlock.SubRecords)
                    {
                        float sx = sub.XPos * GlobalScale;
                        float sz = (BlocksFile.RMBDimension - sub.ZPos) * GlobalScale;
                        float sAng = Deg2Rad(-sub.YRotation / rd);
                        float[] subM = Mat.Mul(Mat.Translate(sx, 0f, sz), Mat.RotateY(sAng));

                        if (sub.Exterior.Block3dObjectRecords != null)
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

                            b.Add(m[12], m[13], m[14]);
                        }

                        if (sub.Exterior.BlockFlatObjectRecords != null)
                            foreach (var f in sub.Exterior.BlockFlatObjectRecords)
                            {
                                if (f.FactionID != 0) continue;   // static NPC — sim owns population
                                float[] fm = Mat.Mul(Mat.Mul(blockM, subM),
                                    Mat.Translate(f.XPos * GlobalScale, -f.YPos * GlobalScale, f.ZPos * GlobalScale));
                                var (fw, fh) = FlatSize(f.TextureArchive, f.TextureRecord);
                                AddFlat(data, flatSeen, f.TextureArchive, f.TextureRecord, fm[12], fm[13], fm[14], fw, fh, b);
                            }
                    }

                    // Block-level misc 3D objects: wall segments, city gates,
                    // fountains, props. No subrecord wrapper — positioned
                    // directly in block space per RMBLayout.AddProps (note the
                    // ZPos + RMBDimension convention and the -4 props Y offset).
                    if (block.RmbBlock.Misc3dObjectRecords != null)
                    {
                        const float propsOffsetY = -4f;
                        foreach (var obj in block.RmbBlock.Misc3dObjectRecords)
                        {
                            float mx = obj.XPos * GlobalScale;
                            float my = (-obj.YPos + propsOffsetY) * GlobalScale;
                            float mz = (obj.ZPos + BlocksFile.RMBDimension) * GlobalScale;
                            float mAng = Deg2Rad(-obj.YRotation / rd);
                            float[] objM = Mat.Mul(Mat.Mul(Mat.Translate(mx, my, mz), Mat.RotateY(mAng)), ScaleOf(obj));

                            float[] m = Mat.Mul(blockM, objM);
                            data.Placements.Add(new Placement { ModelId = obj.ModelIdNum, Matrix = m });
                            if (seen.Add(obj.ModelIdNum))
                                data.ModelIds.Add(obj.ModelIdNum);

                            b.Add(m[12], m[13], m[14]);
                        }
                    }

                    // Block-level misc flat objects (light flats, animals, decor). Skip NPC flats.
                    if (block.RmbBlock.MiscFlatObjectRecords != null)
                        foreach (var f in block.RmbBlock.MiscFlatObjectRecords)
                        {
                            if (f.FactionID != 0) continue;
                            float[] m = Mat.Mul(blockM, Mat.Translate(f.XPos * GlobalScale, -f.YPos * GlobalScale, f.ZPos * GlobalScale));
                            var (fw, fh) = FlatSize(f.TextureArchive, f.TextureRecord);
                            AddFlat(data, flatSeen, f.TextureArchive, f.TextureRecord, m[12], m[13], m[14], fw, fh, b);
                        }

                    // Nature ground scenery (trees/rocks/plants). One per 16x16 tile,
                    // climate archive from the location; same formula the flora registry uses.
                    int natureArchive = loc.Climate.NatureArchive;
                    var ground = block.RmbBlock.FldHeader.GroundData.GroundScenery;
                    if (ground != null)
                    {
                        const float TileDim = 256f, NatureOffsetY = -2f;
                        for (int gsx = 0; gsx < 16; gsx++)
                        for (int gsy = 0; gsy < 16; gsy++)
                        {
                            int rec = ground[gsx, 15 - gsy].TextureRecord;
                            if (rec < 1) continue;
                            float wx = offX + bx * blockSide + gsx * TileDim * GlobalScale;
                            float wy = offY + NatureOffsetY * GlobalScale;
                            float wz = offZ + by * blockSide + (gsy * TileDim + TileDim) * GlobalScale;
                            var (fw, fh) = FlatSize(natureArchive, rec);
                            AddFlat(data, flatSeen, natureArchive, rec, wx, wy, wz, fw, fh, b);
                        }
                    }
                }
            }
        }

        static void AddFlat(TownData data, HashSet<int> flatSeen, int archive, int record,
            float wx, float wy, float wz, float worldW, float worldH, Bounds b)
        {
            data.Flats.Add(new Flat { Archive = archive, Record = record, X = wx, Y = wy, Z = wz, WorldW = worldW, WorldH = worldH });
            if (flatSeen.Add(archive)) data.FlatArchives.Add(archive);
            b.Add(wx, wy, wz);
        }

        // Cache of (archive -> per-record world sizes), so flat billboards match their texture.
        static readonly Dictionary<int, (float w, float h)[]> _flatSizes = new();
        static string _flatArena2;
        static (float w, float h) FlatSize(int archive, int record)
        {
            if (!_flatSizes.TryGetValue(archive, out var sizes))
            {
                var meta = SpriteFlat.Build(_flatArena2, archive).meta;
                sizes = new (float, float)[meta.Count];
                for (int i = 0; i < meta.Count; i++) sizes[i] = (meta.Cells[i].WorldW, meta.Cells[i].WorldH);
                _flatSizes[archive] = sizes;
            }
            return (record >= 0 && record < sizes.Length) ? sizes[record] : (1f, 1f);
        }

        /// Accumulates a world-space AABB across one or many locations.
        sealed class Bounds
        {
            float minx = 1e30f, miny = 1e30f, minz = 1e30f;
            float maxx = -1e30f, maxy = -1e30f, maxz = -1e30f;
            public void Add(float x, float y, float z)
            {
                if (x < minx) minx = x; if (x > maxx) maxx = x;
                if (y < miny) miny = y; if (y > maxy) maxy = y;
                if (z < minz) minz = z; if (z > maxz) maxz = z;
            }
            public void WriteInto(TownData d)
            {
                d.Min = new[] { minx, miny, minz };
                d.Max = new[] { maxx, maxy, maxz };
            }
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
