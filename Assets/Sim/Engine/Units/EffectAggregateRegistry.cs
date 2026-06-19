using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.EffectAggregateRegistry.
    // Reuses the existing EffectAggregateData. The aggregate system recomputes the
    // per-entity modifier sums and emits them as a whole-value set intent; this
    // registry is the sole applier. Removals come from DespawnedEvent.

    /// Intent: replace an entity's aggregated effect modifiers with a fresh snapshot.
    public struct EffectAggregateSetIntent : IEvent { public EntityId Id; public EffectAggregateData Data; }

    public sealed class EffectAggregateRegistry : Registry
    {
        readonly Dictionary<EntityId, EffectAggregateData> _d = new Dictionary<EntityId, EffectAggregateData>();

        public EffectAggregateRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var e in Events.GetEvents<EffectAggregateSetIntent>())
                _d[e.Id] = e.Data;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        public bool TryGet(EntityId id, out EffectAggregateData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, EffectAggregateData>> All => _d;
    }
}
