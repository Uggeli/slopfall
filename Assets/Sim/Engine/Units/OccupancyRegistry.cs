using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.OccupancyRegistry. The social occupancy
    // view is rebuilt wholesale each tick by SocialSystem, so the intent carries all
    // three freshly-built maps and apply = replace all three (whole-swap). Read API
    // (CompanyOf, PlaceCount, OccupantsOf) preserved. Reuses EntityId from the parent
    // namespace.

    /// Intent: "this is the new occupancy snapshot." Carries the three rebuilt maps;
    /// apply swaps all three at once. Last intent this tick wins.
    public struct OccupancySetIntent : IEvent
    {
        public Dictionary<EntityId, int> Company;       // per-entity company count
        public Dictionary<int, int> Place;              // per-building occupant count
        public Dictionary<int, List<EntityId>> Occupants; // per-building occupant list
    }

    public sealed class OccupancyRegistry : Registry
    {
        Dictionary<EntityId, int> _company = new Dictionary<EntityId, int>();
        Dictionary<int, int> _place = new Dictionary<int, int>();
        Dictionary<int, List<EntityId>> _occupants = new Dictionary<int, List<EntityId>>();
        static readonly List<EntityId> NoOccupants = new List<EntityId>();

        public OccupancyRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<OccupancySetIntent>();
            if (intents.Length == 0) return;
            var i = intents[intents.Length - 1];        // last write wins (whole-swap)
            if (i.Company != null) _company = i.Company;
            if (i.Place != null) _place = i.Place;
            if (i.Occupants != null) _occupants = i.Occupants;
        }

        /// How many others the entity is currently sharing social time with.
        public int CompanyOf(EntityId id)
            => _company.TryGetValue(id, out var n) ? n : 0;

        /// How many people are socially present at a building.
        public int PlaceCount(int buildingIndex)
            => _place.TryGetValue(buildingIndex, out var n) ? n : 0;

        /// Who is socially present at a building this tick (L3 membrane). Empty if none.
        public IReadOnlyList<EntityId> OccupantsOf(int buildingIndex)
            => _occupants.TryGetValue(buildingIndex, out var list) ? list : NoOccupants;
    }
}
