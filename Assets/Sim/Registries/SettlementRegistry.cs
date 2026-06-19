using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// A settlement's economic archetype, mapped from Daggerfall's location type at
    /// load (DFRegion.LocationTypes → this, so core never references the API enum).
    /// Drives the economic profile (production role, tax, guards, wealth) and the
    /// per-settlement public finance. Primary sector (Hamlet/Village/Farm) vs
    /// secondary+tertiary (City); Tavern/Temple are standalone service sites.
    public enum SettlementKind
    {
        Other = 0,   // unclassified / no permanent economy in v1
        City,        // TownCity — secondary (wares) + finance/services, richer, taxed, guarded
        Hamlet,      // TownHamlet — primary, poor, low/no tax, few/no guards
        Village,     // TownVillage — primary, smallest settled tier
        Farm,        // HomeFarms — hinterland primary production
        Temple,      // ReligionTemple — standalone service site
        Tavern,      // Tavern — standalone roadside service site
    }

    /// One settled place inside a loaded region. Buildings and residents are tagged
    /// to exactly one settlement so the load-time seeds (employment, local knowledge,
    /// guards/treasury) stay settlement-local instead of bleeding across the region.
    /// Written only by the region loader (sim-thread, at load); read thereafter.
    public sealed class SettlementData
    {
        public int Id;
        public string Name;
        public string RegionName;
        public SettlementKind Kind;

        public OwnerId Treasury;            // this settlement's public purse (own OwnerId)

        public int MapPixelX, MapPixelY;    // region-map position — geographic distance for caravans (v3)
        public float OriginX, OriginZ;      // world-space offset of this settlement's tile in the combined grid
        public int BlocksWide, BlocksHigh;

        // Geography, read from the world maps at load (RegionIndustry.DetectInto):
        // which primary industries the surroundings support. ClimateIndex is the
        // CLIMATE.PAK value (223-232; 0 = unknown/no map). Coastal = the sea is within
        // reach (its hands can fish). Elevation is the WOODS.WLD heightmap byte (0-255);
        // Mountainous = high ground (its hands can mine).
        public int ClimateIndex;
        public bool Coastal;
        public int Elevation;
        public bool Mountainous;

        // Dominant race of this settlement's climate zone (FactionFile.FactionRaces;
        // None=-1). Set at load from the location's climate; drives NPC name banks and
        // IdentityData.Race for everyone who lives here (residents, immigrants, guards).
        public int Race = -1;

        // Membership, filled as the loader walks this settlement's blocks.
        public readonly List<int> Buildings = new List<int>();
        public readonly List<EntityId> Residents = new List<EntityId>();
    }
}
