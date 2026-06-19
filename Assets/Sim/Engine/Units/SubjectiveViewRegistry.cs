using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-agent subjective view. Reuses SubjectiveViewData /
    // EntityRead from the enclosing DaggerfallWorkshop.Sim namespace. SubjectiveSystem
    // rebuilds the whole view each sense-tick and emits a whole-value set intent; this
    // registry is the sole applier.

    /// Intent: "store this agent's rebuilt subjective view." Whole-value set.
    public struct SubjectiveSetIntent : IEvent { public EntityId Id; public SubjectiveViewData Data; }

    public sealed class SubjectiveViewRegistry : Registry
    {
        readonly Dictionary<EntityId, SubjectiveViewData> _d = new Dictionary<EntityId, SubjectiveViewData>();

        public SubjectiveViewRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<SubjectiveSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API ---
        public bool TryGet(EntityId id, out SubjectiveViewData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, SubjectiveViewData>> All => _d;
    }
}
