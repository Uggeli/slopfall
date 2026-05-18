using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class ProgressionData
    {
        public int Level;
        public int SkillPointsThisLevel;   // counter ProgressionSystem maintains
    }

    /// Per-entity level + skill-points-toward-next-level. Sole writer:
    /// ProgressionSystem. IdentityRegistry.Level is kept in sync after a level
    /// up (cross-registry write but one-direction).
    public sealed class ProgressionRegistry
    {
        readonly ConcurrentDictionary<EntityId, ProgressionData> _d = new ConcurrentDictionary<EntityId, ProgressionData>();

        public void Set(EntityId id, ProgressionData data) => _d[id] = data;
        public bool TryGet(EntityId id, out ProgressionData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, ProgressionData>> All => _d;
    }
}
