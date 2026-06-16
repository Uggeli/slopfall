using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Which primary industries a region's settlements support, beyond the universal
    /// farming every settlement has. v1 is authored ("invent per region"); a
    /// terrain/climate-based discovery (coast detection from the world map) can replace
    /// this later without changing callers. Coastal/island regions add fishing as a
    /// second primary workplace, which absorbs farmhands the land alone can't employ.
    public static class RegionIndustry
    {
        static readonly HashSet<string> Coastal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Betony",            // the island the main quest is fought over
            "Cybiades",          // a small secret island
            "Isle of Balfiera",  // Direnni Tower's isle
        };

        /// True if this region's settlements fish the sea as well as farm the land.
        public static bool IsCoastal(string regionName)
            => regionName != null && Coastal.Contains(regionName);

        /// The primary-sector workplace kinds a settlement in this region runs.
        public static IReadOnlyList<BuildingKind> Workplaces(string regionName)
            => IsCoastal(regionName) ? FarmAndFishery : FarmOnly;

        static readonly BuildingKind[] FarmOnly = { BuildingKind.Farm };
        static readonly BuildingKind[] FarmAndFishery = { BuildingKind.Farm, BuildingKind.Fishery };
    }
}
