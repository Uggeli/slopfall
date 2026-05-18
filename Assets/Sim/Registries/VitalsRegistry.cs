using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class VitalsData
    {
        public int CurrentHealth, MaxHealth;
        public int CurrentMagicka, MaxMagicka;
        public int CurrentFatigue, MaxFatigue;
        public int CurrentBreath, MaxBreath;
        public bool IsDead;
    }

    /// HP / Magicka / Fatigue / Breath per entity. Phase 1: mirrored from DaggerfallEntity each frame.
    public sealed class VitalsRegistry
    {
        readonly ConcurrentDictionary<EntityId, VitalsData> _d = new ConcurrentDictionary<EntityId, VitalsData>();

        public void Set(EntityId id, VitalsData data) => _d[id] = data;
        public bool TryGet(EntityId id, out VitalsData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, VitalsData>> All => _d;
    }
}
