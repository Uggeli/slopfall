using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Which buildings an entity knows — the recall half of "senses + memories →
    /// ads". An agent only considers places it currently senses or has learned by
    /// being near them, so memory becomes load-bearing: you go to the pub you
    /// know, and habits form around known places. Seeded with home (and workplace)
    /// at load; grown by SenseSystem. Sole writers: TownLoader (seed) + SenseSystem.
    public sealed class PlaceMemoryRegistry
    {
        readonly ConcurrentDictionary<EntityId, HashSet<int>> _d = new ConcurrentDictionary<EntityId, HashSet<int>>();

        public void Learn(EntityId id, int building)
        {
            if (building < 0) return;
            _d.GetOrAdd(id, _ => new HashSet<int>()).Add(building);
        }

        public bool Knows(EntityId id, int building)
            => _d.TryGetValue(id, out var set) && set.Contains(building);

        public IEnumerable<int> Known(EntityId id)
            => _d.TryGetValue(id, out var set) ? (IEnumerable<int>)set : System.Array.Empty<int>();

        public int CountFor(EntityId id)
            => _d.TryGetValue(id, out var set) ? set.Count : 0;

        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
    }
}
