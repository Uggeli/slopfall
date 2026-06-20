using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaggerfallWorkshop.Sim;
using Sim.AssetExport;
using Xunit;

namespace Sim.SpatialTests
{
    /// Mixed-climate regions must texture each settlement's buildings with ITS OWN
    /// climate, not a single region-global value. The fix tags every placement with its
    /// settlement's climate; this pins that the tag survives bucketing and that a real
    /// multi-zone region actually carries more than one climate.
    public class ClimateTaggingTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        const float TileSize = 32768f * TownLayout.GlobalScale;
        const float BlockSide = 4096f * TownLayout.GlobalScale;

        static (float x, float z) TownCentre(int w, int h)
        {
            int tx = (128 - w * 16) / 2, ty = (128 - h * 16) / 2;
            return (tx / 16f * BlockSide, ty / 16f * BlockSide);
        }

        static TownLayout.Placement P(uint model, int climate, float geoX, float geoZ)
        {
            var m = new float[16];
            m[0] = m[5] = m[10] = m[15] = 1f;
            m[12] = geoX; m[14] = geoZ;
            return new TownLayout.Placement { ModelId = model, Matrix = m, ClimateBase = climate };
        }

        [Fact]
        public void Index_PreservesPerPlacementClimate()
        {
            float ts = 819.2f;
            var d = new TownLayout.TownData();
            // Two placements in the SAME pixel (10,21) but different climates — exactly
            // the mixed-tile case a large town spilling into a neighbour creates.
            d.Placements.Add(P(100, 1, 0, 2 * ts));   // Mountain
            d.Placements.Add(P(100, 0, 0, 2 * ts));   // Desert
            var idx = RegionPlacementIndex.Build(d, 10, 23, ts);

            var climates = idx.At(10, 21).Placements.Select(p => p.ClimateBase).OrderBy(c => c).ToArray();
            Assert.Equal(new[] { 0, 1 }, climates);
        }

        [Fact]
        public void Region_CarriesPerSettlementClimate()
        {
            if (!Available) return;
            const string region = "Santaki";   // small region spanning >1 climate zone

            var world = SimBoot.CreateRegion(Arena2, region, 0f);
            var assets = new AssetService(Arena2);
            var pAll = world.Pois.All.Where(p => p.HasExterior).ToList();
            if (pAll.Count == 0) return;

            int mx0 = Math.Max(0, pAll.Min(p => p.MapPixelX) - 3);
            int my1 = Math.Min(499, pAll.Max(p => p.MapPixelY) + 3);
            var settlements = pAll.Select(p =>
            {
                var (cx, cz) = TownCentre(p.BlocksWide, p.BlocksHigh);
                return (p.Name, (p.MapPixelX - mx0) * TileSize + cx, 0f, (my1 - p.MapPixelY) * TileSize + cz);
            }).ToList();

            var full = assets.GetRegion(region, settlements);
            var distinctClimates = full.Placements.Select(p => p.ClimateBase).Distinct().ToList();

            // The whole point: a multi-zone region must NOT be uniform.
            Assert.True(distinctClimates.Count > 1,
                $"expected mixed climate, got only {string.Join(",", distinctClimates)}");
            // And every placement must carry a real climate base (0..3), not a default.
            Assert.All(full.Placements, p => Assert.InRange(p.ClimateBase, 0, 3));
        }
    }
}
