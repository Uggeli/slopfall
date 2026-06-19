using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.StockRegistry. Building →
    // double[] by (int)Good. Stock fills (imports/craft) and drains (purchases) by
    // continuous amounts, so the intent is a summed DELTA: all deltas to a (building,
    // good) cell this tick add up, order-free. Reuses Good / GoodsCatalog from the
    // enclosing DaggerfallWorkshop.Sim namespace.

    /// Intent: add Delta to one good's quantity at a building (negative draws down).
    /// All matching intents for a cell are summed in Update(); the result clamps at 0.
    public struct StockDeltaIntent : IEvent { public int Building; public Good Good; public double Delta; }

    public sealed class StockRegistry : Registry
    {
        readonly Dictionary<int, double[]> _d = new Dictionary<int, double[]>();

        public StockRegistry(EventBus events) : base(events) { }

        /// Load-time direct write (load runs before ticking, single-threaded): set one
        /// good's quantity at a building to an absolute value (matches the old Set).
        public void Seed(int building, Good good, double qty)
        {
            if (!_d.TryGetValue(building, out var arr))
            {
                arr = new double[GoodsCatalog.Count];
                _d[building] = arr;
            }
            arr[(int)good] = qty < 0 ? 0 : qty;
        }

        // --- read view (read phase), mirrors the legacy API ---
        public double Get(int building, Good good)
            => _d.TryGetValue(building, out var arr) ? arr[(int)good] : 0;
        public bool TryGet(int building, out double[] goods) => _d.TryGetValue(building, out goods);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<int, double[]>> All => _d;

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<StockDeltaIntent>();
            if (intents.Length == 0) return;

            // Sum every delta into its cell first (order-free), then clamp once at the
            // end — so a within-tick draw that precedes its restock doesn't false-floor.
            foreach (var i in intents)
            {
                if (!_d.TryGetValue(i.Building, out var arr))
                {
                    arr = new double[GoodsCatalog.Count];
                    _d[i.Building] = arr;
                }
                arr[(int)i.Good] += i.Delta;
            }
            foreach (var i in intents)
            {
                var arr = _d[i.Building];
                if (arr[(int)i.Good] < 0) arr[(int)i.Good] = 0;
            }
        }
    }
}
