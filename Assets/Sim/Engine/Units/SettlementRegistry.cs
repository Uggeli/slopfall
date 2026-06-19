using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Intent: an entity joins a settlement's resident roster (spawn / repopulation).
    /// Leaving the roster flows through the existing DespawnedEvent (which carries the
    /// settlement id), so there's no explicit leave intent.
    public struct ResidentJoinIntent : IEvent { public int Settlement; public EntityId Entity; }

    /// All settlements in the loaded region, id-keyed. Seeded by the region loader
    /// (direct Add — load-time, returns the row so the loader can fill it). The only
    /// runtime mutation is the resident roster, applied here from join intents +
    /// DespawnedEvent. Reuses SettlementData/SettlementKind from the parent namespace.
    public sealed class SettlementRegistry : Registry
    {
        readonly List<SettlementData> _all = new List<SettlementData>();

        public SettlementRegistry(EventBus events) : base(events) { }

        /// Load-time seed; same id/treasury assignment as the original.
        public SettlementData Add(string name, string regionName, SettlementKind kind)
        {
            var s = new SettlementData
            {
                Id = _all.Count,
                Name = name,
                RegionName = regionName,
                Kind = kind,
                Treasury = new OwnerId(100 + _all.Count),
            };
            _all.Add(s);
            return s;
        }

        public SettlementData Get(int id) => _all[id];
        public int Count => _all.Count;
        public IReadOnlyList<SettlementData> All => _all;

        public override void Update(long tick)
        {
            // Sole writer of the roster; mutating the Residents list in place is safe in
            // the write phase (no reader runs concurrently).
            foreach (ref readonly var d in Events.GetEvents<DespawnedEvent>())
                if (d.Settlement >= 0 && d.Settlement < _all.Count)
                    _all[d.Settlement].Residents.Remove(d.Entity);

            foreach (ref readonly var j in Events.GetEvents<ResidentJoinIntent>())
                if (j.Settlement >= 0 && j.Settlement < _all.Count)
                    _all[j.Settlement].Residents.Add(j.Entity);
        }
    }
}
