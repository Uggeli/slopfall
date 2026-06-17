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
        private BlocksFile _blocks;

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
        public List<TerrainTileData> GetTownTerrain(string region, string location)
        {
            lock (_gate)
            {
                EnsureMapsWoods();
                DFLocation loc = _maps.GetLocation(region, location);
                var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
                int worldClimate = _maps.GetClimateIndex(pix.X, pix.Y);
                int groundArchive = MapsFile.GetWorldClimateSettings(worldClimate).GroundArchive;

                // Centre tile carries the location (buildings + flatten + own ground tiles).
                var center = TerrainTile.Generate(_woods, pix.X, pix.Y, groundArchive,
                    loc.Exterior.ExteriorData.Width, loc.Exterior.ExteriorData.Height,
                    _blocks, loc.Exterior.ExteriorData.BlockNames);

                var tiles = new List<TerrainTileData> { center };

                // 8 surrounding tiles, plain overworld, sharing the centre's vertical
                // datum so the seam heights are continuous. (3x3; region streaming
                // later just widens this.)
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nb = TerrainTile.Generate(_woods, pix.X + dx, pix.Y + dy, groundArchive,
                            0, 0, null, null, center.Floor);
                        nb.OriginX = center.OriginX + dx * TerrainTile.TileWorldSize;
                        nb.OriginZ = center.OriginZ + dy * TerrainTile.TileWorldSize;
                        tiles.Add(nb);
                    }
                }
                return tiles;
            }
        }

        private void EnsureMapsWoods()
        {
            _maps ??= new MapsFile(Path.Combine(_arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            _woods ??= new WoodsFile(Path.Combine(_arena2, "WOODS.WLD"), FileUsage.UseMemory, true);
            _blocks ??= new BlocksFile(Path.Combine(_arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
        }

        public const int GroundTileSize = 64;   // ground tile records are 64x64
        public const int GroundAtlasCols = 8;    // 56 tiles -> 8 x 7

        /// <summary>
        /// Ground-tile atlas PNG for a climate ground archive: records 0..55 laid out
        /// in an 8x7 grid (top-left origin, row-major). The terrain mesh indexes tiles
        /// by record; rotation/flip are applied client-side via UVs.
        /// </summary>
        public byte[] GetGroundAtlas(int archive)
        {
            lock (_gate)
            {
                var tex = Tex(archive);
                int count = Math.Min(56, tex.RecordCount);
                int cols = GroundAtlasCols, rows = (56 + cols - 1) / cols;
                int ts = GroundTileSize;
                int aw = cols * ts, ah = rows * ts;
                var atlas = new byte[aw * ah * 4];   // transparent by default

                for (int rec = 0; rec < count; rec++)
                {
                    DFBitmap bmp;
                    try { bmp = tex.GetDFBitmap(rec, 0); } catch { continue; }
                    if (bmp?.Data == null || bmp.Width == 0) continue;
                    var rgba = TextureDecode.Rgba(bmp, tex, out int w, out int h);
                    int ox = (rec % cols) * ts, oy = (rec / cols) * ts;
                    for (int y = 0; y < ts; y++)
                    {
                        int sy = h == ts ? y : y * h / ts;
                        for (int x = 0; x < ts; x++)
                        {
                            int sx = w == ts ? x : x * w / ts;
                            int s = (sy * w + sx) * 4;
                            int d = ((oy + y) * aw + (ox + x)) * 4;
                            atlas[d] = rgba[s]; atlas[d + 1] = rgba[s + 1];
                            atlas[d + 2] = rgba[s + 2]; atlas[d + 3] = rgba[s + 3];
                        }
                    }
                }
                return Png.Encode(aw, ah, atlas);
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

        private readonly Dictionary<int, (byte[] png, SpriteMeta meta)> _spriteCache = new();

        /// <summary>Person sprite sheet (PNG) for an archive, built + cached on demand.</summary>
        public byte[] GetSpriteSheet(int archive) => GetSprite(archive).png;

        /// <summary>Person sprite metadata (cell sizes, frame counts, world size).</summary>
        public SpriteMeta GetSpriteMeta(int archive) => GetSprite(archive).meta;

        /// <summary>The civilian archive list the client hashes entity ids into.</summary>
        public int[] CivilianArchives => SpritePerson.CivilianArchives;

        private (byte[] png, SpriteMeta meta) GetSprite(int archive)
        {
            lock (_gate)
            {
                if (_spriteCache.TryGetValue(archive, out var c)) return c;
                var built = SpritePerson.Build(_arena2, archive);
                _spriteCache[archive] = built;
                return built;
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
