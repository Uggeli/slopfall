using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.LarderRegistry. Household
    // provisions keyed by home building index. A continuous quantity that fills (Buy /
    // in-kind farm share) and drains (EatHome), so the intent is a summed DELTA: all
    // deltas to a home this tick add up, order-free, clamped once at zero.

    /// Intent: add Delta provisions to a home larder (negative draws down). Homes < 0
    /// are ignored, matching the legacy guard. Summed in Update(), clamped at zero.
    public struct LarderDeltaIntent : IEvent { public int Building; public double Delta; }

    public sealed class LarderRegistry : Registry
    {
        readonly Dictionary<int, double> _provisions = new Dictionary<int, double>();

        public LarderRegistry(EventBus events) : base(events) { }

        /// Load-time direct write (load runs before ticking, single-threaded): set a
        /// home's provisions to an absolute value (matches the old Set).
        public void Seed(int home, double qty)
        {
            if (home < 0) return;
            _provisions[home] = qty < 0 ? 0 : qty;
        }

        // --- read view (read phase), mirrors the legacy API ---
        public double Get(int home) => home >= 0 && _provisions.TryGetValue(home, out var p) ? p : 0;
        public int Count => _provisions.Count;
        public IEnumerable<KeyValuePair<int, double>> All => _provisions;

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<LarderDeltaIntent>();
            if (intents.Length == 0) return;

            // Sum first (a draw can precede its deposit), then clamp once.
            foreach (var i in intents)
            {
                if (i.Building < 0) continue;
                _provisions[i.Building] = Get(i.Building) + i.Delta;
            }
            foreach (var i in intents)
            {
                if (i.Building < 0) continue;
                if (_provisions[i.Building] < 0) _provisions[i.Building] = 0;
            }
        }
    }
}
