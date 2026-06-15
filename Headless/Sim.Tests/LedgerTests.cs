using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// E0a: the ledger must account for every coin EconomySystem creates or
    /// destroys, so conservation is provable at any tick.
    public class LedgerTests
    {
        static double SumCoin(SimulationContext ctx)
        {
            double s = 0;
            foreach (var kv in ctx.Coin.All) s += kv.Value;
            return s;
        }

        [Fact]
        public void ConservationHolds_AcrossWagesBillsAndCostOfLiving()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);

            // A keeper working (wage faucet) and a patron eating at their tavern
            // (transfer) — plus cost of living on both (sink). Every flow at once.
            var keeper = h.SpawnEntity("Keeper");
            var patron = h.SpawnEntity("Patron");
            int tavern = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern });
            h.Ctx.Stock.Set(tavern, Good.Provisions, 100);   // stocked so meals can be served
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = tavern, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Coin.Set(patron, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());
            h.Ctx.Needs.Set(patron, new NeedsData());
            h.Ctx.Behavior.Set(keeper, new BehaviorData
            {
                Activity = ActivityKind.Work, Phase = ActivityPhase.Doing,
                TargetBuilding = tavern, RemainingGameMinutes = 100000,
            });
            h.Ctx.Behavior.Set(patron, new BehaviorData
            {
                Activity = ActivityKind.EatTavern, Phase = ActivityPhase.Doing,
                TargetBuilding = tavern, RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 18, timeScale: 600f);

            double initial = SumCoin(h.Ctx);
            h.Step(20);

            var ledger = h.Ctx.Ledger.Current;
            double now = SumCoin(h.Ctx);

            // The whole point: money in == money out, exactly.
            Assert.Equal(initial + ledger.Minted - ledger.Sunk, now, 9);
            // No faucet fires here (the keeper mint is retired; this tavern neither
            // exports nor receives a crown remittance), so nothing is minted.
            Assert.Equal(0, ledger.Minted, 9);
            // A sink ran (cost of living) and a transfer (the meal) reached the keeper.
            Assert.True(ledger.Sunk > 0, "nothing sunk");
            Assert.True(ledger.SalesRevenue > 0, "no sales revenue");
            // The meal drew real provisions off the shelf.
            Assert.True(h.Ctx.Stock.Get(tavern, Good.Provisions) < 100, "stock not drawn down by the meal");
        }

        [Fact]
        public void MintAndBurnEvents_AreTallied()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var a = h.SpawnEntity("A");
            h.Ctx.Coin.Set(a, 0.2);
            h.Ctx.Needs.Set(a, new NeedsData());
            h.SeedClock(hour: 12, timeScale: 60f);

            h.Ctx.Events.Emit(new CoinTransferEvent { From = EntityId.None, To = a, Amount = 0.5 }); // mint
            h.Step(2);
            h.Ctx.Events.Emit(new CoinTransferEvent { From = a, To = EntityId.None, Amount = 0.1 }); // burn
            h.Step(2);

            // Residual accounting carries normal float error, so compare with
            // a tolerance rather than exact thresholds.
            var ledger = h.Ctx.Ledger.Current;
            Assert.True(ledger.Minted > 0.49, "mint not tallied: " + ledger.Minted);
            Assert.True(ledger.Sunk > 0.09, "burn not tallied: " + ledger.Sunk);
        }
    }
}
