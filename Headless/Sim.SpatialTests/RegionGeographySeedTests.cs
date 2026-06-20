using System;
using System.IO;
using System.Linq;
using DaggerfallWorkshop.Sim;
using Sim.AssetExport;
using Xunit;

namespace Sim.SpatialTests
{
    /// Stage 2: RegionLoader Pass 3 must seed world.Geography itself (geo is now sim
    /// truth), reproducing the Stage 1 web-host bbox exactly. No-ops without ARENA2.
    public class RegionGeographySeedTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void Pass3_SeedsGeographyWithTheBetonyBbox()
        {
            if (!Available) return;
            var assets = new AssetService(Arena2);

            var world = SimBoot.CreateRegion(Arena2, "Betony", 0f, 12345,
                (loc, w, h) => assets.RegionTileFloor("Betony", loc, w, h),
                TerrainTile.MaxTerrainHeight);

            var g = world.Geography;
            Assert.True(g.HasRegion);
            // Known Stage 1 live values (must be reproduced exactly).
            Assert.Equal(107, g.Mx0);
            Assert.Equal(251, g.My0);
            Assert.Equal(137, g.Mx1);
            Assert.Equal(277, g.My1);
            Assert.Equal(819.2f, g.TileSize, 1);

            // Each exterior POI must have a geo origin set by the loader (not 0,0,0
            // unless it genuinely sits at the bbox origin).
            var pois = world.Pois.All.Where(p => p.HasExterior).ToList();
            Assert.NotEmpty(pois);
            Assert.Contains(pois, p => p.OriginX != 0f || p.OriginZ != 0f);

            // PixelOf round-trips a POI's own geo origin back to its map pixel.
            var s = pois.First(p => p.Settlement != null);
            var (mx, my) = g.PixelOf(s.OriginX, s.OriginZ);
            Assert.Equal(s.MapPixelX, mx);
            Assert.Equal(s.MapPixelY, my);
        }

        [Fact]
        public void CreateRegion_WithoutTileFloor_LeavesGeographyUnseeded()
        {
            if (!Available) return;
            var world = SimBoot.CreateRegion(Arena2, "Betony", 0f);   // no delegate
            Assert.False(world.Geography.HasRegion);   // back-compat for Sim.Host callers
        }
    }
}
