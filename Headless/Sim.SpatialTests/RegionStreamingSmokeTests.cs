using System;
using System.IO;
using System.Linq;
using DaggerfallWorkshop.Sim;
using Sim.AssetExport;
using Xunit;

namespace Sim.SpatialTests
{
    /// End-to-end conservation guard: the per-pixel streamed buckets must hold exactly
    /// the same placement set the old whole-region dump returned. No-ops silently when
    /// the ARENA2 game data is absent (mirrors Sim.Tests/Arena2DataTests).
    public class RegionStreamingSmokeTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        const float TileSize = 32768f * TownLayout.GlobalScale;   // 819.2 m
        const float BlockSide = 4096f * TownLayout.GlobalScale;   // 102.4 m

        static (float x, float z) TownCentre(int w, int h)
        {
            int tx = (128 - w * 16) / 2, ty = (128 - h * 16) / 2;
            return (tx / 16f * BlockSide, ty / 16f * BlockSide);
        }

        [Fact]
        public void StreamedTiles_ConserveTheFullRegionPlacementSet()
        {
            if (!Available) return;
            const string region = "Betony";

            var world = SimBoot.CreateRegion(Arena2, region, 0f);
            var assets = new AssetService(Arena2);

            var pAll = world.Pois.All.Where(p => p.HasExterior).ToList();
            if (pAll.Count == 0) return;

            int mx0 = Math.Max(0, pAll.Min(p => p.MapPixelX) - 3);
            int my0 = Math.Max(0, pAll.Min(p => p.MapPixelY) - 3);
            int mx1 = Math.Min(999, pAll.Max(p => p.MapPixelX) + 3);
            int my1 = Math.Min(499, pAll.Max(p => p.MapPixelY) + 3);

            var settlements = pAll.Select(p =>
            {
                var (cx, cz) = TownCentre(p.BlocksWide, p.BlocksHigh);
                return (p.Name, (p.MapPixelX - mx0) * TileSize + cx, 0f, (my1 - p.MapPixelY) * TileSize + cz);
            }).ToList();

            var full = assets.GetRegion(region, settlements);
            var idx = assets.GetRegionIndex(region, settlements, mx0, my1, TileSize);

            int streamed = idx.AllTiles().Sum(t => t.tile.Placements.Count);
            Assert.Equal(full.Placements.Count, streamed);
            Assert.True(full.Placements.Count > 0, "region should have placements");
        }
    }
}
