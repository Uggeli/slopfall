using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One agent's place on the lifespan curve (L2 — docs/living_world_L2_lifecycle.md).
    /// Atoms' entry→persist→decay→exit, the exit clock: age advances each game-year
    /// and the mortality hazard rises past LifespanYears until the agent dies of age.
    public sealed class LifeData
    {
        public double AgeYears;
        public double LifespanYears;
    }

    /// Per-entity life state. Writers: the loaders (seed at spawn) + AgingSystem
    /// (age ticks). Removed by LifecycleSystem on despawn.
    public sealed class LifeRegistry
    {
        readonly ConcurrentDictionary<EntityId, LifeData> _d = new ConcurrentDictionary<EntityId, LifeData>();

        public void Set(EntityId id, LifeData data) => _d[id] = data;
        public bool TryGet(EntityId id, out LifeData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, LifeData>> All => _d;
    }
}
