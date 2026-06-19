using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.CoinRegistry. Coin is a
    // CONSERVED quantity that moves between holders, so the intents are DELTA-shaped:
    // every transfer and set this tick is summed in Update(), making accumulation
    // order-free (no read-modify-write race, no last-write-wins clobber of a balance).
    // Reuses the existing CoinTransferEvent (DaggerfallWorkshop.Sim.Engine.SimEvents)
    // and the EntityId / DespawnedEvent types from the enclosing namespaces.

    /// Intent: set an entity's coin to an absolute value (zeroing on escheat, seeding
    /// the opening purse). Last write wins per entity; applied AFTER summed transfers.
    public struct CoinSetIntent : IEvent { public EntityId Id; public double Amount; }

    public sealed class CoinRegistry : Registry
    {
        readonly Dictionary<EntityId, double> _d = new Dictionary<EntityId, double>();

        public CoinRegistry(EventBus events) : base(events) { }

        /// Load-time direct write (load runs before ticking, single-threaded). Matches
        /// the old Set's negative-clamp.
        public void Seed(EntityId id, double coin) => _d[id] = coin < 0 ? 0 : coin;

        // --- read view (read phase) ---
        public double Get(EntityId id) => _d.TryGetValue(id, out var c) ? c : 0;
        public bool TryGet(EntityId id, out double coin) => _d.TryGetValue(id, out coin);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, double>> All => _d;
        public double Total { get { double s = 0; foreach (var v in _d.Values) s += v; return s; } }

        readonly HashSet<EntityId> _touched = new HashSet<EntityId>();

        public override void Update(long tick)
        {
            // 1) Summed transfers: each is a -From / +To delta, accumulated in any
            //    order, clamped ONCE afterward so a within-tick spend that precedes
            //    its income doesn't false-floor at zero (a chained balance concern —
            //    A pays B who pays C in the same tick all nets correctly).
            var transfers = Events.GetEvents<CoinTransferEvent>();
            if (transfers.Length > 0)
            {
                _touched.Clear();
                foreach (var t in transfers)
                {
                    if (!t.From.IsNone) { _d[t.From] = Get(t.From) - t.Amount; _touched.Add(t.From); }
                    if (!t.To.IsNone) { _d[t.To] = Get(t.To) + t.Amount; _touched.Add(t.To); }
                }
                foreach (var id in _touched)
                    if (_d[id] < 0) _d[id] = 0;
            }

            // 2) Absolute sets (seed / escheat zeroing): last write per entity wins,
            //    applied after deltas so a set is authoritative for that tick.
            foreach (var s in Events.GetEvents<CoinSetIntent>())
                _d[s.Id] = s.Amount < 0 ? 0 : s.Amount;

            // 3) Despawned entities leave the money map entirely.
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }
    }
}
