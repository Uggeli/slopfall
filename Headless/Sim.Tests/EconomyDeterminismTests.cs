using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// F3: economy passes must be deterministic under stock contention. When a shelf
    /// can't serve everyone this tick, the outcome (who gets the last unit, who pays)
    /// must not depend on registry enumeration order. EconomySystem walks contenders
    /// sorted by EntityId, so the lowest EntityId wins the scarce unit — reproducibly,
    /// and identically across a future parallel Update / region servers.
    public class EconomyDeterminismTests
    {
        [Fact]
        public void ContendedStock_LowestEntityIdWins()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore });
            h.Ctx.Stock.Set(store, Good.Provisions, 0.0001);   // enough for at most one buyer this tick

            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());

            // Two identical buyers; the only thing distinguishing them is their EntityId.
            var first = Buyer(h, store);
            var second = Buyer(h, store);
            Assert.True(first.Value < second.Value, "spawn order should give `first` the lower id");

            h.SeedClock(hour: 12, timeScale: 600f);
            h.Step(1);

            // Both paid the same cost of living; only the winner paid a sale bill on
            // top, so the winner ends with strictly less coin. The lower EntityId must
            // be the winner — by the sort, not by registry luck.
            double firstCoin = h.Ctx.Coin.Get(first);
            double secondCoin = h.Ctx.Coin.Get(second);
            Assert.True(h.Ctx.Ledger.Current.SalesRevenue > 0, "no sale happened at all");
            Assert.True(firstCoin < secondCoin,
                "lowest EntityId should win the scarce unit: first=" + firstCoin + " second=" + secondCoin);
        }

        static EntityId Buyer(SimHarness h, int store)
        {
            var p = h.SpawnEntity("Buyer");
            h.Ctx.Coin.Set(p, 1.0);
            h.Ctx.Needs.Set(p, new NeedsData());
            h.Ctx.Behavior.Set(p, new BehaviorData
            {
                Activity = ActivityKind.Buy, Phase = ActivityPhase.Doing,
                TargetBuilding = store, RemainingGameMinutes = 100000,
            });
            return p;
        }
    }
}
