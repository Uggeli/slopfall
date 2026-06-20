using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// One settlement's packed walkability rectangle and the delta that shifts the
    /// agents inside it into geographic world space (the projection the snapshot
    /// stream applies). Mirrors the tuple the web host builds today.
    public struct GeoRemapEntry { public float MinX, MinZ, MaxX, MaxZ, Dx, Dy, Dz; }

    /// The region's overworld layout as sim truth: map-pixel bbox, the shared
    /// terrain datum, the packed→geo agent remap, and a map-pixel → POI-id spatial
    /// index. Seeded once per region boot (Stage 1: by the web host; Stage 2: by
    /// RegionLoader). Empty/absent in town mode. Static after seed, so Update() is
    /// a no-op — mirrors TownGridRegistry.
    public sealed class RegionGeographyRegistry : Registry
    {
        readonly Dictionary<long, List<int>> _poiByPixel = new();
        readonly List<GeoRemapEntry> _remap = new();
        static readonly IReadOnlyList<int> Empty = new int[0];

        public RegionGeographyRegistry(EventBus events) : base(events) { }

        public bool HasRegion { get; private set; }
        public int Mx0 { get; private set; }
        public int My0 { get; private set; }
        public int Mx1 { get; private set; }
        public int My1 { get; private set; }
        public float TileSize { get; private set; }
        public float Datum { get; private set; }

        public static long PixelKey(int mx, int my) => ((long)mx << 32) | (uint)my;

        public void Seed(int mx0, int my0, int mx1, int my1, float tileSize, float datum,
            IEnumerable<(int mx, int my, int poiId)> poiPixels, IEnumerable<GeoRemapEntry> remap)
        {
            Mx0 = mx0; My0 = my0; Mx1 = mx1; My1 = my1; TileSize = tileSize; Datum = datum;
            _poiByPixel.Clear(); _remap.Clear();
            foreach (var (mx, my, poiId) in poiPixels)
            {
                long key = PixelKey(mx, my);
                if (!_poiByPixel.TryGetValue(key, out var list))
                    _poiByPixel[key] = list = new List<int>();
                list.Add(poiId);
            }
            _remap.AddRange(remap);
            HasRegion = true;
        }

        public IReadOnlyList<int> PoisAt(int mx, int my)
            => _poiByPixel.TryGetValue(PixelKey(mx, my), out var list) ? list : Empty;

        public List<int> RingPois(int mx, int my, int r)
        {
            var hit = new List<int>();
            for (int x = mx - r; x <= mx + r; x++)
                for (int y = my - r; y <= my + r; y++)
                    if (_poiByPixel.TryGetValue(PixelKey(x, y), out var list))
                        hit.AddRange(list);
            return hit;
        }

        public (int mx, int my) PixelOf(float geoX, float geoZ)
            => (Mx0 + (int)MathF.Round(geoX / TileSize),
                My1 - (int)MathF.Round(geoZ / TileSize));

        public (float x, float y, float z) GeoRemap(float x, float z)
        {
            foreach (var r in _remap)
                if (x >= r.MinX && x < r.MaxX && z >= r.MinZ && z < r.MaxZ)
                    return (x + r.Dx, r.Dy, z + r.Dz);
            return (x, 0f, z);
        }

        public override void Update(long tick) { }   // static after seed
    }
}
