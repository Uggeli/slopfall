using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Raw output of SenseSystem: who each agent can currently perceive — in
    /// range and not occluded by a wall. Uninterpreted: PerceptionSystem turns
    /// this into percepts worth acting on (a friend to greet, someone resented
    /// nearby). Rebuilt each sense-tick. Sole writer: SenseSystem.
    public sealed class SensedRegistry
    {
        static readonly IReadOnlyList<EntityId> Empty = new EntityId[0];
        readonly ConcurrentDictionary<EntityId, List<EntityId>> _d = new ConcurrentDictionary<EntityId, List<EntityId>>();

        public void Set(EntityId id, List<EntityId> sensed) => _d[id] = sensed;
        public IReadOnlyList<EntityId> Of(EntityId id) => _d.TryGetValue(id, out var l) ? (IReadOnlyList<EntityId>)l : Empty;
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, List<EntityId>>> All => _d;
    }
}
