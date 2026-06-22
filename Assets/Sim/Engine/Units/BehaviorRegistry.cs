using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of BehaviorRegistry. Reuses BehaviorData (and ActivityKind/
    // ActivityPhase) from the DaggerfallWorkshop.Sim namespace. This registry is the sole
    // APPLIER of BehaviorSetIntent; removes despawned entities. The intent has MULTIPLE
    // emitters — ExecutionSystem (Doing/Moving agents) and SharedActivitySystem
    // (queued/served agents: the promotion flip + waiter slot placement). There is NO
    // single-writer-of-BehaviorSetIntent invariant; correctness relies on at most one
    // emitter writing a given agent in a given tick (single-writer-PER-AGENT), since the
    // apply below is last-write-wins in nondeterministic parallel order.

    /// Intent: "set entity's current activity to this whole BehaviorData." ExecutionSystem
    /// always sets a freshly-built BehaviorData, so the intent carries the whole row.
    public struct BehaviorSetIntent : IEvent
    {
        public EntityId Id;
        public BehaviorData Data;
    }

    /// Current activity per civilian.
    public sealed class BehaviorRegistry : Registry
    {
        readonly Dictionary<EntityId, BehaviorData> _d = new Dictionary<EntityId, BehaviorData>();

        public BehaviorRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var i in Events.GetEvents<BehaviorSetIntent>())
                _d[i.Id] = i.Data;

            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        public bool TryGet(EntityId id, out BehaviorData data) => _d.TryGetValue(id, out data);

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, BehaviorData>> All => _d;
    }
}
