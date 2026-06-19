using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-entity directed-opinion store. Reuses RelationsData /
    // RelationData from the enclosing DaggerfallWorkshop.Sim namespace. SocialSystem
    // does whole-row replacement and emits a whole-value set intent; this registry is
    // the sole applier.

    /// Intent: "store this entity's recomputed relations row." Whole-value set.
    public struct RelationsSetIntent : IEvent { public EntityId Id; public RelationsData Data; }

    public sealed class RelationsRegistry : Registry
    {
        readonly Dictionary<EntityId, RelationsData> _d = new Dictionary<EntityId, RelationsData>();

        public RelationsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<RelationsSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API ---
        public bool TryGet(EntityId id, out RelationsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, RelationsData>> All => _d;
    }
}
