using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Running audit of the town's money supply. Every coin that EconomySystem
    /// creates from outside the economy (faucets) or destroys (sinks) is tallied
    /// here, so conservation is checkable at any tick:
    ///
    ///     Σ money supply (purses + treasury)  ==  initial Σ  +  Minted  −  Sunk
    ///
    /// The faucets (coin IN from off-map) are exports — producers selling surplus
    /// abroad — and the crown's remittance into the treasury (G6). The sinks (coin
    /// OUT) are imports off-map and the cost of living. The transfer tallies
    /// (SalesRevenue, ServiceRevenue, Wholesale, Alms, Taxes, GuardPay) move coin
    /// between holders and so don't change the total — they show *where* it
    /// circulates. Conservation holds on the full money supply (the treasury, which
    /// tax fills and guard salaries drain, is part of it).
    public sealed class LedgerData
    {
        public double Minted;        // coin created by faucets (exports + crown subsidy)
        public double Sunk;          // coin destroyed by sinks (cost of living, To=None, tavern-to-nobody)
        public double Exports;       // faucet breakdown: coin IN from off-map for exported surplus (G6)
        public double CrownSubsidy;  // faucet breakdown: crown remittance minted into the treasury (G6)
        public double SalesRevenue;  // transfer breakdown: B2C goods bills that reached a keeper
        public double ServiceRevenue;// transfer breakdown: temple/guild/bank service fees (G5)
        public double Alms;          // transfer breakdown: entity→entity charity
        public double Imports;       // sink breakdown: coin sent off-map for imported goods (G2)
        public double Wholesale;     // transfer breakdown: B2B restock bills business→business (G4)
        public double Taxes;         // transfer breakdown: coin collected from purses → treasury (E3)
        public double GuardPay;      // transfer breakdown: treasury → guards' salaries (E3)

        // Goods-flow audit (UNITS, per Good — not coin): what the economy physically
        // moved over the run, so production can be told apart from importing. Live
        // cumulative arrays owned by EconomySystem (sampled + copied by readers, not
        // per-tick snapshots).
        public double[] Produced;    // created from the land/craft (incl. farm in-kind to larders)
        public double[] Imported;    // bought off-map (coin leaves town)
        public double[] Exported;    // surplus shipped off-map (coin enters town)
        public double[] Consumed;    // eaten / used up (left the world for good)
    }

    /// Single-global ledger. Sole writer: EconomySystem (whole-row swap each
    /// tick, same atomic pattern as WorldClockRegistry).
    public sealed class LedgerRegistry
    {
        LedgerData _d = new LedgerData();

        public LedgerData Current => Volatile.Read(ref _d);
        public void Set(LedgerData data) => Interlocked.Exchange(ref _d, data);
    }
}
