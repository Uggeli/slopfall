using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// V-vector axes, ported from ODD's StateAxis convention: every axis is a
    /// DEFICIT that drifts up over time and gets pushed back toward its
    /// setpoint (0) by activities. Uniform direction keeps the gap math
    /// branch-free. Directed emotions (Fear-of-X, Anger-at-X) arrive with the
    /// social/conflict layer later.
    public static class NeedAxis
    {
        public const int Hunger    = 0;
        public const int EnergyDef = 1;   // tiredness
        public const int SocialDef = 2;   // loneliness
        public const int CoinDef   = 3;   // poverty pressure
        public const int GoodsDef  = 4;   // household provisions running low → drives shopping
        public const int Count     = 5;
    }

    public sealed class NeedsData
    {
        /// Deficit per axis, 0 = satisfied. Soft range [0..1]; drift clamps at
        /// 1.5 so an extreme value can still express urgency in scoring.
        public double[] V = new double[NeedAxis.Count];
    }

    /// Per-entity need/emotion state. Seeded by TownLoader; sole writer
    /// thereafter is NeedsSystem (drift + activity effects).
    public sealed class NeedsRegistry
    {
        readonly ConcurrentDictionary<EntityId, NeedsData> _d = new ConcurrentDictionary<EntityId, NeedsData>();

        public void Set(EntityId id, NeedsData data) => _d[id] = data;
        public bool TryGet(EntityId id, out NeedsData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, NeedsData>> All => _d;
    }
}
