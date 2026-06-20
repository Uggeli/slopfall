using System;
using System.Collections.Generic;

namespace Sim.AssetExport
{
    /// One map pixel's render geometry: the model placements + decorative flats whose
    /// world position falls in this pixel, plus the unique model ids the client must
    /// fetch for this tile (deduped within the tile; the browser dedupes globally).
    public sealed class RegionTile
    {
        public List<TownLayout.Placement> Placements = new();
        public List<TownLayout.Flat> Flats = new();
        public List<uint> ModelIds = new();
    }

    /// Partitions a whole-region placement list (TownLayout.ResolveRegion output) into
    /// per-map-pixel buckets so the viewer can stream geometry on the same pixel ring
    /// terrain already uses. A placement is bucketed by its world translation, so a
    /// city spanning several pixels splits across the correct buckets. Static after
    /// build (placements never move).
    public sealed class RegionPlacementIndex
    {
        readonly Dictionary<long, RegionTile> _tiles = new();
        static readonly RegionTile EmptyTile = new();

        /// Region-global texture climate (taken from the first settlement, as the old
        /// whole-region dump did). The viewer needs it to load each tile's models;
        /// per-settlement climate is a documented follow-up.
        public int ClimateBase { get; private set; }

        public static long PixelKey(int mx, int my) => ((long)mx << 32) | (uint)my;

        static (int mx, int my) PixelOf(float geoX, float geoZ, int mx0, int my1, float ts)
            => (mx0 + (int)MathF.Round(geoX / ts), my1 - (int)MathF.Round(geoZ / ts));

        public static RegionPlacementIndex Build(TownLayout.TownData region, int mx0, int my1, float tileSize)
        {
            var idx = new RegionPlacementIndex { ClimateBase = region.ClimateBase };
            var seenModel = new Dictionary<long, HashSet<uint>>();

            foreach (var p in region.Placements)
            {
                // Column-major 4x4: translation is at [12]=x, [14]=z.
                var (mx, my) = PixelOf(p.Matrix[12], p.Matrix[14], mx0, my1, tileSize);
                var tile = idx.GetOrAdd(mx, my, seenModel, out var seen);
                tile.Placements.Add(p);
                if (seen.Add(p.ModelId)) tile.ModelIds.Add(p.ModelId);
            }
            foreach (var f in region.Flats)
            {
                var (mx, my) = PixelOf(f.X, f.Z, mx0, my1, tileSize);
                idx.GetOrAdd(mx, my, seenModel, out _).Flats.Add(f);
            }
            return idx;
        }

        RegionTile GetOrAdd(int mx, int my, Dictionary<long, HashSet<uint>> seenModel, out HashSet<uint> seen)
        {
            long key = PixelKey(mx, my);
            if (!_tiles.TryGetValue(key, out var tile))
            {
                _tiles[key] = tile = new RegionTile();
                seenModel[key] = new HashSet<uint>();
            }
            seen = seenModel[key];
            return tile;
        }

        public RegionTile At(int mx, int my)
            => _tiles.TryGetValue(PixelKey(mx, my), out var t) ? t : EmptyTile;

        public IEnumerable<(long key, RegionTile tile)> AllTiles()
        {
            foreach (var kv in _tiles) yield return (kv.Key, kv.Value);
        }
    }
}
