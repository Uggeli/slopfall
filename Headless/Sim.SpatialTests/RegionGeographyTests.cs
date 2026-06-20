using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.SpatialTests
{
    public class RegionGeographyTests
    {
        static RegionGeographyRegistry Seeded()
        {
            var g = new RegionGeographyRegistry(new EventBus());
            // bbox mx 10..13, my 20..23; tile 819.2; datum 0.
            // Two POIs: id 0 at pixel (11,21), id 1 at pixel (12,21).
            g.Seed(10, 20, 13, 23, 819.2f, 0f,
                new[] { (11, 21, 0), (12, 21, 1) },
                new[]
                {
                    // settlement 0 packed rect [0,200)x[0,200) → geo delta (+1000,+5,+2000)
                    new GeoRemapEntry { MinX = 0, MinZ = 0, MaxX = 200, MaxZ = 200,
                                        Dx = 1000, Dy = 5, Dz = 2000 },
                });
            return g;
        }

        [Fact]
        public void PixelOf_InvertsThePlacementFormula()
        {
            var g = Seeded();
            // geoX = (mx-mx0)*ts, geoZ = (my1-my)*ts  →  pixel (12,21)
            float ts = 819.2f;
            float geoX = (12 - 10) * ts, geoZ = (23 - 21) * ts;
            Assert.Equal((12, 21), g.PixelOf(geoX, geoZ));
        }

        [Fact]
        public void PoisAt_ReturnsPoisInThatPixel()
        {
            var g = Seeded();
            Assert.Equal(new[] { 1 }, g.PoisAt(12, 21));
            Assert.Empty(g.PoisAt(13, 23));
        }

        [Fact]
        public void RingPois_IsBoundaryInclusive()
        {
            var g = Seeded();
            // r=1 around (11,21) includes pixel (12,21) → poi 1, and (11,21) → poi 0.
            var ring = g.RingPois(11, 21, 1);
            Assert.Contains(0, ring);
            Assert.Contains(1, ring);
            // r=0 around (12,21) excludes poi 0.
            Assert.DoesNotContain(0, g.RingPois(12, 21, 0));
        }

        [Fact]
        public void GeoRemap_ShiftsPointInsideSettlementRectElseIdentity()
        {
            var g = Seeded();
            Assert.Equal((1100f, 5f, 2050f), g.GeoRemap(100f, 50f));   // inside rect 0
            Assert.Equal((900f, 0f, 900f), g.GeoRemap(900f, 900f));   // outside all rects
        }
    }
}
