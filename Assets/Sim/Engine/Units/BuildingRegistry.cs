using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// All structures of the loaded location, keyed by a stable per-load index.
    /// Seeded by the loader (direct Add — load runs before the tick loop and the
    /// caller needs the index back); static and read-only thereafter, so Update()
    /// is a no-op (no runtime intents). Reuses BuildingRow/BuildingKind from the
    /// parent namespace.
    public sealed class BuildingRegistry : Registry
    {
        readonly Dictionary<int, BuildingRow> _d = new Dictionary<int, BuildingRow>();
        int _nextIndex = -1;

        public BuildingRegistry(EventBus events) : base(events) { }

        /// Load-time seed; returns the index the loader tags buildings/residents with.
        public int Add(BuildingRow row) { int i = ++_nextIndex; _d[i] = row; return i; }

        public bool TryGet(int index, out BuildingRow row) => _d.TryGetValue(index, out row);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<int, BuildingRow>> All => _d;

        public override void Update(long tick) { }   // static after load
    }
}
