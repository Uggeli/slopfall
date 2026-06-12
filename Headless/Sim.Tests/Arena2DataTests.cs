using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using Xunit;

namespace Sim.Tests
{
    /// Golden-value tests against real ARENA2 game data (classic Daggerfall
    /// freeware, Sept 1996). Goldens were captured with `Sim.Host --probe`
    /// and cross-checked against documented canon where it exists (62 regions,
    /// 8x8 city of Daggerfall). Tests no-op silently when the data directory
    /// is absent so the suite still runs on machines without game files.
    public class Arena2DataTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        static MapsFile OpenMaps() =>
            new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);

        [Fact]
        public void MapsFile_HasCanonicalWorldShape()
        {
            if (!Available) return;
            var maps = OpenMaps();

            Assert.Equal(62, maps.RegionCount);

            int totalLocations = 0;
            for (int i = 0; i < maps.RegionCount; i++)
                totalLocations += (int)maps.GetRegion(i).LocationCount;
            Assert.Equal(15251, totalLocations);
        }

        [Fact]
        public void DaggerfallRegion_IsIndex17_With1331Locations()
        {
            if (!Available) return;
            var maps = OpenMaps();

            Assert.Equal(17, maps.GetRegionIndex("Daggerfall"));
            var region = maps.GetRegion(17);
            Assert.Equal("Daggerfall", region.Name);
            Assert.Equal(1331u, region.LocationCount);
        }

        [Fact]
        public void CityOfDaggerfall_LoadsWithKnownLayout()
        {
            if (!Available) return;
            var maps = OpenMaps();

            var loc = maps.GetLocation("Daggerfall", "Daggerfall");
            Assert.True(loc.Loaded);
            Assert.Equal(DFRegion.LocationTypes.TownCity, loc.MapTableData.LocationType);
            Assert.Equal(1291010263, loc.MapTableData.MapId);
            Assert.True(loc.HasDungeon);            // Castle Daggerfall
            Assert.Equal(8, loc.Exterior.ExteriorData.Width);
            Assert.Equal(8, loc.Exterior.ExteriorData.Height);
            Assert.Equal(316, loc.Exterior.BuildingCount);
            Assert.Equal("WALLAA02.RMB", loc.Exterior.ExteriorData.BlockNames[0]);
        }

        [Fact]
        public void BlocksBsa_ResolvesEveryCityBlock()
        {
            if (!Available) return;
            var maps = OpenMaps();
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);

            Assert.Equal(1295, blocks.Count);

            var loc = maps.GetLocation("Daggerfall", "Daggerfall");
            int width = loc.Exterior.ExteriorData.Width;
            int height = loc.Exterior.ExteriorData.Height;
            for (int i = 0; i < width * height; i++)
            {
                string name = loc.Exterior.ExteriorData.BlockNames[i];
                var block = blocks.GetBlock(name);
                Assert.Equal(DFBlock.BlockTypes.Rmb, block.Type);
                Assert.NotNull(block.RmbBlock.SubRecords);
                // Every RMB field header carries the fixed 32-slot building list.
                Assert.Equal(32, block.RmbBlock.FldHeader.BuildingDataList.Length);
            }
        }

        [Fact]
        public void WoodsFile_HeightmapHasCanonicalDimensions()
        {
            if (!Available) return;
            var woods = new WoodsFile(Path.Combine(Arena2, "WOODS.WLD"), FileUsage.UseMemory, true);

            Assert.Equal(1000, WoodsFile.mapWidthValue);
            Assert.Equal(500, WoodsFile.mapHeightValue);

            // Heightmap must contain actual terrain, not a zeroed buffer:
            // scan a horizontal strip through the middle of the map.
            bool anyLand = false;
            for (int x = 0; x < WoodsFile.mapWidthValue; x += 10)
            {
                if (woods.GetHeightMapValue(x, 250) > 2)
                {
                    anyLand = true;
                    break;
                }
            }
            Assert.True(anyLand);
        }
    }
}
