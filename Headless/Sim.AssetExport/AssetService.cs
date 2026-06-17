// On-demand ARENA2 asset service — the server side of render plane 1.
// Opens ARCH3D.BSA once, caches TextureFiles, and answers two queries:
//   GetModelGltf(objectId)        -> self-contained glTF JSON (geometry embedded
//                                    as a base64 buffer; textures referenced by
//                                    URL so the browser caches each one town-wide)
//   GetTexturePng(archive,record) -> PNG bytes
//
// The DaggerfallConnect readers mutate internal state per record, so all decode
// runs under one lock. Asset requests are rare and browser-cached, so a coarse
// lock is fine.

using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace Sim.AssetExport
{
    public sealed class AssetService
    {
        private readonly string _arena2;
        private readonly Arch3dFile _arch;
        private readonly Dictionary<int, TextureFile> _texCache = new();
        private readonly object _gate = new();

        /// <summary>URL path a glTF material points at for its texture.</summary>
        public static string TextureUri(int archive, int record) => $"/asset/texture/{archive}/{record}";

        public AssetService(string arena2Path)
        {
            _arena2 = arena2Path;
            _arch = new Arch3dFile(Path.Combine(_arena2, Arch3dFile.Filename), FileUsage.UseMemory, true);
        }

        public int ModelCount { get { lock (_gate) return _arch.Count; } }

        /// <summary>glTF JSON for a model object id, or null if no such model.</summary>
        public string GetModelGltf(uint objectId)
        {
            lock (_gate)
            {
                int record = _arch.GetRecordIndex(objectId);
                if (record < 0 || record >= _arch.Count)
                    return null;

                DFMesh mesh = _arch.GetMesh(record);
                if (mesh.SubMeshes == null || mesh.SubMeshes.Length == 0)
                    return null;

                var sizes = new Dictionary<(int, int), (int w, int h)>();
                var materials = new List<MaterialDef>();
                var seen = new HashSet<string>();

                foreach (var sm in mesh.SubMeshes)
                {
                    var key = (sm.TextureArchive, sm.TextureRecord);
                    if (!sizes.ContainsKey(key))
                    {
                        int w = 0, h = 0;
                        try { var sz = Tex(sm.TextureArchive).GetSize(sm.TextureRecord); w = sz.Width; h = sz.Height; }
                        catch { /* unknown texture -> untextured material */ }
                        sizes[key] = (w, h);

                        string matName = $"tex_{sm.TextureArchive}_{sm.TextureRecord}";
                        if (seen.Add(matName))
                            materials.Add(new MaterialDef
                            {
                                Name = matName,
                                Archive = sm.TextureArchive,
                                Record = sm.TextureRecord,
                                ImageUri = w > 0 ? TextureUri(sm.TextureArchive, sm.TextureRecord) : null,
                            });
                    }
                }

                var prims = MeshExtract.FromDFMesh(mesh, (a, r) => sizes.TryGetValue((a, r), out var s) ? s : (0, 0));
                return GltfWriter.BuildGltf($"model_{objectId}", prims, materials,
                    bufferUri: null, imageUriFactory: m => m.ImageUri, out _);
            }
        }

        /// <summary>PNG bytes for a texture record, or null if it can't be read.</summary>
        public byte[] GetTexturePng(int archive, int record)
        {
            lock (_gate)
            {
                try
                {
                    var tex = Tex(archive);
                    DFBitmap bmp = tex.GetDFBitmap(record, 0);
                    if (bmp == null || bmp.Data == null) return null;
                    var rgba = TextureDecode.Rgba(bmp, tex, out int w, out int h);
                    return Png.Encode(w, h, rgba);
                }
                catch
                {
                    return null;
                }
            }
        }

        private TextureFile Tex(int archive)
        {
            if (!_texCache.TryGetValue(archive, out var tex))
            {
                tex = new TextureFile(Path.Combine(_arena2, TextureFile.IndexToFileName(archive)), FileUsage.UseMemory, true);
                tex.LoadPalette(Path.Combine(_arena2, tex.PaletteName));
                _texCache[archive] = tex;
            }
            return tex;
        }
    }
}
