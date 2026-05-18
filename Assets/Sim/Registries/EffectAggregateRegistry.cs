using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Aggregated modifiers from all active effects on an entity, indexed by
    /// modifier key (e.g. "Strength", "Speed", "MagicResistFire"). Written by
    /// EffectAggregateSystem; read by anything that needs an entity's final
    /// stat values after effects.
    public sealed class EffectAggregateData
    {
        public Dictionary<string, int> Modifiers = new Dictionary<string, int>();
    }

    public sealed class EffectAggregateRegistry
    {
        readonly ConcurrentDictionary<EntityId, EffectAggregateData> _d = new ConcurrentDictionary<EntityId, EffectAggregateData>();

        public void Set(EntityId id, EffectAggregateData data) => _d[id] = data;
        public bool TryGet(EntityId id, out EffectAggregateData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, EffectAggregateData>> All => _d;
    }
}
