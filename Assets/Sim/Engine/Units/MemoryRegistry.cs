using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-entity episodic memory store. Reuses MemoryData /
    // MemoryEntry / MemoryKind from the enclosing DaggerfallWorkshop.Sim namespace.
    // SocialSystem does whole-row replacement (bounded at MaxEntries) and emits a
    // whole-value set intent; this registry is the sole applier.

    /// Intent: "store this entity's recomputed memory row." Whole-value set.
    public struct MemorySetIntent : IEvent { public EntityId Id; public MemoryData Data; }

    public sealed class MemoryRegistry : Registry
    {
        public const int MaxEntries = 32;

        readonly Dictionary<EntityId, MemoryData> _d = new Dictionary<EntityId, MemoryData>();

        public MemoryRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<MemorySetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API ---
        public bool TryGet(EntityId id, out MemoryData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, MemoryData>> All => _d;
    }
}
