using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// A remembered fact-type about a place — one atom in a PLACES record (Atoms
    /// what_is_memory). Extensible: stock seen at a shop, danger met at a spot, a
    /// good alms corner. The first is whether a larder/shelf had provisions, which
    /// lets a hungry agent recall "nothing at home" and not even consider eating
    /// there. Keep values simple (a level/flag); the bag stays a few atoms wide.
    public enum PlaceFact
    {
        ProvisionsHere = 0,   // did this place have food when I last saw it? (>0 yes, 0 none)
    }

    /// The PLACES store (proto). Holds, per agent: which buildings it knows AND the
    /// facts it remembers about them — the recall half of "senses + memories → ads".
    /// An agent considers only places it senses or has learned, and acts on what it
    /// REMEMBERS about them (recall gates ad generation), not on omniscient world
    /// state. Beliefs are deliberately STALE — refreshed only on contact — so a
    /// housemate restocking, or a shelf refilling, isn't known until the agent is
    /// back, the false-belief the memory doc treats as a feature, not a bug.
    ///
    /// Proto-subset of the full memory store: one fact-atom bag per place, no
    /// category-ref / delta-diff / consolidation yet (THINGS/EVENTS/MEANINGS and the
    /// sleep job are the larger vertical). Sole writers: TownLoader (seed), SenseSystem
    /// (Learn on sight), EconomySystem (Note facts on larder/shop contact).
    public sealed class PlaceMemoryRegistry
    {
        public struct Fact { public double Value; public long AsOfTick; }

        sealed class Mind
        {
            public readonly HashSet<int> Known = new HashSet<int>();
            public readonly Dictionary<long, Fact> Facts = new Dictionary<long, Fact>();   // key = (building, factType)
        }

        readonly ConcurrentDictionary<EntityId, Mind> _d = new ConcurrentDictionary<EntityId, Mind>();

        static long Key(int building, PlaceFact fact) => ((long)building << 8) | (uint)fact;

        public void Learn(EntityId id, int building)
        {
            if (building < 0) return;
            _d.GetOrAdd(id, _ => new Mind()).Known.Add(building);
        }

        public bool Knows(EntityId id, int building)
            => _d.TryGetValue(id, out var m) && m.Known.Contains(building);

        public IEnumerable<int> Known(EntityId id)
            => _d.TryGetValue(id, out var m) ? (IEnumerable<int>)m.Known : System.Array.Empty<int>();

        public int CountFor(EntityId id)
            => _d.TryGetValue(id, out var m) ? m.Known.Count : 0;

        /// Remember a fact about a place (contact updates the belief). Knowing a fact
        /// about a place implies knowing the place.
        public void Note(EntityId id, int building, PlaceFact fact, double value, long tick)
        {
            if (building < 0) return;
            var m = _d.GetOrAdd(id, _ => new Mind());
            m.Known.Add(building);
            m.Facts[Key(building, fact)] = new Fact { Value = value, AsOfTick = tick };
        }

        /// Recall a remembered fact. Returns false if the agent has no memory of it
        /// (caller decides the default — "never seen" is not "saw nothing").
        public bool Recall(EntityId id, int building, PlaceFact fact, out Fact f)
        {
            f = default;
            return building >= 0 && _d.TryGetValue(id, out var m) && m.Facts.TryGetValue(Key(building, fact), out f);
        }

        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
    }
}
