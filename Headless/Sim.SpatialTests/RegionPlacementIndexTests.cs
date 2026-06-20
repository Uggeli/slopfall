using System.Collections.Generic;
using System.Linq;
using Sim.AssetExport;
using Xunit;

namespace Sim.SpatialTests
{
    public class RegionPlacementIndexTests
    {
        // A placement whose world translation sits at (geoX, _, geoZ).
        static TownLayout.Placement P(uint model, float geoX, float geoZ)
        {
            var m = new float[16];
            m[0] = m[5] = m[10] = m[15] = 1f;   // identity rotation/scale
            m[12] = geoX; m[13] = 0f; m[14] = geoZ;   // column-major translation
            return new TownLayout.Placement { ModelId = model, Matrix = m };
        }

        static TownLayout.TownData Region(float ts)
        {
            var d = new TownLayout.TownData();
            // bbox mx0=10, my1=23. pixel(mx,my): geoX=(mx-10)*ts, geoZ=(23-my)*ts.
            d.Placements.Add(P(100, 0 * ts, 2 * ts));        // pixel (10,21)
            d.Placements.Add(P(100, 0 * ts, 2 * ts));        // pixel (10,21), same model
            d.Placements.Add(P(200, 3 * ts, 0 * ts));        // pixel (13,23)
            // a "city" placement spilling one pixel east of (10,21):
            d.Placements.Add(P(300, 1 * ts, 2 * ts));        // pixel (11,21)
            d.Flats.Add(new TownLayout.Flat { Archive = 504, Record = 1, X = 0 * ts, Z = 2 * ts });
            return d;
        }

        [Fact]
        public void Build_ConservesEveryPlacement()
        {
            float ts = 819.2f;
            var region = Region(ts);
            var idx = RegionPlacementIndex.Build(region, 10, 23, ts);

            int total = idx.AllTiles().Sum(t => t.tile.Placements.Count);
            Assert.Equal(region.Placements.Count, total);   // none lost, none duplicated
        }

        [Fact]
        public void Build_BucketsByPixelAndDedupesModelIds()
        {
            float ts = 819.2f;
            var idx = RegionPlacementIndex.Build(Region(ts), 10, 23, ts);

            var t1021 = idx.At(10, 21);
            Assert.Equal(2, t1021.Placements.Count);          // the two model-100 placements
            Assert.Equal(new uint[] { 100 }, t1021.ModelIds); // deduped within the tile
            Assert.Single(idx.At(13, 23).Placements);         // model 200
            Assert.Single(idx.At(11, 21).Placements);         // the spill placement (own pixel)
            Assert.Empty(idx.At(99, 99).Placements);          // empty pixel → empty tile, not null
        }

        [Fact]
        public void Build_BucketsFlatsByPixel()
        {
            float ts = 819.2f;
            var idx = RegionPlacementIndex.Build(Region(ts), 10, 23, ts);
            Assert.Single(idx.At(10, 21).Flats);
        }
    }
}
