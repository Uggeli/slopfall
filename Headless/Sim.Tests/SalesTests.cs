using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// G3 (B2C): a purchase is a real transaction — the good leaves the seller's
    /// shelf, coin reaches the keeper, and the buyer's need is met. No stock → no
    /// sale and no relief. Hand-set a Doing behavior (as LedgerTests does) so the
    /// patron keeps buying while EconomySystem + NeedsSystem run.
    public class SalesTests
    {
        static EntityId Patron(SimHarness h, ActivityKind activity, int building, double coin, NeedsData needs)
        {
            var p = h.SpawnEntity("Patron");
            h.Ctx.Coin.Set(p, coin);
            h.Ctx.Needs.Set(p, needs);
            h.Ctx.Behavior.Set(p, new BehaviorData
            {
                Activity = activity, Phase = ActivityPhase.Doing,
                TargetBuilding = building, RemainingGameMinutes = 100000,
            });
            return p;
        }

        [Fact]
        public void Buy_DrawsStock_PaysKeeper_AndMeetsTheGoodsNeed()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore });
            h.Ctx.Stock.Set(store, Good.Provisions, 50);

            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());

            var needs = new NeedsData();
            needs.V[NeedAxis.GoodsDef] = 1.0;             // wants goods
            var patron = Patron(h, ActivityKind.Buy, store, 1.0, needs);
            h.SeedClock(hour: 12, timeScale: 600f);

            double keeper0 = h.Ctx.Coin.Get(keeper);
            double stock0 = h.Ctx.Stock.Get(store, Good.Provisions);
            h.Step(8);

            Assert.True(h.Ctx.Stock.Get(store, Good.Provisions) < stock0, "stock not drawn down");
            Assert.True(h.Ctx.Coin.Get(keeper) > keeper0, "keeper got no sale revenue");
            Assert.True(h.Ctx.Coin.Get(patron) < 1.0, "patron paid nothing");
            Assert.True(h.Ctx.Needs.TryGet(patron, out var pn) && pn.V[NeedAxis.GoodsDef] < 1.0,
                "the goods need wasn't met by buying");
            Assert.True(h.Ctx.Ledger.Current.SalesRevenue > 0, "sale not tallied");
        }

        [Fact]
        public void EmptyShelf_NoSale_NoRelief()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore });
            h.Ctx.Stock.Set(store, Good.Provisions, 0);   // empty shelf

            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());

            var needs = new NeedsData();
            needs.V[NeedAxis.GoodsDef] = 1.0;
            var patron = Patron(h, ActivityKind.Buy, store, 1.0, needs);
            h.SeedClock(hour: 12, timeScale: 600f);

            double keeper0 = h.Ctx.Coin.Get(keeper);
            h.Step(8);

            // Nothing to sell: keeper earns nothing (no Behavior on them, so coin is
            // untouched), and the goods need is NOT satisfied — it only drifts up.
            Assert.Equal(0, h.Ctx.Ledger.Current.SalesRevenue, 9);
            Assert.Equal(keeper0, h.Ctx.Coin.Get(keeper), 9);
            Assert.True(h.Ctx.Needs.TryGet(patron, out var pn) && pn.V[NeedAxis.GoodsDef] >= 1.0,
                "goods need fell with no goods to buy");
        }

        [Fact]
        public void EmptyShop_DoesNotAdvertiseBuy()
        {
            var h = new SimHarness();
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore, X = 5, Z = 5 });
            h.Ctx.Stock.Set(store, Good.Provisions, 0);   // empty

            var shopper = h.SpawnEntity("Shopper");
            h.Ctx.Position.Set(shopper, 5, 0, 5, 0);
            h.Ctx.Residency.Set(shopper, new ResidencyData { BuildingIndex = 999, Role = ResidentRole.Resident });
            h.Ctx.PlaceMemory.Learn(shopper, store);
            h.SeedClock(hour: 12, timeScale: 600f);
            h.Step(1);

            var ads = ActionDiscovery.GatherAds(h.Ctx, shopper);
            Assert.DoesNotContain(ads, a => a.Verb == ActivityKind.Buy);

            // Restock it and the Buy ad reappears — the shelf is the precondition.
            h.Ctx.Stock.Set(store, Good.Provisions, 10);
            ads = ActionDiscovery.GatherAds(h.Ctx, shopper);
            Assert.Contains(ads, a => a.Verb == ActivityKind.Buy);
        }
    }
}
