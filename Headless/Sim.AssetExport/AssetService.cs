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
        private MapsFile _maps;
        private WoodsFile _woods;

        /// <summary>URL path a glTF material points at for its texture.</summary>
        public static string TextureUri(int archive, int record) => $"/asset/texture/{archive}/{record}";

        public AssetService(string arena2Path)
        {
            _arena2 = arena2Path;
            _arch = new Arch3dFile(Path.Combine(_arena2, Arch3dFile.Filename), FileUsage.UseMemory, true);
        }

        public int ModelCount { get { lock (_gate) return _arch.Count; } }

        /// <summary>glTF JSON for a model object id, or null if no such model.</summary>
        public string GetModelGltf(uint objectId, int climate = (int)DaggerfallWorkshop.ClimateBases.Temperate, int season = 0)
        {
            lock (_gate)
            {
                int record = _arch.GetRecordIndex(objectId);
                if (record < 0 || record >= _arch.Count)
                    return null;

                DFMesh mesh = _arch.GetMesh(record);
                if (mesh.SubMeshes == null || mesh.SubMeshes.Length == 0)
                    return null;

                // Map each submesh's original (archive,record) to the climate-swapped
                // (archive,record) + size; the primitive then carries the swapped identity.
                var resolved = new Dictionary<(int, int), (int archive, int record, int w, int h)>();
                var materials = new List<MaterialDef>();
                var seen = new HashSet<string>();

                foreach (var sm in mesh.SubMeshes)
                {
                    var orig = (sm.TextureArchive, sm.TextureRecord);
                    if (resolved.ContainsKey(orig))
                        continue;

                    int archive = ClimateSwap.Archive(sm.TextureArchive, sm.TextureRecord, climate, season);
                    int rec = sm.TextureRecord;
                    int w = 0, h = 0;
                    try { var sz = Tex(archive).GetSize(rec); w = sz.Width; h = sz.Height; }
                    catch { /* unknown texture -> untextured material */ }
                    resolved[orig] = (archive, rec, w, h);

                    string matName = $"tex_{archive}_{rec}";
                    if (seen.Add(matName))
                        materials.Add(new MaterialDef
                        {
                            Name = matName,
                            Archive = archive,
                            Record = rec,
                            ImageUri = w > 0 ? TextureUri(archive, rec) : null,
                        });
                }

                var prims = MeshExtract.FromDFMesh(mesh,
                    (a, r) => resolved.TryGetValue((a, r), out var v) ? v : (a, r, 0, 0));
                return GltfWriter.BuildGltf($"model_{objectId}", prims, materials,
                    bufferUri: null, imageUriFactory: m => m.ImageUri, out _);
            }
        }

        /// <summary>Resolved town layout (placements + climate) for the asset endpoint.</summary>
        public TownLayout.TownData GetTown(string region, string location)
        {
            lock (_gate)
                return TownLayout.Resolve(_arena2, region, location);
        }

        /// <summary>
        /// Terrain tile for the booted town's map pixel, flattened under the city to
        /// y=0 with the climate ground texture. (M3b-1: the location's own tile only.)
        /// </summary>
        public TerrainTileData GetTownTerrain(string region, string location)
        {
            lock (_gate)
            {
                EnsureMapsWoods();
                DFLocation loc = _maps.GetLocation(region, location);
                var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
                int worldClimate = _maps.GetClimateIndex(pix.X, pix.Y);
                int groundArchive = MapsFile.GetWorldClimateSettings(worldClimate).GroundArchive;
                return TerrainTile.Generate(_woods, pix.X, pix.Y, groundArchive,
                    loc.Exterior.ExteriorData.Width, loc.Exterior.ExteriorData.Height);
            }
        }

        private void EnsureMapsWoods()
        {
            _maps ??= new MapsFile(Path.Combine(_arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            _woods ??= new WoodsFile(Path.Combine(_arena2, "WOODS.WLD"), FileUsage.UseMemory, true);
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
