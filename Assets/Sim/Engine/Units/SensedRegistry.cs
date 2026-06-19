using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the raw perception store: who each agent can currently
    // perceive. Value is List<EntityId> (same as the old registry). SenseSystem rebuilds
    // each agent's list each sense-tick and emits a whole-value set intent; this registry
    // is the sole applier.

    /// Intent: "store this agent's rebuilt sensed list." Whole-value set.
    public struct SensedSetIntent : IEvent { public EntityId Id; public List<EntityId> Sensed; }

    public sealed class SensedRegistry : Registry
    {
        static readonly IReadOnlyList<EntityId> Empty = new EntityId[0];
        readonly Dictionary<EntityId, List<EntityId>> _d = new Dictionary<EntityId, List<EntityId>>();

        public SensedRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<SensedSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Sensed;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API ---
        public IReadOnlyList<EntityId> Of(EntityId id) => _d.TryGetValue(id, out var l) ? (IReadOnlyList<EntityId>)l : Empty;
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, List<EntityId>>> All => _d;
    }
}
