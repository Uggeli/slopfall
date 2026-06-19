using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.StatusFlagsRegistry. Reuses
    // the existing StatusFlags enum. The derive system computes the per-entity flag
    // word from effects and emits it as a whole-value set intent; this registry is
    // the sole applier. Removals come from DespawnedEvent.

    /// Intent: replace an entity's derived status flags with a fresh value.
    public struct StatusFlagsSetIntent : IEvent { public EntityId Id; public StatusFlags Flags; }

    public sealed class StatusFlagsRegistry : Registry
    {
        readonly Dictionary<EntityId, StatusFlags> _d = new Dictionary<EntityId, StatusFlags>();

        public StatusFlagsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var e in Events.GetEvents<StatusFlagsSetIntent>())
                _d[e.Id] = e.Flags;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        public StatusFlags Get(EntityId id) => _d.TryGetValue(id, out var f) ? f : StatusFlags.None;
        public bool TryGet(EntityId id, out StatusFlags flags) => _d.TryGetValue(id, out flags);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, StatusFlags>> All => _d;
    }
}
