using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-agent conscience store. Reuses ConscienceData from the
    // enclosing DaggerfallWorkshop.Sim namespace. Seeded by loaders / a future installer
    // system, which emit a whole-value set intent; this registry is the sole applier.

    /// Intent: "store this agent's recomputed conscience charges." Whole-value set.
    public struct ConscienceSetIntent : IEvent { public EntityId Id; public ConscienceData Data; }

    public sealed class ConscienceRegistry : Registry
    {
        readonly Dictionary<EntityId, ConscienceData> _d = new Dictionary<EntityId, ConscienceData>();

        public ConscienceRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<ConscienceSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, ConscienceData data) => _d[id] = data;

        // --- read API ---
        public bool TryGet(EntityId id, out ConscienceData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, ConscienceData>> All => _d;

        /// The aversive charge on a verb for this agent (0 = no qualm).
        public double ChargeFor(EntityId id, ActivityKind verb)
            => _d.TryGetValue(id, out var data) && data.Charge.TryGetValue((int)verb, out var c) ? c : 0;
    }
}
