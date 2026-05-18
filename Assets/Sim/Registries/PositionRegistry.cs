using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class PositionData
    {
        public float X, Y, Z;
        public float Yaw;
        public override string ToString() => "(" + X.ToString("F1") + "," + Y.ToString("F1") + "," + Z.ToString("F1") + " yaw=" + Yaw.ToString("F0") + ")";
    }

    /// World position + facing per entity. Phase 1: written by SimMirror from Transform each frame.
    /// Will eventually be authoritative (sim-driven) once Phase 5 lands sim physics.
    public sealed class PositionRegistry
    {
        readonly ConcurrentDictionary<EntityId, PositionData> _d = new ConcurrentDictionary<EntityId, PositionData>();

        public void Set(EntityId id, float x, float y, float z, float yaw)
        {
            _d[id] = new PositionData { X = x, Y = y, Z = z, Yaw = yaw };
        }

        public bool TryGet(EntityId id, out PositionData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, PositionData>> All => _d;
    }
}
