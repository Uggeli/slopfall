using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS store for per-agent lineage (family-tree hook). Reuses LineageData from the
    // enclosing DaggerfallWorkshop.Sim namespace. Seeded by loaders; a future
    // genealogy/faction system emits a whole-value set intent (e.g. on birth/marriage)
    // and this registry is the sole applier.

    /// Intent: "store this agent's lineage row." Whole-value set.
    public struct LineageSetIntent : IEvent { public EntityId Id; public LineageData Data; }

    public sealed class LineageRegistry : Registry
    {
        readonly Dictionary<EntityId, LineageData> _d = new Dictionary<EntityId, LineageData>();

        public LineageRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<LineageSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, LineageData data) => _d[id] = data;

        // --- read API ---
        public bool TryGet(EntityId id, out LineageData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, LineageData>> All => _d;
    }
}
