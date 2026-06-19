namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.LedgerRegistry. The ledger
    // is a single global audit row (money supply faucets/sinks + goods-flow arrays),
    // recomputed wholesale by the economy each tick — not accumulated here — so the
    // intent carries the whole value and last write wins, the WeatherSetIntent pattern.
    // Reuses LedgerData from the enclosing DaggerfallWorkshop.Sim namespace.

    /// Intent: replace the whole ledger row. Last write this tick wins.
    public struct LedgerSetIntent : IEvent { public LedgerData Value; }

    public sealed class LedgerRegistry : Registry
    {
        LedgerData _d = new LedgerData();

        public LedgerRegistry(EventBus events) : base(events) { }

        /// Read view (read phase only — no concurrent writer under phase separation).
        public LedgerData Current => _d;

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<LedgerSetIntent>();
            if (intents.Length == 0) return;
            var v = intents[intents.Length - 1].Value;   // last write wins
            if (v != null) _d = v;
        }
    }
}
