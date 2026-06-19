using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.ProgressionRegistry. Reuses
    // the existing ProgressionData. The progression system computes the new level +
    // skill-point counter and emits it as a whole-value set intent; this registry is
    // the sole applier. Removals come from DespawnedEvent.

    /// Intent: replace an entity's progression (level + skill points) with a new value.
    public struct ProgressionSetIntent : IEvent { public EntityId Id; public ProgressionData Data; }

    public sealed class ProgressionRegistry : Registry
    {
        readonly Dictionary<EntityId, ProgressionData> _d = new Dictionary<EntityId, ProgressionData>();

        public ProgressionRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var e in Events.GetEvents<ProgressionSetIntent>())
                _d[e.Id] = e.Data;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, ProgressionData data) => _d[id] = data;

        public bool TryGet(EntityId id, out ProgressionData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, ProgressionData>> All => _d;
    }
}
