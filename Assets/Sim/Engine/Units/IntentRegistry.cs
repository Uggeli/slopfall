using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of IntentRegistry. Reuses IntentData from the
    // DaggerfallWorkshop.Sim namespace. Sole writer is this registry, applying
    // IntentSetIntent; removes despawned entities.
    //
    // NOTE: the old registry was a transient handoff — OddSystem Set, ExecutionSystem
    // read then Remove()'d the same agent. In the CQRS pattern a registry only writes
    // its own store from intents, so a registry cannot self-clear after a read. The
    // per-agent clear that ExecutionSystem used to do should become an explicit
    // IntentClearIntent emitted by the consumer (added here so the surface is complete).

    /// Intent: "set entity's pending chosen activity." OddSystem emits this whole-value.
    public struct IntentSetIntent : IEvent
    {
        public EntityId Id;
        public IntentData Data;
    }

    /// Intent: "clear entity's pending intent" — the consumer's explicit handoff ack,
    /// replacing the old in-place Remove() after a read.
    public struct IntentClearIntent : IEvent
    {
        public EntityId Id;
    }

    /// Per-agent pending intent.
    public sealed class IntentRegistry : Registry
    {
        readonly Dictionary<EntityId, IntentData> _d = new Dictionary<EntityId, IntentData>();

        public IntentRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var i in Events.GetEvents<IntentSetIntent>())
                _d[i.Id] = i.Data;

            foreach (var c in Events.GetEvents<IntentClearIntent>())
                _d.Remove(c.Id);

            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        public bool TryGet(EntityId id, out IntentData data) => _d.TryGetValue(id, out data);

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, IntentData>> All => _d;
    }
}
