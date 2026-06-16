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
        public void Workplaces_AddFishery_OnlyWhenCoastal()
        {
            var inland = new SettlementData { Coastal = false };
            var coast = new SettlementData { Coastal = true };

            Assert.DoesNotContain(BuildingKind.Fishery, RegionIndustry.Workplaces(inland));
            Assert.Contains(BuildingKind.Farm, RegionIndustry.Workplaces(inland));
            Assert.Contains(BuildingKind.Fishery, RegionIndustry.Workplaces(coast));
            Assert.Contains(BuildingKind.Farm, RegionIndustry.Workplaces(coast));
        }

        [Fact]
        public void DetectInto_NoMap_FallsBackToAuthoredCoastalTable()
        {
            var island = new SettlementData { RegionName = "Betony" };
            var mainland = new SettlementData { RegionName = "Daggerfall" };

            RegionIndustry.DetectInto(null, island);     // no world map → authored fallback
            RegionIndustry.DetectInto(null, mainland);

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
    }
}
