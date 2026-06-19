using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.TreasuryRegistry. Public
    // coin held per OwnerId (crown, guild, temple). Tax flows in, guard salaries flow
    // out — a conserved quantity, so the intent is a summed DELTA keyed by owner,
    // order-free, clamped once at zero. Reuses OwnerId from the enclosing namespace.

    /// Intent: add Delta to an owner's treasury (negative draws down). Summed across
    /// all events this tick, then clamped at zero.
    public struct TreasuryDeltaIntent : IEvent { public OwnerId Owner; public double Delta; }

    public sealed class TreasuryRegistry : Registry
    {
        readonly Dictionary<int, double> _d = new Dictionary<int, double>();

        public TreasuryRegistry(EventBus events) : base(events) { }

        /// Load-time direct write (load runs before ticking, single-threaded): add to
        /// an owner's treasury (accumulates, matching the old Add), clamped at zero.
        public double Seed(OwnerId owner, double delta)
        {
            double v = Get(owner) + delta;
            if (v < 0) v = 0;
            _d[owner.Value] = v;
            return v;
        }

        // --- read view (read phase), mirrors the legacy API ---
        public double Get(OwnerId owner) => _d.TryGetValue(owner.Value, out var c) ? c : 0;
        public double Total { get { double s = 0; foreach (var v in _d.Values) s += v; return s; } }
        public IEnumerable<KeyValuePair<int, double>> All => _d;

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<TreasuryDeltaIntent>();
            if (intents.Length == 0) return;

            // Sum first (within-tick: tax in then salaries out), then clamp once.
            foreach (var i in intents)
                _d[i.Owner.Value] = Get(i.Owner) + i.Delta;
            foreach (var i in intents)
                if (_d[i.Owner.Value] < 0) _d[i.Owner.Value] = 0;
        }
    }
}
