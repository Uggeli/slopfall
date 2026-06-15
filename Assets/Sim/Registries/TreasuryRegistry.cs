using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Public coin held by an owner (OwnerId) — the town treasury, and later each
    /// faction's coffers. Tax flows in here, guard salaries flow out; it's part of
    /// the money supply (conservation counts it alongside entity purses), it just
    /// belongs to an authority rather than a person. Keyed by OwnerId so the crown,
    /// a guild, a temple are all the same shape. Sole writer: EconomySystem (after
    /// TownLoader seeds the opening reserve).
    public sealed class TreasuryRegistry
    {
        readonly ConcurrentDictionary<int, double> _d = new ConcurrentDictionary<int, double>();

        public double Get(OwnerId owner) => _d.TryGetValue(owner.Value, out var c) ? c : 0;

        public void Set(OwnerId owner, double coin) => _d[owner.Value] = coin < 0 ? 0 : coin;

        /// Add (or, negative, draw down) treasury coin, clamped at zero; returns the
        /// resulting balance. Single-writer, so no lock beyond the concurrent map.
        public double Add(OwnerId owner, double delta)
        {
            double v = Get(owner) + delta;
            if (v < 0) v = 0;
            _d[owner.Value] = v;
            return v;
        }

        public double Total
        {
            get { double s = 0; foreach (var v in _d.Values) s += v; return s; }
        }

        public IEnumerable<KeyValuePair<int, double>> All => _d;
    }
}
