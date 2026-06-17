// Minimal stand-ins for the three DaggerfallWorkshop climate types that the
// (otherwise pure) ClimateSwaps.cs references — the full DaggerfallUnityEnums /
// DaggerfallUnityStructs are huge and Unity-coupled, so we shim just these.
// Values/fields copied verbatim from those files.

namespace DaggerfallWorkshop
{
    public enum ClimateBases { Desert, Mountain, Temperate, Swamp }

    public enum ClimateSeason { Summer, Winter, Rain }

    // Order copied from DaggerfallUnityEnums (snow variants are commented out there).
    public enum ClimateNatureSets
    {
        RainForest, SubTropical, Swamp, Desert,
        TemperateWoodland, WoodlandHills, HauntedWoodlands, Mountains,
    }

    // Only the nested Seasons enum is referenced by ClimateSwaps (nature helpers).
    public static class DaggerfallDateTime
    {
        public enum Seasons { Spring, Summer, Autumn, Winter }
    }

    public struct ClimateTextureInfo
    {
        public DaggerfallConnect.DFLocation.ClimateTextureGroup textureGroup;
        public DaggerfallConnect.DFLocation.ClimateTextureSet textureSet;
        public bool supportsWinter;
        public bool supportsRain;
    }
}
