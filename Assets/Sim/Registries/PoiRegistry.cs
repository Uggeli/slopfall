using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim
{
    /// Semantic role of any region location — the superset spanning settled places
    /// (which also carry a SettlementData) and the non-settled POIs that are rendered
    /// now and simulated later. Mapped from DFRegion.LocationTypes in Sim.World
    /// (PoiClassifier.PoiRoleOf), so core never references the API enum.
    ///
    /// Future per-POI simulation purpose (this pass renders only):
    ///   Dungeon      — threat/lair node: monster·bandit·undead source, danger to towns, loot/quests
    ///   Coven        — witch enclave: night activity, reagent/potion trade, recruits from population
    ///   CultShrine   — covert/heretical worship: cultists, rituals, clandestine recruitment
    ///   Graveyard    — undead emergence at night, necromancy, threat seepage into towns
    ///   ManorWealthy — isolated noble household: employs locals, trades, a family unit
    ///   HovelPoor    — isolated subsistence family / hermit
    ///   PlayerShip   — out of scope
    public enum PoiRole
    {
        None = 0,
        City, Hamlet, Village, Farm, Temple, Tavern,   // settled — carry a Settlement
        Dungeon, Coven, CultShrine, Graveyard,         // non-settled
        ManorWealthy, HovelPoor,
        PlayerShip,                                    // excluded from load/render
    }

    public static class PoiRoles
    {
        /// Settled roles carry a permanent population and own a SettlementData.
        public static bool IsSettled(PoiRole r) =>
            r == PoiRole.City || r == PoiRole.Hamlet || r == PoiRole.Village ||
            r == PoiRole.Farm || r == PoiRole.Temple || r == PoiRole.Tavern;
    }

    /// The unified root for ONE region location. Every location in a loaded region
    /// gets exactly one of these. Settled POIs also link the existing SettlementData
    /// (Settlement != null); non-settled POIs carry only role + geography and render
    /// their exterior (HasExterior) with no simulation yet. RawLocationType is the
    /// numeric DFRegion.LocationTypes value (core stays API-enum-free).
    /// Written only by the region loader (sim-thread, at load); read thereafter.
    public sealed class RegionPoi
    {
        public string Name;
        public string RegionName;
        public int RawLocationType;
        public PoiRole Role;

        public int MapPixelX, MapPixelY;       // region-map position
        public float OriginX, OriginY, OriginZ; // world-space render offset (filled by the web boot)
        public int BlocksWide, BlocksHigh;
        public bool HasExterior;                // false when the exterior has no RMB blocks

        public SettlementData Settlement;       // non-null only for settled POIs (the unchanged economy object)
    }

    /// Every POI in the loaded region, in load order. Seeded by the region loader
    /// (direct Add — load-time). Static after load; no runtime mutation, so Update is a no-op.
    public sealed class PoiRegistry : Registry
    {
        readonly List<RegionPoi> _all = new List<RegionPoi>();
        public PoiRegistry(EventBus events) : base(events) { }

        public RegionPoi Add(RegionPoi poi) { _all.Add(poi); return poi; }
        public IReadOnlyList<RegionPoi> All => _all;
        public int Count => _all.Count;

        public override void Update(long tick) { }
    }
}
