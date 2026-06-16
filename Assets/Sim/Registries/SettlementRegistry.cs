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

        // Membership, filled as the loader walks this settlement's blocks.
        public readonly List<int> Buildings = new List<int>();
        public readonly List<EntityId> Residents = new List<EntityId>();
    }

    /// All settlements in the loaded region, keyed by settlement id. Single-writer
    /// (region loader at load); enumeration is over a stable id-ordered list so any
    /// per-settlement pass is deterministic (same discipline as the economy walk).
    public sealed class SettlementRegistry
    {
        readonly List<SettlementData> _all = new List<SettlementData>();

        /// Allocate the next settlement, giving it an id and its own treasury OwnerId.
        /// OwnerId.Town (1) stays the sentinel; settlement purses start at 100 so each
        /// settlement is an isolated public-finance unit (taxes its own residents, pays
        /// its own guards). Single-town becomes OwnerId(100) — the same single pool with
        /// a different id, so amounts are unchanged.
        public SettlementData Add(string name, string regionName, SettlementKind kind)
        {
            var s = new SettlementData
            {
                Id = _all.Count,
                Name = name,
                RegionName = regionName,
                Kind = kind,
                Treasury = new OwnerId(100 + _all.Count),
            };
            _all.Add(s);
            return s;
        }

        public SettlementData Get(int id) => _all[id];
        public int Count => _all.Count;
        public IReadOnlyList<SettlementData> All => _all;
    }
}
