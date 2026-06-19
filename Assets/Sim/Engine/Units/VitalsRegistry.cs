using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the HP/Magicka/Fatigue/Breath store. Reuses the existing
    // VitalsData (from the DaggerfallWorkshop.Sim namespace). The registry is the
    // sole writer: its Update() applies last tick's VitalsSetIntents and removes
    // despawned entities — nothing else.
    //
    // The old write path was the Health SYSTEM applying DamageEvent/HealEvent
    // directly to the store and emitting a death signal. Under CQRS that split is:
    // the Health system consumes DamageEvent/HealEvent, computes the new VitalsData,
    // and Publishes a VitalsSetIntent (+ a DeathEvent on a fatal hit). This registry
    // just applies the resulting VitalsSetIntent — whole-value replacement, matching
    // the old ctx.Vitals.Set(id, VitalsData) surface.

    /// Intent: "set entity's vitals to Data." Whole-value replacement. The Health
    /// system derives Data from Damage/Heal; VitalsRegistry is the sole applier.
    public struct VitalsSetIntent : IEvent { public EntityId Id; public VitalsData Data; }

    public sealed class VitalsRegistry : Registry
    {
        readonly Dictionary<EntityId, VitalsData> _d = new Dictionary<EntityId, VitalsData>();

        public VitalsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<VitalsSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;   // last write wins per id

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, VitalsData data) => _d[id] = data;

        // --- read API (read phase only; mirrors the old registry surface) ---
        public bool TryGet(EntityId id, out VitalsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, VitalsData>> All => _d;
    }
}
