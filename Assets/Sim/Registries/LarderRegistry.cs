using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The household larder: provisions kept at a home, the food a resident draws
    /// on to eat in (EatHome). Keyed by home building (BuildingRegistry index), so
    /// a home's residents share one larder. Continuous quantity (like Stock/coin),
    /// so it fills and empties per game-minute alongside the activity model.
    ///
    /// Parallel to StockRegistry (a shop's shelf), but for a household's food: the
    /// physical backing the GoodsDef "provisions running low" need always implied.
    /// Filled by EconomySystem — Buy deposits bought provisions, farm/fishery work
    /// deposits the hand's in-kind share — and (sub-step 3) drawn down by EatHome.
    /// EconomySystem is the sole writer (it already owns Stock); NeedsSystem reads
    /// it to gate the meal's relief. See docs/subsistence.md.
    public sealed class LarderRegistry
    {
        readonly ConcurrentDictionary<int, double> _provisions = new ConcurrentDictionary<int, double>();

        /// Provisions on hand at a home (0 if none, or if home < 0).
        public double Get(int home) => home >= 0 && _provisions.TryGetValue(home, out var p) ? p : 0;

        /// Add (negative draws down) provisions at a home, clamped at zero; returns
        /// the result. Single-writer, so the read-modify-write needs no lock.
        public double Add(int home, double delta)
        {
            if (home < 0) return 0;
            double v = _provisions.AddOrUpdate(home, delta < 0 ? 0 : delta, (_, cur) => cur + delta);
            if (v < 0) { _provisions[home] = 0; v = 0; }
            return v;
        }

        public void Set(int home, double qty)
        {
            if (home < 0) return;
            _provisions[home] = qty < 0 ? 0 : qty;
        }

        public int Count => _provisions.Count;
        public IEnumerable<KeyValuePair<int, double>> All => _provisions;
    }
}
