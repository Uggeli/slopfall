using System.Collections.Generic;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Who is in social company where, rebuilt each tick by SocialSystem
    /// (whole-map swap; readers see last completed tick). Two views:
    /// per-entity company count (NeedsSystem scales social relief with it)
    /// and per-building occupant count (OddSystem's liveliness bonus).
    public sealed class OccupancyRegistry
    {
        Dictionary<EntityId, int> _company = new Dictionary<EntityId, int>();
        Dictionary<int, int> _place = new Dictionary<int, int>();
        Dictionary<int, List<EntityId>> _occupants = new Dictionary<int, List<EntityId>>();
        static readonly List<EntityId> NoOccupants = new List<EntityId>();

        public void Swap(Dictionary<EntityId, int> company, Dictionary<int, int> place,
                         Dictionary<int, List<EntityId>> occupants)
        {
            Interlocked.Exchange(ref _company, company);
            Interlocked.Exchange(ref _place, place);
            Interlocked.Exchange(ref _occupants, occupants);
        }

        /// How many others the entity is currently sharing social time with.
        public int CompanyOf(EntityId id)
        {
            var map = Volatile.Read(ref _company);
            return map.TryGetValue(id, out var n) ? n : 0;
        }

        /// How many people are socially present at a building.
        public int PlaceCount(int buildingIndex)
        {
            var map = Volatile.Read(ref _place);
            return map.TryGetValue(buildingIndex, out var n) ? n : 0;
        }

        /// Who is socially present at a building this tick — the company an
        /// agent's subjective view reads (L3 membrane). Empty if none.
        public IReadOnlyList<EntityId> OccupantsOf(int buildingIndex)
        {
            var map = Volatile.Read(ref _occupants);
            return map.TryGetValue(buildingIndex, out var list) ? list : NoOccupants;
        }
    }
}
