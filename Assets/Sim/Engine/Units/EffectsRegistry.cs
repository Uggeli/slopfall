using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.EffectsRegistry. Reuses the
    // existing EffectsData / EffectInstance value types. The effect system computes
    // the new per-entity effect list and emits it as a whole-value set intent; this
    // registry is the sole applier. Removals come from DespawnedEvent.

    /// Intent: replace an entity's active effects with a freshly-computed snapshot.
    public struct EffectsSetIntent : IEvent { public EntityId Id; public EffectsData Data; }

    public sealed class EffectsRegistry : Registry
    {
        readonly Dictionary<EntityId, EffectsData> _d = new Dictionary<EntityId, EffectsData>();

        public EffectsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var e in Events.GetEvents<EffectsSetIntent>())
                _d[e.Id] = e.Data;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        public bool TryGet(EntityId id, out EffectsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, EffectsData>> All => _d;
    }
}
