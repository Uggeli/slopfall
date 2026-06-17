// Hand-rolled glTF 2.0 writer: POCOs serialised with System.Text.Json
// (camelCase, omit-null) + a tightly-packed little-endian .bin buffer.
// One primitive per submesh; each attribute gets its own bufferView so every
// accessor sits at byteOffset 0. No external glTF dependency.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sim.AssetExport
{
    public sealed class MaterialDef
    {
        public string Name;
        public string ImageUri;   // null => untextured (flat colour)
    }

    public static class GltfWriter
    {
        // glTF enum constants
        private const int FLOAT = 5126;
        private const int UNSIGNED_INT = 5125;
        private const int ARRAY_BUFFER = 34962;
        private const int ELEMENT_ARRAY_BUFFER = 34963;
        private const int NEAREST = 9728;
        private const int REPEAT = 10497;

        /// <summary>Writes baseName.gltf + baseName.bin into outDir; images by relative URI.</summary>
        public static void Write(string outDir, string baseName, List<Primitive> prims, List<MaterialDef> materials)
        {
            Directory.CreateDirectory(outDir);
            string binName = baseName + ".bin";

            var g = new Gltf();
            g.Asset = new Asset();
            g.Scene = 0;
            g.Scenes.Add(new Scene { Nodes = new[] { 0 } });
            g.Nodes.Add(new Node { Mesh = 0, Name = baseName });

            // Samplers / images / textures / materials
            g.Samplers.Add(new Sampler { MagFilter = NEAREST, MinFilter = NEAREST, WrapS = REPEAT, WrapT = REPEAT });
            for (int m = 0; m < materials.Count; m++)
            {
                var md = materials[m];
                var pbr = new PbrMetallicRoughness { MetallicFactor = 0f, RoughnessFactor = 1f };
                if (md.ImageUri != null)
                {
                    int imageIndex = g.Images.Count;
                    g.Images.Add(new Image { Uri = md.ImageUri });
                    int texIndex = g.Textures.Count;
                    g.Textures.Add(new Texture { Source = imageIndex, Sampler = 0 });
                    pbr.BaseColorTexture = new TextureInfo { Index = texIndex };
                }
                else
                {
                    pbr.BaseColorFactor = new[] { 0.8f, 0.8f, 0.8f, 1f };
                }
                g.Materials.Add(new Material
                {
                    Name = md.Name,
                    PbrMetallicRoughness = pbr,
                    DoubleSided = true,        // M1: render double-sided until winding is verified
                    AlphaMode = "MASK",
                    AlphaCutoff = 0.5f,
                });
            }

            // Geometry -> .bin + accessors/bufferViews
            var mesh = new Mesh { Name = baseName };
            using var bin = new MemoryStream();
            var bw = new BinaryWriter(bin);

            foreach (var prim in prims)
            {
                int n = prim.Verts.Count;

                // POSITION
                int posView = AddView(g, (int)bin.Position, n * 12, ARRAY_BUFFER);
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                foreach (var v in prim.Verts)
                {
                    bw.Write(v.px); bw.Write(v.py); bw.Write(v.pz);
                    if (v.px < minX) minX = v.px; if (v.px > maxX) maxX = v.px;
                    if (v.py < minY) minY = v.py; if (v.py > maxY) maxY = v.py;
                    if (v.pz < minZ) minZ = v.pz; if (v.pz > maxZ) maxZ = v.pz;
                }
                int posAcc = AddAccessor(g, posView, FLOAT, n, "VEC3",
                    new[] { minX, minY, minZ }, new[] { maxX, maxY, maxZ });

                // NORMAL
                int nrmView = AddView(g, (int)bin.Position, n * 12, ARRAY_BUFFER);
                foreach (var v in prim.Verts) { bw.Write(v.nx); bw.Write(v.ny); bw.Write(v.nz); }
                int nrmAcc = AddAccessor(g, nrmView, FLOAT, n, "VEC3", null, null);

                // TEXCOORD_0
                int uvView = AddView(g, (int)bin.Position, n * 8, ARRAY_BUFFER);
                foreach (var v in prim.Verts) { bw.Write(v.u); bw.Write(v.v); }
                int uvAcc = AddAccessor(g, uvView, FLOAT, n, "VEC2", null, null);

                // INDICES
                int idxView = AddView(g, (int)bin.Position, prim.Indices.Count * 4, ELEMENT_ARRAY_BUFFER);
                foreach (var i in prim.Indices) bw.Write(i);
                int idxAcc = AddAccessor(g, idxView, UNSIGNED_INT, prim.Indices.Count, "SCALAR", null, null);

                mesh.Primitives.Add(new MeshPrimitive
                {
                    Attributes = new Dictionary<string, int>
                    {
                        ["POSITION"] = posAcc,
                        ["NORMAL"] = nrmAcc,
                        ["TEXCOORD_0"] = uvAcc,
                    },
                    Indices = idxAcc,
                    Material = MaterialIndexFor(materials, prim),
                });
            }
            g.Meshes.Add(mesh);

            bw.Flush();
            byte[] binBytes = bin.ToArray();
            g.Buffers.Add(new Buffer { Uri = binName, ByteLength = binBytes.Length });

            File.WriteAllBytes(Path.Combine(outDir, binName), binBytes);

            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true,
            };
            File.WriteAllText(Path.Combine(outDir, baseName + ".gltf"), JsonSerializer.Serialize(g, opts));
        }

        private static int MaterialIndexFor(List<MaterialDef> materials, Primitive prim)
        {
            string name = $"tex_{prim.TextureArchive}_{prim.TextureRecord}";
            for (int i = 0; i < materials.Count; i++)
                if (materials[i].Name == name) return i;
            return 0;
        }

        private static int AddView(Gltf g, int offset, int length, int target)
        {
            g.BufferViews.Add(new BufferView { Buffer = 0, ByteOffset = offset, ByteLength = length, Target = target });
            return g.BufferViews.Count - 1;
        }

        private static int AddAccessor(Gltf g, int view, int componentType, int count, string type, float[] min, float[] max)
        {
            g.Accessors.Add(new Accessor
            {
                BufferView = view,
                ComponentType = componentType,
                Count = count,
                Type = type,
                Min = min,
                Max = max,
            });
            return g.Accessors.Count - 1;
        }

        #region glTF POCOs
        private sealed class Gltf
        {
            public Asset Asset { get; set; }
            public int Scene { get; set; }
            public List<Scene> Scenes { get; set; } = new();
            public List<Node> Nodes { get; set; } = new();
            public List<Mesh> Meshes { get; set; } = new();
            public List<Material> Materials { get; set; } = new();
            public List<Texture> Textures { get; set; } = new();
            public List<Image> Images { get; set; } = new();
            public List<Sampler> Samplers { get; set; } = new();
            public List<Accessor> Accessors { get; set; } = new();
            public List<BufferView> BufferViews { get; set; } = new();
            public List<Buffer> Buffers { get; set; } = new();
        }
        private sealed class Asset { public string Version { get; set; } = "2.0"; public string Generator { get; set; } = "Sim.AssetExport"; }
        private sealed class Scene { public int[] Nodes { get; set; } }
        private sealed class Node { public int? Mesh { get; set; } public string Name { get; set; } }
        private sealed class Mesh { public List<MeshPrimitive> Primitives { get; set; } = new(); public string Name { get; set; } }
        private sealed class MeshPrimitive
        {
            public Dictionary<string, int> Attributes { get; set; }
            public int? Indices { get; set; }
            public int? Material { get; set; }
        }
        private sealed class Material
        {
            public string Name { get; set; }
            public PbrMetallicRoughness PbrMetallicRoughness { get; set; }
            public bool DoubleSided { get; set; }
            public string AlphaMode { get; set; }
            public float? AlphaCutoff { get; set; }
        }
        private sealed class PbrMetallicRoughness
        {
            public TextureInfo BaseColorTexture { get; set; }
            public float[] BaseColorFactor { get; set; }
            public float MetallicFactor { get; set; }
            public float RoughnessFactor { get; set; }
        }
        private sealed class TextureInfo { public int Index { get; set; } public int? TexCoord { get; set; } }
        private sealed class Texture { public int Source { get; set; } public int? Sampler { get; set; } }
        private sealed class Image { public string Uri { get; set; } }
        private sealed class Sampler { public int MagFilter { get; set; } public int MinFilter { get; set; } public int WrapS { get; set; } public int WrapT { get; set; } }
        private sealed class Accessor
        {
            public int BufferView { get; set; }
            public int ComponentType { get; set; }
            public int Count { get; set; }
            public string Type { get; set; }
            public float[] Min { get; set; }
            public float[] Max { get; set; }
        }
        private sealed class BufferView { public int Buffer { get; set; } public int ByteOffset { get; set; } public int ByteLength { get; set; } public int? Target { get; set; } }
        private sealed class Buffer { public string Uri { get; set; } public int ByteLength { get; set; } }
        #endregion
    }
}
