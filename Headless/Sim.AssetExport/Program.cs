// Sim.AssetExport — Milestone 1 of the render-client track.
// Decodes ARENA2 building geometry + textures (pure C# DaggerfallConnect
// readers) into web-standard glTF + PNG. Fully headless / browser-free; this
// is the core of the future client-side asset service.
//
// Usage:
//   Sim.AssetExport count [--arena2 <path>]
//   Sim.AssetExport dump <objectId> [--record] [--out <dir>] [--arena2 <path>]
//     <objectId>   ARCH3D model object id (as referenced by city blocks)
//     --record     interpret the number as a raw record index instead
//   Sim.AssetExport sprite <archive> [--arena2 <path>]
//     <archive>    texture archive number (e.g. 255=Rat, 399=CityWatch, 381=civilian)
//
// ARENA2 path resolves from --arena2, else $DAGGERFALL_ARENA2, else the default.

using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace Sim.AssetExport
{
    internal static class Program
    {
        private const string DefaultArena2 = "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: Sim.AssetExport (count | dump <objectId> [--record]) [--out <dir>] [--arena2 <path>]");
                return 2;
            }

            string arena2 = OptValue(args, "--arena2")
                ?? Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
                ?? DefaultArena2;
            string outDir = OptValue(args, "--out") ?? "asset-export";

            // Diagnostic: render a terrain tile's painted tilemap top-down to inspect
            // autotiling orientation in isolation. Doesn't need ARCH3D.
            if (args[0] == "tilemap")
            {
                string region = args.Length > 1 ? args[1] : "Daggerfall";
                string location = args.Length > 2 ? args[2] : "Gothway Garden";
                return TilemapDiag.Run(arena2, region, location, outDir);
            }

            if (args[0] == "sprite")
            {
                if (args.Length < 2 || !int.TryParse(args[1], out int spriteArchive))
                {
                    Console.Error.WriteLine("usage: Sim.AssetExport sprite <archive> [--arena2 <path>]");
                    return 2;
                }
                var (png, meta) = SpritePerson.Build(arena2, spriteArchive);
                Console.WriteLine($"sprite archive {meta.Archive}: rows={meta.Rows} cols(maxFrames)={meta.Cols} cell={meta.CellW}x{meta.CellH} world={meta.WorldW:0.00}x{meta.WorldH:0.00}m png={png.Length}B");
                Console.Write("  frames/record:");
                for (int r = 0; r < meta.Frames.Length; r++) Console.Write($" [{r}]={meta.Frames[r]}");
                Console.WriteLine();
                return 0;
            }

            string arch3dPath = Path.Combine(arena2, Arch3dFile.Filename);
            if (!File.Exists(arch3dPath))
            {
                Console.Error.WriteLine($"ARCH3D.BSA not found at {arch3dPath}");
                return 1;
            }

            var arch = new Arch3dFile(arch3dPath, FileUsage.UseMemory, true);
            string cmd = args[0];

            if (cmd == "count")
            {
                Console.WriteLine($"ARCH3D.BSA records: {arch.Count}");
                Console.WriteLine("sample objectIds (record -> id):");
                int[] samples = { 0, 1, arch.Count / 4, arch.Count / 2, arch.Count - 1 };
                foreach (int r in samples)
                    if (r >= 0 && r < arch.Count)
                        Console.WriteLine($"  record {r,5} -> objectId {arch.GetRecordId(r)}");
                return 0;
            }

            if (cmd != "dump" || args.Length < 2 || !long.TryParse(args[1], out long number))
            {
                Console.Error.WriteLine("usage: Sim.AssetExport dump <objectId> [--record] [--out <dir>] [--arena2 <path>]");
                return 2;
            }

            bool byRecord = HasFlag(args, "--record");
            int record = byRecord ? (int)number : arch.GetRecordIndex((uint)number);
            if (record < 0 || record >= arch.Count)
            {
                Console.Error.WriteLine($"no ARCH3D record for {(byRecord ? "record" : "objectId")} {number} (count={arch.Count})");
                return 1;
            }

            uint objectId = arch.GetRecordId(record);
            DFMesh mesh = arch.GetMesh(record);
            if (mesh.SubMeshes == null || mesh.SubMeshes.Length == 0)
            {
                Console.Error.WriteLine($"record {record} (objectId {objectId}) has no submeshes");
                return 1;
            }

            // Load each referenced texture -> PNG + size, dedup by (archive,record).
            var texCache = new Dictionary<int, TextureFile>();
            var sizes = new Dictionary<(int, int), (int w, int h)>();
            var materials = new List<MaterialDef>();
            var seenMat = new HashSet<string>();
            Directory.CreateDirectory(outDir);

            foreach (var sm in mesh.SubMeshes)
            {
                var key = (sm.TextureArchive, sm.TextureRecord);
                if (sizes.ContainsKey(key)) continue;

                string matName = $"tex_{sm.TextureArchive}_{sm.TextureRecord}";
                string pngUri = null;
                int w = 0, h = 0;

                try
                {
                    if (!texCache.TryGetValue(sm.TextureArchive, out var tex))
                    {
                        string texPath = Path.Combine(arena2, TextureFile.IndexToFileName(sm.TextureArchive));
                        tex = new TextureFile(texPath, FileUsage.UseMemory, true);
                        tex.LoadPalette(Path.Combine(arena2, tex.PaletteName));
                        texCache[sm.TextureArchive] = tex;
                    }

                    DFBitmap bmp = tex.GetDFBitmap(sm.TextureRecord, 0);
                    w = bmp.Width; h = bmp.Height;
                    pngUri = $"{matName}.png";
                    WritePng(Path.Combine(outDir, pngUri), bmp, tex);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [warn] texture {sm.TextureArchive}.{sm.TextureRecord} failed: {ex.Message} (exporting untextured)");
                    pngUri = null;
                }

                sizes[key] = (w, h);
                if (seenMat.Add(matName))
                    materials.Add(new MaterialDef { Name = matName, ImageUri = pngUri, Archive = sm.TextureArchive, Record = sm.TextureRecord });
            }

            var prims = MeshExtract.FromDFMesh(mesh,
                (a, r) => sizes.TryGetValue((a, r), out var s) ? (a, r, s.w, s.h) : (a, r, 0, 0));
            string baseName = $"model_{objectId}";
            GltfWriter.Write(outDir, baseName, prims, materials);

            // Headless sanity summary
            int totalVerts = 0, totalTris = 0;
            foreach (var p in prims) { totalVerts += p.Verts.Count; totalTris += p.Indices.Count / 3; }
            Console.WriteLine($"exported {baseName}  (record {record}, objectId {objectId})");
            Console.WriteLine($"  submeshes/prims : {prims.Count}");
            Console.WriteLine($"  vertices        : {totalVerts}");
            Console.WriteLine($"  triangles       : {totalTris}");
            Console.WriteLine($"  textures        : {materials.Count}");
            Console.WriteLine($"  bounds (m)      : {Bounds(prims)}");
            Console.WriteLine($"  out             : {Path.GetFullPath(outDir)}/{baseName}.gltf");
            return 0;
        }

        private static void WritePng(string path, DFBitmap bmp, TextureFile tex)
        {
            var rgba = TextureDecode.Rgba(bmp, tex, out int w, out int h);
            Png.Write(path, w, h, rgba);
        }

        private static string Bounds(List<Primitive> prims)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var p in prims)
                foreach (var v in p.Verts)
                {
                    if (v.px < minX) minX = v.px; if (v.px > maxX) maxX = v.px;
                    if (v.py < minY) minY = v.py; if (v.py > maxY) maxY = v.py;
                    if (v.pz < minZ) minZ = v.pz; if (v.pz > maxZ) maxZ = v.pz;
                }
            if (minX > maxX) return "(empty)";
            return $"[{minX:0.00},{minY:0.00},{minZ:0.00}] .. [{maxX:0.00},{maxY:0.00},{maxZ:0.00}]";
        }

        private static string OptValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (var a in args) if (a == name) return true;
            return false;
        }
    }
}
