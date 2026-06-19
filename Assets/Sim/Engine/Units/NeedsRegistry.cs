using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-entity need/emotion store. Reuses the existing
    // NeedsData (from the DaggerfallWorkshop.Sim namespace) — same V-vector
    // semantics (NeedAxis). The registry is the sole writer: its Update() applies
    // last tick's NeedsSetIntents to its own storage, and removes despawned
    // entities. NeedsSystem reads the store and Publishes intents (whole-value set,
    // matching the old ctx.Needs.Set(id, NeedsData) surface).

    /// Intent: "set entity's need vector to Data." Whole-value replacement, exactly
    /// the old ctx.Needs.Set(id, data) discipline. NeedsRegistry is the sole applier.
    public struct NeedsSetIntent : IEvent { public EntityId Id; public NeedsData Data; }

    public sealed class NeedsRegistry : Registry
    {
        readonly Dictionary<EntityId, NeedsData> _d = new Dictionary<EntityId, NeedsData>();

        public NeedsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<NeedsSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;   // last write wins per id

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, NeedsData data) => _d[id] = data;

        // --- read API (read phase only; mirrors the old registry surface) ---
        public bool TryGet(EntityId id, out NeedsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, NeedsData>> All => _d;
    }
}
