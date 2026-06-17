// Thin wrapper over DFU's ClimateSwaps: resolves a location's climate base and
// applies the climate+season texture-archive swap.

using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;

namespace Sim.AssetExport
{
    public static class ClimateSwap
    {
        /// <summary>
        /// Climate base for a location (0=Desert,1=Mountain,2=Temperate,3=Swamp),
        /// read from the world climate PAK at the location's map pixel — same path
        /// the sim uses, so it matches the loaded town.
        /// </summary>
        public static int ClimateBasesOf(MapsFile maps, DFLocation loc)
        {
            var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
            int worldClimate = maps.GetClimateIndex(pix.X, pix.Y);
            DFLocation.ClimateSettings settings = MapsFile.GetWorldClimateSettings(worldClimate);
            switch (settings.ClimateType)
            {
                case DFLocation.ClimateBaseType.Desert: return (int)ClimateBases.Desert;
                case DFLocation.ClimateBaseType.Mountain: return (int)ClimateBases.Mountain;
                case DFLocation.ClimateBaseType.Swamp: return (int)ClimateBases.Swamp;
                default: return (int)ClimateBases.Temperate;
            }
        }

        /// <summary>Climate+season-swapped texture archive for a model submesh.</summary>
        public static int Archive(int archive, int record, int climate, int season)
            => ClimateSwaps.ApplyClimate(archive, record, (ClimateBases)climate, (ClimateSeason)season);
    }
}
