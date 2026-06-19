using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.ResidencyRegistry. Reuses the
    // existing ResidencyData / ResidentRole. Written by TownLoader at spawn (and any
    // future re-housing) as a whole-value set intent; this registry is the sole
    // applier. Removals come from DespawnedEvent.

    /// Intent: set/replace which building an entity belongs to and in what role.
    public struct ResidencySetIntent : IEvent { public EntityId Id; public ResidencyData Data; }

    public sealed class ResidencyRegistry : Registry
    {
        readonly Dictionary<EntityId, ResidencyData> _d = new Dictionary<EntityId, ResidencyData>();

        public ResidencyRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var e in Events.GetEvents<ResidencySetIntent>())
                _d[e.Id] = e.Data;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded). Used by
        /// the ARENA2 loaders, which both write and read back this store mid-load.
        public void Seed(EntityId id, ResidencyData data) => _d[id] = data;

        public bool TryGet(EntityId id, out ResidencyData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, ResidencyData>> All => _d;
    }
}
