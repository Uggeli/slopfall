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

        public void Swap(Dictionary<EntityId, int> company, Dictionary<int, int> place)
        {
            Interlocked.Exchange(ref _company, company);
            Interlocked.Exchange(ref _place, place);
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
    }
}
