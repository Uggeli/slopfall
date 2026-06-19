using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using Sim.AssetExport;
using Xunit;

namespace Sim.Tests
{
    /// Verifies TownLayout exports block-level Misc3dObjectRecords (wall
    /// segments, gates, misc props) in addition to subrecord building models.
    /// No-ops silently when the ARENA2 data directory is absent.
    public class TownLayoutTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void WalledCity_ExportsMiscObjects_IncludingWalls()
        {
            if (!Available) return;

            // Independently count the model instances the walled city should
            // yield, walking blocks exactly as TownLayout.AddLocation does.
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            DFLocation loc = maps.GetLocation("Daggerfall", "Daggerfall");
            Assert.True(loc.Loaded);

            int width = loc.Exterior.ExteriorData.Width;
            int height = loc.Exterior.ExteriorData.Height;
            int subCount = 0, miscCount = 0;
            for (int by = 0; by < height; by++)
            for (int bx = 0; bx < width; bx++)
            {
                string name = loc.Exterior.ExteriorData.BlockNames[by * width + bx];
                var block = blocks.GetBlock(name);
                if (block.Type != DFBlock.BlockTypes.Rmb || block.RmbBlock.SubRecords == null)
                    continue;
                foreach (var sub in block.RmbBlock.SubRecords)
                    if (sub.Exterior.Block3dObjectRecords != null)
                        subCount += sub.Exterior.Block3dObjectRecords.Length;
                if (block.RmbBlock.Misc3dObjectRecords != null)
                    miscCount += block.RmbBlock.Misc3dObjectRecords.Length;
            }

            // The test is only meaningful if this walled city actually carries
            // block-level misc geometry (it does: wall segments + props).
            Assert.True(miscCount > 0, "expected Daggerfall to carry Misc3dObjectRecords");

            var town = TownLayout.Resolve(Arena2, "Daggerfall", "Daggerfall");

            // One placement per emitted model instance: subrecord + misc.
            Assert.Equal(subCount + miscCount, town.Placements.Count);
        }
    }
}
