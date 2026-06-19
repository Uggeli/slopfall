using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.EmploymentRegistry. Reuses the
    // existing EmploymentData. Assigned at load (and by any future job-assignment
    // system) as a whole-value set intent; this registry is the sole applier.
    // Removals come from DespawnedEvent.

    /// Intent: set/replace who an entity works for.
    public struct EmploymentSetIntent : IEvent { public EntityId Id; public EmploymentData Data; }

    public sealed class EmploymentRegistry : Registry
    {
        readonly Dictionary<EntityId, EmploymentData> _d = new Dictionary<EntityId, EmploymentData>();

        public EmploymentRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var e in Events.GetEvents<EmploymentSetIntent>())
                _d[e.Id] = e.Data;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, EmploymentData data) => _d[id] = data;

        public bool TryGet(EntityId id, out EmploymentData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, EmploymentData>> All => _d;
    }
}
