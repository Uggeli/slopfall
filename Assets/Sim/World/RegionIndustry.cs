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
        const int CoastRadius = 2;          // sea within this many map-pixels → the town can fish

        /// Read the settlement's geography off the world maps and record it: climate +
        /// whether the coast is near (CLIMATE.PAK), and the ground elevation (WOODS.WLD).
        /// Loaders call this once per settlement before the employment seed reads
        /// Workplaces. `maps` may be null (no world map loaded) — then fall back to the
        /// authored coastal table so behaviour stays defined.
        ///
        /// "Mountainous" is read from the CLIMATE map's terrain class (Mountain /
        /// MountainWoods), NOT the heightmap: settlements are founded in the valleys,
        /// so their pixel elevation doesn't separate a mountain region from a lowland
        /// one (probed: Dragontail towns 27–55 vs lowland Betony 7–45 — overlapping).
        /// Bethesda's climate map already classifies the terrain type, so it's the
        /// reliable signal; the WOODS elevation is recorded for observability + later
        /// refinement (e.g. high-camp vs valley mining, terrain-aware travel).
        public static void DetectInto(MapsFile maps, WoodsFile woods, SettlementData s)
        {
            if (maps == null)
            {
                s.ClimateIndex = 0;
                s.Coastal = IsCoastalRegion(s.RegionName);
                s.Elevation = 0;
                s.Mountainous = false;
                return;
            }
            s.ClimateIndex = SampleClimate(maps, s.MapPixelX, s.MapPixelY);
            s.Coastal = SeaNear(maps, s.MapPixelX, s.MapPixelY, CoastRadius);
            s.Elevation = woods != null ? woods.GetHeightMapValue(s.MapPixelX, s.MapPixelY) : 0;
            s.Mountainous = IsMountainClimate(s.ClimateIndex);
        }

        /// Mountain terrain per the climate map — where the rock is worth mining.
        static bool IsMountainClimate(int climateIndex)
            => climateIndex == (int)MapsFile.Climates.Mountain
            || climateIndex == (int)MapsFile.Climates.MountainWoods;

        /// The primary-sector workplace kinds this settlement's surroundings support:
        /// farmland always (every settlement grows some food), plus a fishery where the
        /// sea is near and a mine in mountain country — so an island's idle hands fish
        /// and a mountain town's hands dig, instead of crowding the few fields.
        public static IReadOnlyList<BuildingKind> Workplaces(SettlementData s)
        {
            if (s == null) return FarmOnly;
            if (s.Coastal && s.Mountainous) return FarmFisheryMine;
            if (s.Coastal) return FarmAndFishery;
            if (s.Mountainous) return FarmAndMine;
            return FarmOnly;
        }

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

        // Every settlement gets food (farm) + the wool→cloth chain (pasture + weaver) —
        // the non-food export faucet (docs/industry_layers.md); terrain adds the coastal
        // fishery / mountain mine. (Per-settlement specialisation is a later refinement.)
        static readonly BuildingKind[] FarmOnly = { BuildingKind.Farm, BuildingKind.Pasture, BuildingKind.Weaver };
        static readonly BuildingKind[] FarmAndFishery = { BuildingKind.Farm, BuildingKind.Fishery, BuildingKind.Pasture, BuildingKind.Weaver };
        static readonly BuildingKind[] FarmAndMine = { BuildingKind.Farm, BuildingKind.Mine, BuildingKind.Pasture, BuildingKind.Weaver };
        static readonly BuildingKind[] FarmFisheryMine = { BuildingKind.Farm, BuildingKind.Fishery, BuildingKind.Mine, BuildingKind.Pasture, BuildingKind.Weaver };
    }
}
