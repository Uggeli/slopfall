namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.WorldMarketRegistry. The
    // off-map world as a saturating buyer: per-Good units bought into the market today,
    // reset on the day rollover. Sales are a summed DELTA into the per-good tally; the
    // daily reset is its own intent applied BEFORE the day's sales (so a sale emitted on
    // the rollover tick still lands against the fresh quota). Reuses Good / GoodsCatalog.

    /// Intent: sell Units of a good into the off-map market (summed into today's tally).
    public struct MarketSellIntent : IEvent { public Good Good; public double Units; }

    /// Intent: external demand replenishes — zero the day's tally. Emitted by the
    /// economy on the day rollover (replacing the legacy ResetDay() call).
    public struct MarketResetIntent : IEvent { }

    public sealed class WorldMarketRegistry : Registry
    {
        readonly double[] _bought = new double[GoodsCatalog.Count];

        public WorldMarketRegistry(EventBus events) : base(events) { }

        // --- read view (read phase), mirrors the legacy API ---
        public double BoughtOf(Good good) => _bought[(int)good];

        public override void Update(long tick)
        {
            // Reset first so a rollover-tick sale measures against the fresh day's quota.
            if (Events.GetEvents<MarketResetIntent>().Length > 0)
                System.Array.Clear(_bought, 0, _bought.Length);

            // Then sum the day's sales into the tally (order-free accumulation).
            foreach (var s in Events.GetEvents<MarketSellIntent>())
                if (s.Units > 0) _bought[(int)s.Good] += s.Units;
        }
    }
}
