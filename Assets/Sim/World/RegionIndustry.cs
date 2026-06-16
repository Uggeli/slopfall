using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;

namespace DaggerfallWorkshop.Sim
{
    /// Which primary industries a settlement supports, derived from its surroundings —
    /// the hinterland Daggerfall doesn't draw as buildings. Every settlement farms its
    /// land; one near the sea also fishes. The base is read from the world CLIMATE.PAK
    /// at load (DetectInto), so it's discovered from the actual map rather than authored
    /// per region. (An authored coastal table survives only as the fallback for loads
    /// without a map, e.g. synthetic tests.) Mountain/forest → mining/logging is the
    /// next branch here, once those primary goods exist.
    public static class RegionIndustry
    {
        const int CoastRadius = 2;   // sea within this many map-pixels → the town can fish

        /// Read the settlement's geography off the world map and record it (climate +
        /// whether the coast is near). Loaders call this once per settlement before the
        /// employment seed reads Workplaces. `maps` may be null (no world map loaded) —
        /// then fall back to the authored coastal table so behaviour stays defined.
        public static void DetectInto(MapsFile maps, SettlementData s)
        {
            if (maps == null)
            {
                s.ClimateIndex = 0;
                s.Coastal = IsCoastalRegion(s.RegionName);
                return;
            }
            s.ClimateIndex = SampleClimate(maps, s.MapPixelX, s.MapPixelY);
            s.Coastal = SeaNear(maps, s.MapPixelX, s.MapPixelY, CoastRadius);
        }

        /// The primary-sector workplace kinds this settlement's surroundings support:
        /// farmland always, plus a fishery where the sea is near (so an island's idle
        /// hands fish, not just work the few fields).
        public static IReadOnlyList<BuildingKind> Workplaces(SettlementData s)
            => s != null && s.Coastal ? FarmAndFishery : FarmOnly;

        /// True if any map-pixel within `r` of (px,py) is open sea.
        static bool SeaNear(MapsFile maps, int px, int py, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (SampleClimate(maps, px + dx, py + dy) == (int)MapsFile.Climates.Ocean)
                        return true;
            return false;
        }

        /// Climate index at a map-pixel, clamped into range (neighbours of edge towns
        /// fall back to the edge pixel rather than reading out of bounds).
        static int SampleClimate(MapsFile maps, int px, int py)
        {
            if (px < MapsFile.MinMapPixelX) px = MapsFile.MinMapPixelX;
            if (px >= MapsFile.MaxMapPixelX) px = MapsFile.MaxMapPixelX - 1;
            if (py < MapsFile.MinMapPixelY) py = MapsFile.MinMapPixelY;
            if (py >= MapsFile.MaxMapPixelY) py = MapsFile.MaxMapPixelY - 1;
            return maps.GetClimateIndex(px, py);
        }

        // Fallback only (no world map): the known island/coastal regions.
        static readonly HashSet<string> CoastalRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Betony", "Cybiades", "Isle of Balfiera",
        };

        static bool IsCoastalRegion(string regionName)
            => regionName != null && CoastalRegions.Contains(regionName);

        static readonly BuildingKind[] FarmOnly = { BuildingKind.Farm };
        static readonly BuildingKind[] FarmAndFishery = { BuildingKind.Farm, BuildingKind.Fishery };
    }
}
