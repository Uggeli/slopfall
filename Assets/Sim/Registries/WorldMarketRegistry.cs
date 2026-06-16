namespace DaggerfallWorkshop.Sim
{
    /// The off-map world as a finite, saturating buyer — the seam that stops exports
    /// being an infinite full-price faucet. Each good has a daily demand quota
    /// (GoodsCatalog.WorldDemandPerDay); this just tracks how much has been sold into
    /// the market so far today. EconomySystem reads Bought-vs-quota to price exports
    /// (price falls once the quota is exceeded) and to let producers DECIDE whether a
    /// sale is still worth making. One shared counter for the whole sim, so every
    /// settlement competes for the same external demand (a glut anywhere cuts the price
    /// everywhere) — and when multiple regions are simulated, they compete here too.
    ///
    /// Sole writer: EconomySystem (Sell during Update, ResetDay on the day rollover).
    public sealed class WorldMarketRegistry
    {
        readonly double[] _bought = new double[GoodsCatalog.Count];   // units sold into the market this day

        public double BoughtOf(Good good) => _bought[(int)good];

        public void Sell(Good good, double qty)
        {
            if (qty > 0) _bought[(int)good] += qty;
        }

        /// New day: external demand replenishes, so the day's tally resets.
        public void ResetDay()
        {
            for (int i = 0; i < _bought.Length; i++) _bought[i] = 0;
        }
    }
}
