using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// G2: a working keeper stocks their business. Craft shops produce wares
    /// from nothing (no coin); stores import staples from off-map, paying the
    /// import price out of town — a real sink the ledger must tally. Hand-set a
    /// Doing/Work behavior (as LedgerTests does) so the keeper keeps working
    /// while EconomySystem runs the supply.
    public class SupplyTests
    {
        static EntityId WorkingKeeper(SimHarness h, int building, double coin)
        {
            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = building, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, coin);
            h.Ctx.Needs.Set(keeper, new NeedsData());
            h.Ctx.Behavior.Set(keeper, new BehaviorData
            {
                Activity = ActivityKind.Work, Phase = ActivityPhase.Doing,
                TargetBuilding = building, RemainingGameMinutes = 100000,
            });
            return keeper;
        }

        [Fact]
        public void GeneralStoreKeeper_ImportsStaples_PayingOffMap()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore });
            h.Ctx.Stock.Set(store, Good.Provisions, 5);   // shelves low → restock
            h.Ctx.Stock.Set(store, Good.Drink, 5);
            var keeper = WorkingKeeper(h, store, 1.0);
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(10);

            // Stock rose toward the target, and the off-map spend was tallied.
            Assert.True(h.Ctx.Stock.Get(store, Good.Provisions) > 5, "provisions not restocked");
            Assert.True(h.Ctx.Stock.Get(store, Good.Drink) > 5, "drink not restocked");
            Assert.True(h.Ctx.Stock.Get(store, Good.Wares) == 0, "a store shouldn't conjure wares");
            Assert.True(h.Ctx.Ledger.Current.Imports > 0, "imports not tallied as a sink");
        }

        [Fact]
        public void CraftKeeper_ProducesWares_AndExportsTheSurplus()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int shop = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.WeaponSmith });
            h.Ctx.Stock.Set(shop, Good.Wares, 0);
            var keeper = WorkingKeeper(h, shop, 0.5);
            double coin0 = h.Ctx.Coin.Get(keeper);
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(15);                                 // long enough to fill the shelf, then export

            // Production is free (no imports), fills the shelf to its target, and the
            // surplus is exported off-map — coin IN that more than covers cost of
            // living, so the smith's purse grows (the productive faucet, G6).
            Assert.True(h.Ctx.Stock.Get(shop, Good.Wares) > 0, "no wares produced");
            Assert.True(h.Ctx.Stock.Get(shop, Good.Wares) <= EconomySystem.StockTarget + 1e-6,
                "export didn't trim the surplus to the shelf target");
            Assert.Equal(0, h.Ctx.Ledger.Current.Imports, 9);
            Assert.True(h.Ctx.Ledger.Current.Exports > 0, "surplus wasn't exported");
            Assert.True(h.Ctx.Coin.Get(keeper) > coin0, "export income didn't reach the keeper");
        }

        [Fact]
        public void Tavern_B2BRestocksProvisions_FromAStore_InTown()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore, X = 0, Z = 0 });
            int tavern = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 10, Z = 0 });
            h.Ctx.Stock.Set(store, Good.Provisions, 40);   // store has stock to wholesale
            h.Ctx.Stock.Set(tavern, Good.Provisions, 0);   // tavern is out → needs B2B

            var storeKeeper = h.SpawnEntity("StoreKeeper");          // the seller (not working)
            h.Ctx.Residency.Set(storeKeeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(storeKeeper, 0.5);
            WorkingKeeper(h, tavern, 1.0);                           // tavern keeper at work → restocks
            h.SeedClock(hour: 12, timeScale: 600f);

            double sellerCoin0 = h.Ctx.Coin.Get(storeKeeper);
            double storeStock0 = h.Ctx.Stock.Get(store, Good.Provisions);
            h.Step(8);

            Assert.True(h.Ctx.Stock.Get(tavern, Good.Provisions) > 0, "tavern wasn't B2B-restocked");
            Assert.True(h.Ctx.Stock.Get(store, Good.Provisions) < storeStock0, "store stock not drawn by the wholesale");
            Assert.True(h.Ctx.Coin.Get(storeKeeper) > sellerCoin0, "store keeper got no wholesale revenue");
            Assert.True(h.Ctx.Ledger.Current.Wholesale > 0, "B2B wholesale not tallied");
            // B2B stays in town: it's a transfer, not an off-map import.
            Assert.Equal(0, h.Ctx.Ledger.Current.Imports, 9);
        }

        [Fact]
        public void Restock_StopsAtTarget()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int shop = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.WeaponSmith });
            h.Ctx.Stock.Set(shop, Good.Wares, EconomySystem.StockTarget);   // already full
            WorkingKeeper(h, shop, 0.5);
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(20);

            // Production never overshoots the target (no infinite hoard).
            Assert.Equal(EconomySystem.StockTarget, h.Ctx.Stock.Get(shop, Good.Wares), 6);
        }
    }
}
