using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Real goods held by each building — the inventory the economy actually
    /// moves. Building (by BuildingRegistry index) → quantity of each Good, a
    /// flat array indexed by (int)Good. Quantities are continuous (like coin and
    /// needs), so trade can flow per game-minute alongside the activity model
    /// rather than only in whole units.
    ///
    /// Seeded at load (TownLoader.SeedStock); thereafter the sole writer is
    /// EconomySystem — imports and craft add stock (G2), purchases draw it down
    /// (G3/G4). Other systems read it (what's for sale, whether a tavern can
    /// still serve a meal).
    public sealed class StockRegistry
    {
        readonly ConcurrentDictionary<int, double[]> _d = new ConcurrentDictionary<int, double[]>();

        public double Get(int building, Good good)
            => _d.TryGetValue(building, out var arr) ? arr[(int)good] : 0;

        public bool TryGet(int building, out double[] goods) => _d.TryGetValue(building, out goods);

        public void Set(int building, Good good, double qty)
        {
            var arr = _d.GetOrAdd(building, _ => new double[GoodsCatalog.Count]);
            arr[(int)good] = qty < 0 ? 0 : qty;
        }

        /// Add (or, with a negative delta, draw down) stock, clamped at zero;
        /// returns the resulting quantity. Single-writer, so the read-modify-write
        /// needs no lock beyond the concurrent map's array creation.
        public double Add(int building, Good good, double delta)
        {
            var arr = _d.GetOrAdd(building, _ => new double[GoodsCatalog.Count]);
            double v = arr[(int)good] + delta;
            if (v < 0) v = 0;
            arr[(int)good] = v;
            return v;
        }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<int, double[]>> All => _d;
    }
}
