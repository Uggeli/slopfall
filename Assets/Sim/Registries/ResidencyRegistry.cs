using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public enum ResidentRole
    {
        Resident,   // lives here
        Keeper,     // works here (shopkeeper, publican, priest, guild steward)
    }

    public sealed class ResidencyData
    {
        public int BuildingIndex;   // key into BuildingRegistry
        public ResidentRole Role;
    }

    /// Which building an entity belongs to and in what role. Written by
    /// TownLoader at spawn; later the ODD layer reads this for "go home at
    /// dusk / open shop at dawn" scheduling.
    public sealed class ResidencyRegistry
    {
        readonly ConcurrentDictionary<EntityId, ResidencyData> _d = new ConcurrentDictionary<EntityId, ResidencyData>();

        public void Set(EntityId id, ResidencyData data) => _d[id] = data;
        public bool TryGet(EntityId id, out ResidencyData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, ResidencyData>> All => _d;
    }
}
