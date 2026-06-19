using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.PlaceMemoryRegistry. The old API exposed
    // two MUTATORS — Learn(id, building) and Note(id, building, fact, value, tick) —
    // each now an intent carrying exactly those args. The registry owns the per-entity
    // Mind store and applies the intents in Update(). DespawnedEvent removes a dead
    // entity's Mind. Read API (Knows, Known, CountFor, Recall, Count) preserved.
    //
    // Reuses the public Fact struct and PlaceFact enum from the parent
    // DaggerfallWorkshop.Sim.PlaceMemoryRegistry — Recall's out-param keeps that exact
    // type so callers see no signature change. The private Mind type is re-declared
    // here (it was private to the original, not a shared type).

    /// Intent: "I (id) now know building exists." Mirrors the old Learn().
    public struct PlaceLearnIntent : IEvent { public EntityId Id; public int Building; }

    /// Intent: "remember fact about building, value as of tick." Mirrors the old
    /// Note() (which also implies Learn of the building).
    public struct PlaceNoteIntent : IEvent
    {
        public EntityId Id;
        public int Building;
        public PlaceFact Fact;
        public double Value;
        public long Tick;
    }

    public sealed class PlaceMemoryRegistry : Registry
    {
        sealed class Mind
        {
            public readonly HashSet<int> Known = new HashSet<int>();
            public readonly Dictionary<long, PlaceFactValue> Facts
                = new Dictionary<long, PlaceFactValue>(); // key = (building, factType)
        }

        readonly Dictionary<EntityId, Mind> _d = new Dictionary<EntityId, Mind>();

        static long Key(int building, PlaceFact fact) => ((long)building << 8) | (uint)fact;

        public PlaceMemoryRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var l in Events.GetEvents<PlaceLearnIntent>())
                ApplyLearn(l.Id, l.Building);

            foreach (var n in Events.GetEvents<PlaceNoteIntent>())
                ApplyNote(n.Id, n.Building, n.Fact, n.Value, n.Tick);

            // A despawned entity's memories go with it (removals last so a same-tick
            // learn+despawn resolves to gone).
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded): record
        /// that an entity knows a building (matches the old Learn).
        public void Learn(EntityId id, int building) => ApplyLearn(id, building);

        /// Load-time direct write: record a remembered fact about a building (matches
        /// the old Note). Provided for parity; the loaders use Learn.
        public void Note(EntityId id, int building, PlaceFact fact, double value, long tick)
            => ApplyNote(id, building, fact, value, tick);

        void ApplyLearn(EntityId id, int building)
        {
            if (building < 0) return;
            GetOrAdd(id).Known.Add(building);
        }

        void ApplyNote(EntityId id, int building, PlaceFact fact, double value, long tick)
        {
            if (building < 0) return;
            var m = GetOrAdd(id);
            m.Known.Add(building);   // knowing a fact about a place implies knowing it
            m.Facts[Key(building, fact)] =
                new PlaceFactValue { Value = value, AsOfTick = tick };
        }

        Mind GetOrAdd(EntityId id)
        {
            if (!_d.TryGetValue(id, out var m)) { m = new Mind(); _d[id] = m; }
            return m;
        }

        public bool Knows(EntityId id, int building)
            => _d.TryGetValue(id, out var m) && m.Known.Contains(building);

        public IEnumerable<int> Known(EntityId id)
            => _d.TryGetValue(id, out var m) ? (IEnumerable<int>)m.Known : System.Array.Empty<int>();

        public int CountFor(EntityId id)
            => _d.TryGetValue(id, out var m) ? m.Known.Count : 0;

        /// Recall a remembered fact. False if the agent has no memory of it
        /// ("never seen" is not "saw nothing").
        public bool Recall(EntityId id, int building, PlaceFact fact,
                           out PlaceFactValue f)
        {
            f = default;
            return building >= 0 && _d.TryGetValue(id, out var m) && m.Facts.TryGetValue(Key(building, fact), out f);
        }

        public int Count => _d.Count;
    }
}
