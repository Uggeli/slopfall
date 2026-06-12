using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Inspects real ARENA2 data through the DaggerfallConnect readers.
    /// `--probe` lists regions; `--probe <region>` lists its locations;
    /// `--probe <region> <location>` dumps one location's layout.
    public static class DataProbe
    {
        public static string Arena2Path =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        public static int Run(string regionName, string locationName)
        {
            string arena2 = Arena2Path;
            Console.WriteLine("arena2: " + arena2);

            var maps = new MapsFile(System.IO.Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            Console.WriteLine("regions: " + maps.RegionCount);

            if (regionName == null)
            {
                int totalLocations = 0;
                for (int i = 0; i < maps.RegionCount; i++)
                {
                    var region = maps.GetRegion(i);
                    totalLocations += (int)region.LocationCount;
                    if (region.LocationCount > 0)
                        Console.WriteLine("  [" + i.ToString("00") + "] " + region.Name + " — " + region.LocationCount + " locations");
                }
                Console.WriteLine("total locations: " + totalLocations);
                return 0;
            }

            var reg = maps.GetRegion(regionName);
            if (reg.LocationCount == 0) { Console.Error.WriteLine("region empty/unknown: " + regionName); return 1; }

            if (locationName == null)
            {
                Console.WriteLine("region " + reg.Name + ": " + reg.LocationCount + " locations; first 25:");
                for (int i = 0; i < Math.Min(25, (int)reg.LocationCount); i++)
                    Console.WriteLine("  " + reg.MapNames[i]);
                return 0;
            }

            var loc = maps.GetLocation(regionName, locationName);
            if (!loc.Loaded) { Console.Error.WriteLine("location not found: " + locationName); return 1; }

            Console.WriteLine("location: " + loc.Name
                + "  type=" + loc.MapTableData.LocationType
                + "  mapId=" + loc.MapTableData.MapId
                + "  dungeon=" + loc.HasDungeon);
            Console.WriteLine("exterior: " + loc.Exterior.ExteriorData.Width + "x" + loc.Exterior.ExteriorData.Height
                + " blocks, buildings=" + loc.Exterior.BuildingCount);

            var blocks = new BlocksFile(System.IO.Path.Combine(arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            Console.WriteLine("blocks.bsa records: " + blocks.Count);
            for (int y = 0; y < loc.Exterior.ExteriorData.Height; y++)
            {
                for (int x = 0; x < loc.Exterior.ExteriorData.Width; x++)
                {
                    string name = loc.Exterior.ExteriorData.BlockNames[y * loc.Exterior.ExteriorData.Width + x];
                    var block = blocks.GetBlock(name);
                    Console.WriteLine("  block(" + x + "," + y + ") " + name
                        + "  3d=" + (block.RmbBlock.SubRecords != null ? block.RmbBlock.SubRecords.Length : 0)
                        + " people=" + SumPeople(block)
                        + " buildings=" + (block.RmbBlock.FldHeader.BuildingDataList != null ? block.RmbBlock.FldHeader.BuildingDataList.Length : 0));
                }
            }
            return 0;
        }

        static int SumPeople(DFBlock block)
        {
            int people = 0;
            if (block.RmbBlock.SubRecords != null)
                foreach (var sub in block.RmbBlock.SubRecords)
                    if (sub.Exterior.BlockPeopleRecords != null)
                        people += sub.Exterior.BlockPeopleRecords.Length;
            return people;
        }
    }
}
