using System;
using System.IO;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// A settlement's primary industries are DERIVED from its surroundings (the world
    /// climate map), not authored per region. Pure classification + the null-map
    /// fallback need no data; the detection itself is ARENA2-gated.
    public class RegionIndustryTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void Workplaces_AddFisheryOrMine_FromTheGeography()
        {
            var inland = new SettlementData { Coastal = false, Mountainous = false };
            var coast = new SettlementData { Coastal = true, Mountainous = false };
            var mountain = new SettlementData { Coastal = false, Mountainous = true };
            var fjord = new SettlementData { Coastal = true, Mountainous = true };

            // Farmland everywhere; fishery only at the coast; mine only in the hills.
            Assert.Equal(new[] { BuildingKind.Farm }, RegionIndustry.Workplaces(inland));
            Assert.Contains(BuildingKind.Fishery, RegionIndustry.Workplaces(coast));
            Assert.DoesNotContain(BuildingKind.Mine, RegionIndustry.Workplaces(coast));
            Assert.Contains(BuildingKind.Mine, RegionIndustry.Workplaces(mountain));
            Assert.DoesNotContain(BuildingKind.Fishery, RegionIndustry.Workplaces(mountain));
            Assert.Contains(BuildingKind.Fishery, RegionIndustry.Workplaces(fjord));
            Assert.Contains(BuildingKind.Mine, RegionIndustry.Workplaces(fjord));
        }

        [Fact]
        public void DetectInto_NoMap_FallsBackToAuthoredCoastalTable()
        {
            var island = new SettlementData { RegionName = "Betony" };
            var mainland = new SettlementData { RegionName = "Daggerfall" };

            RegionIndustry.DetectInto(null, null, island);     // no world map → authored fallback
            RegionIndustry.DetectInto(null, null, mainland);

            Assert.True(island.Coastal, "Betony is a known coastal region in the fallback table");
            Assert.False(mainland.Coastal);
            Assert.Equal(0, island.ClimateIndex);        // 0 = unknown without a map
        }

        [Fact]
        public void Detection_ReadsTheMap_NotABlanketFlag()
        {
            if (!Available) return;

            var boot = SimBoot.CreateRegion(Arena2, "Betony", 600f);
            var settlements = boot.Ctx.Settlements;

            int coastal = 0, inland = 0, climated = 0;
            foreach (var s in settlements.All)
            {
                if (s.Coastal) coastal++; else inland++;
                if (s.ClimateIndex != 0) climated++;       // the climate map was actually sampled
            }

            // Every settlement got a real climate read, and detection VARIES across the
            // island (interior settlements aren't coastal) — proof it reads the terrain,
            // not a per-region flag.
            Assert.Equal(settlements.Count, climated);
            Assert.True(coastal > 0, "an island should have coastal settlements");
            Assert.True(inland > 0, "some interior Betony settlements should not reach the sea");
        }

        [Fact]
        public void MountainRegion_IsMined_LowlandIsNot()
        {
            if (!Available) return;

            var dragon = SimBoot.CreateRegion(Arena2, "Dragontail Mountains", 600f);
            int mtn = 0, mines = 0;
            foreach (var s in dragon.Ctx.Settlements.All)
            {
                if (s.Mountainous) mtn++;
                foreach (var bi in s.Buildings)
                    if (dragon.Ctx.Buildings.TryGet(bi, out var b) && b.Kind == BuildingKind.Mine) mines++;
            }
            Assert.True(mtn > 0, "a mountain region should have mountainous settlements");
            Assert.True(mines > 0, "mountainous settlements should get a mine synthesized");

            // A lowland island mines nothing.
            foreach (var s in SimBoot.CreateRegion(Arena2, "Betony", 600f).Ctx.Settlements.All)
                Assert.False(s.Mountainous, s.Name + " (lowland Betony) shouldn't read mountainous");
        }
    }
}
