using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-entity personality store. Reuses the existing
    // PersonalityData (from the DaggerfallWorkshop.Sim namespace) — traits, derived
    // weights, drift scales, Derive()/Describe() all stay where they are. The
    // registry is the sole writer: its Update() applies last tick's
    // PersonalitySetIntents (whole-value, matching the old ctx.Personality.Set(id,
    // data) surface) and removes despawned entities. In practice this is written once
    // by the loader at spawn.

    /// Intent: "set entity's personality to Data." Whole-value replacement.
    /// PersonalityRegistry is the sole applier.
    public struct PersonalitySetIntent : IEvent { public EntityId Id; public PersonalityData Data; }

    public sealed class PersonalityRegistry : Registry
    {
        readonly Dictionary<EntityId, PersonalityData> _d = new Dictionary<EntityId, PersonalityData>();

        public PersonalityRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<PersonalitySetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;   // last write wins per id

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, PersonalityData data) => _d[id] = data;

        // --- read API (read phase only; mirrors the old registry surface) ---
        public bool TryGet(EntityId id, out PersonalityData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, PersonalityData>> All => _d;
    }
}
