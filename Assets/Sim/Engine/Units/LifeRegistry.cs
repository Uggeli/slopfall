using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-entity lifespan store. Reuses the existing LifeData
    // (from the DaggerfallWorkshop.Sim namespace). The registry is the sole writer:
    // its Update() applies last tick's LifeSetIntents and removes despawned entities.
    // The aging system reads the store and Publishes intents.
    //
    // The intent carries the two LifeData fields (AgeYears, LifespanYears) rather
    // than the object, so an emitter need not construct/share a LifeData instance —
    // the registry builds the stored value on apply. Net effect matches the old
    // ctx.Life.Set(id, LifeData).

    /// Intent: "set entity's life clock." Carries the AgeYears + LifespanYears the
    /// store should hold for Id. LifeRegistry is the sole applier.
    public struct LifeSetIntent : IEvent { public EntityId Id; public double AgeYears; public double LifespanYears; }

    public sealed class LifeRegistry : Registry
    {
        readonly Dictionary<EntityId, LifeData> _d = new Dictionary<EntityId, LifeData>();

        public LifeRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<LifeSetIntent>();
            for (int i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                _d[s.Id] = new LifeData { AgeYears = s.AgeYears, LifespanYears = s.LifespanYears };
            }

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, LifeData data) => _d[id] = data;

        // --- read API (read phase only; mirrors the old registry surface) ---
        public bool TryGet(EntityId id, out LifeData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, LifeData>> All => _d;
    }
}
