using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-agent semantic store. Reuses MeaningsData /
    // CategoryNode from the enclosing DaggerfallWorkshop.Sim namespace. MeaningsSystem
    // folds interactions into categories and decays them, then emits a whole-value set
    // intent; this registry is the sole applier.

    /// Intent: "store this agent's recomputed meanings." Whole-value set.
    public struct MeaningsSetIntent : IEvent { public EntityId Id; public MeaningsData Data; }

    public sealed class MeaningsRegistry : Registry
    {
        readonly Dictionary<EntityId, MeaningsData> _d = new Dictionary<EntityId, MeaningsData>();

        public MeaningsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<MeaningsSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API ---
        public bool TryGet(EntityId id, out MeaningsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, MeaningsData>> All => _d;
    }
}
