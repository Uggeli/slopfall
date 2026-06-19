using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of PositionRegistry. Reuses PositionData from the
    // DaggerfallWorkshop.Sim namespace. Sole writer is this registry, applying
    // PositionSetIntent in Update(); removes despawned entities via DespawnedEvent.

    /// Intent: "set entity's world position + facing." All writers today supply
    /// x,y,z,yaw (SimMirror, MovementSystem, CreatureSystem, TownLoader), so this is
    /// both the whole-value set and the move intent.
    public struct PositionSetIntent : IEvent
    {
        public EntityId Id;
        public float X, Y, Z, Yaw;
    }

    /// World position + facing per entity.
    public sealed class PositionRegistry : Registry
    {
        readonly Dictionary<EntityId, PositionData> _d = new Dictionary<EntityId, PositionData>();

        public PositionRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var i in Events.GetEvents<PositionSetIntent>())
                _d[i.Id] = new PositionData { X = i.X, Y = i.Y, Z = i.Z, Yaw = i.Yaw };

            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        /// Load-time direct write (load runs before ticking, single-threaded).
        public void Seed(EntityId id, float x, float y, float z, float yaw)
            => _d[id] = new PositionData { X = x, Y = y, Z = z, Yaw = yaw };

        public bool TryGet(EntityId id, out PositionData data) => _d.TryGetValue(id, out data);

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, PositionData>> All => _d;
    }
}
