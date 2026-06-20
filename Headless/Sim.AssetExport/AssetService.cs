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

        /// <summary>Resolved layout for a whole region: every settlement's exterior,
        /// each offset to its packed combined-grid origin (matching the sim).</summary>
        public TownLayout.TownData GetRegion(string region,
            IReadOnlyList<(string name, float ox, float oy, float oz)> settlements)
        {
            lock (_gate)
                return TownLayout.ResolveRegion(_arena2, region, settlements);
        }

        /// <summary>Region layout partitioned into per-map-pixel buckets so the viewer
        /// streams geometry on the terrain ring instead of loading every settlement.</summary>
        public RegionPlacementIndex GetRegionIndex(string region,
            IReadOnlyList<(string name, float ox, float oy, float oz)> settlements,
            int mx0, int my1, float tileSize)
        {
            lock (_gate)
            {
                var data = TownLayout.ResolveRegion(_arena2, region, settlements);
                return RegionPlacementIndex.Build(data, mx0, my1, tileSize);
            }
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
                        // +Z = north: the south neighbour (dy=+1) sits at lower Z.
                        nb.OriginX = center.OriginX + dx * TerrainTile.TileWorldSize;
                        nb.OriginZ = center.OriginZ - dy * TerrainTile.TileWorldSize;
                        tiles.Add(nb);
                    }
                }
                return tiles;
            }
        }

        // Region terrain, streamed per tile. One map pixel == one 819.2 m tile; every
        // tile levels to a SHARED region datum so adjacent tiles meet without a cliff,
        // and results are cached (a tile is deterministic from WOODS + climate + datum).
        private readonly Dictionary<long, TerrainTileData> _regionTileCache = new();
        private static long TileKey(int mx, int my) => ((long)mx << 20) | (uint)my;

        /// <summary>The flattened floor of one settlement's tile — the shared datum the
        /// whole region levels against, picked once so every streamed tile agrees.</summary>
        public float RegionTileFloor(string region, string location, int locW, int locH)
        {
            lock (_gate)
            {
                EnsureMapsWoods();
                DFLocation loc = _maps.GetLocation(region, location);
                var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
                int groundArchive = MapsFile.GetWorldClimateSettings(_maps.GetClimateIndex(pix.X, pix.Y)).GroundArchive;
                var tile = TerrainTile.Generate(_woods, pix.X, pix.Y, groundArchive,
                    locW, locH, _blocks, loc.Exterior.ExteriorData.BlockNames);
                return tile.Floor;
            }
        }

        /// <summary>One region terrain tile at map pixel (mx,my), levelled to `datum` and
        /// shifted to its geographic world position by (addX,addZ). A town pixel has its
        /// footprint flattened + ground-painted; a wilderness pixel is plain overworld.
        /// Cached by pixel — the camera re-requests freely as it pans.</summary>
        public TerrainTileData GetRegionTile(string region, int mx, int my, float datum,
            float addX, float addZ, string locName, int locW, int locH)
        {
            lock (_gate)
            {
                long key = TileKey(mx, my);
                if (_regionTileCache.TryGetValue(key, out var hit)) return hit;

                EnsureMapsWoods();
                var climate = MapsFile.GetWorldClimateSettings(_maps.GetClimateIndex(mx, my));
                int groundArchive = climate.GroundArchive;
                int natureArchive = climate.NatureArchive;   // climate nature atlas (500-511)
                float climateScale = climate.ClimateType == DFLocation.ClimateBaseType.Desert ? 0.25f : 1.0f;
                TerrainTileData tile = locName != null
                    ? TerrainTile.Generate(_woods, mx, my, groundArchive, locW, locH,
                        _blocks, _maps.GetLocation(region, locName).Exterior.ExteriorData.BlockNames, datum,
                        natureArchive, climateScale)
                    : TerrainTile.Generate(_woods, mx, my, groundArchive, 0, 0, null, null, datum,
                        natureArchive, climateScale);

                // Grid-align the tile to its map pixel so it meets its wilderness
                // neighbours seamlessly. Generate centres a town's flattened footprint
                // WITHIN the tile (and bakes a negative origin to drop that on the
                // sim's town origin) — but region tiles tile by pixel, so we discard
                // that shift and keep the flatten centred in-tile. The web host centres
                // each town's buildings to match (see TownCentre in Program.cs).
                tile.OriginX = addX;
                tile.OriginZ = addZ;
                _regionTileCache[key] = tile;
                return tile;
            }
        }

        private void EnsureMapsWoods()
        {
            _maps ??= new MapsFile(Path.Combine(_arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            _woods ??= new WoodsFile(Path.Combine(_arena2, "WOODS.WLD"), FileUsage.UseMemory, true);
            _blocks ??= new BlocksFile(Path.Combine(_arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
        }

        public const int GroundTileSize = 64;   // ground tile records are 64x64
        public const int GroundAtlasRecCols = 8; // 56 records -> 8 x 7 per orientation
        public const int GroundAtlasRecRows = 7;
        public const int GroundAtlasOrients = 4; // 0/90/180/270 baked side by side
        public const int GroundAtlasCols = GroundAtlasRecCols * GroundAtlasOrients; // 32

        /// <summary>
        /// Ground-tile atlas PNG for a climate ground archive. Each of records 0..55 is
        /// baked in its FOUR orientations (0/90/180/270 deg) so the client samples a
        /// pre-oriented tile with identity UVs — no rotation/flip on the client. Layout:
        /// 32 x 7 grid of 64px tiles; orientation o occupies column band [o*8, o*8+8),
        /// record at (col = rec%8 + o*8, row = rec/8). The tilemap byte's rot bit (90 deg,
        /// =RotateColors) and flip bit (180 deg, =FlipColors) combine to o = rot + 2*flip,
        /// i.e. RotateColors applied o times — baked here with DFU's exact pixel op.
        /// </summary>
        public byte[] GetGroundAtlas(int archive)
        {
            lock (_gate)
            {
                var tex = Tex(archive);
                int count = Math.Min(56, tex.RecordCount);
                int ts = GroundTileSize;
                int aw = GroundAtlasCols * ts, ah = GroundAtlasRecRows * ts;
                var atlas = new byte[aw * ah * 4];   // transparent by default

                for (int rec = 0; rec < count; rec++)
                {
                    DFBitmap bmp;
                    try { bmp = tex.GetDFBitmap(rec, 0); } catch { continue; }
                    if (bmp?.Data == null || bmp.Width == 0) continue;
                    var rgba = TextureDecode.Rgba(bmp, tex, out int w, out int h);

                    // Base 64x64 tile (nearest-resample the record to tile size).
                    // V-FLIP while resampling (sy counts from the bottom): DFU's
                    // ImageProcessing/GetColor32 flips ground textures to bottom-up before
                    // baking the rotation variants, and the marching-squares lookup's
                    // rotate/flip values are authored against that orientation. Our
                    // TextureDecode keeps the bitmap top-down, so without this flip every
                    // transition tile's feathered edge is mirrored — interior tiles look
                    // fine, but dirt/grass/coast borders come out jagged/inverted.
                    var tile = new byte[ts * ts * 4];
                    for (int y = 0; y < ts; y++)
                    {
                        int fy = ts - 1 - y;
                        int sy = h == ts ? fy : fy * h / ts;
                        for (int x = 0; x < ts; x++)
                        {
                            int sx = w == ts ? x : x * w / ts;
                            int s = (sy * w + sx) * 4, d = (y * ts + x) * 4;
                            tile[d] = rgba[s]; tile[d + 1] = rgba[s + 1];
                            tile[d + 2] = rgba[s + 2]; tile[d + 3] = rgba[s + 3];
                        }
                    }

                    // Bake the 4 orientations (rotate 90 deg o times) into the atlas.
                    var cur = tile;
                    for (int o = 0; o < GroundAtlasOrients; o++)
                    {
                        int ox = ((rec % GroundAtlasRecCols) + o * GroundAtlasRecCols) * ts;
                        int oy = (rec / GroundAtlasRecCols) * ts;
                        for (int y = 0; y < ts; y++)
                            for (int x = 0; x < ts; x++)
                            {
                                int s = (y * ts + x) * 4, d = ((oy + y) * aw + (ox + x)) * 4;
                                atlas[d] = cur[s]; atlas[d + 1] = cur[s + 1];
                                atlas[d + 2] = cur[s + 2]; atlas[d + 3] = cur[s + 3];
                            }
                        cur = Rotate90(cur, ts);   // next orientation
                    }
                }
                return Png.Encode(aw, ah, atlas);
            }
        }

        /// <summary>Rotate an NxN RGBA tile 90 deg, matching DFU's ImageProcessing
        /// RotateColors: dst(x,y) = src(y, N-1-x).</summary>
        private static byte[] Rotate90(byte[] src, int n)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = y, sy = n - 1 - x;
                    int s = (sy * n + sx) * 4, d = (y * n + x) * 4;
                    dst[d] = src[s]; dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
                }
            return dst;
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
        public int[] GuardArchives => SpritePerson.GuardArchives;
        public int[] MonsterArchives => SpritePerson.MonsterArchives;

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

        private readonly Dictionary<int, (byte[] png, FlatMeta meta)> _flatCache = new();
        public byte[] GetFlatSheet(int archive) => Flat(archive).png;
        public FlatMeta GetFlatMeta(int archive) => Flat(archive).meta;
        private (byte[] png, FlatMeta meta) Flat(int archive)
        {
            lock (_gate)
            {
                if (!_flatCache.TryGetValue(archive, out var built))
                    _flatCache[archive] = built = SpriteFlat.Build(_arena2, archive);
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
