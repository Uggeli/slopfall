using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-entity stats + skills store. Reuses the existing
    // StatsData (from the DaggerfallWorkshop.Sim namespace) — primary attributes,
    // Skills, SkillExp. The registry is the sole writer: its Update() applies last
    // tick's StatsSetIntents (whole-row replacement, matching the old ctx.Stats.Set(
    // id, data) discipline used by the loader and SkillAdvancementSystem) and removes
    // despawned entities.

    /// Intent: "set entity's stats/skills to Data." Whole-row replacement.
    /// StatsRegistry is the sole applier.
    public struct StatsSetIntent : IEvent { public EntityId Id; public StatsData Data; }

    public sealed class StatsRegistry : Registry
    {
        readonly Dictionary<EntityId, StatsData> _d = new Dictionary<EntityId, StatsData>();

        public StatsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<StatsSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;   // last write wins per id

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, StatsData data) => _d[id] = data;

        // --- read API (read phase only; mirrors the old registry surface) ---
        public bool TryGet(EntityId id, out StatsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, StatsData>> All => _d;
    }
}
