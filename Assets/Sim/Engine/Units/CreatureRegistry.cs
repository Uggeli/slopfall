using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.CreatureRegistry. Reuses the existing
    // CreatureData struct + EntityId from the parent namespace. CreatureSystem spawns,
    // wanders, and updates creatures via CreatureSetIntent (last write wins per id);
    // dead creatures leave the store on DespawnedEvent (they ARE entities, keyed by
    // EntityId). Read API (TryGet, Contains, All, Count) preserved.

    /// Intent: spawn-or-update a creature row. Last write wins per EntityId.
    public struct CreatureSetIntent : IEvent { public EntityId Id; public CreatureData Data; }

    public sealed class CreatureRegistry : Registry
    {
        readonly Dictionary<EntityId, CreatureData> _d = new Dictionary<EntityId, CreatureData>();

        public CreatureRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            // Sets first, then despawn removals win for the same id this tick.
            foreach (var s in Events.GetEvents<CreatureSetIntent>())
                _d[s.Id] = s.Data;
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        public bool TryGet(EntityId id, out CreatureData data) => _d.TryGetValue(id, out data);
        public bool Contains(EntityId id) => _d.ContainsKey(id);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, CreatureData>> All => _d;
    }
}
