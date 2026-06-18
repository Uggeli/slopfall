using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// The Subsistence slice (docs/subsistence.md): food costs ingredients, kept in
    /// a household larder. Provisions reach the larder by Buy (coin), in-kind farm
    /// work, or theft; theft is the Take verb, charged by conscience so the honest
    /// won't. S1 (larder + fill) is behavior-preserving; S2 wires Steal. Magnitudes
    /// are frozen placeholders — these pin the WIRING, not the numbers.
    public class SubsistenceTests
    {
        // --- S1: the larder fills from buying and from working the land ---

        [Fact]
        public void Buy_RestocksTheHomeLarder()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int home = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1 });
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore });
            h.Ctx.Stock.Set(store, Good.Provisions, 50);

            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());

            var buyer = h.SpawnEntity("Buyer");
            h.Ctx.Residency.Set(buyer, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            h.Ctx.Coin.Set(buyer, 1.0);
            var needs = new NeedsData(); needs.V[NeedAxis.GoodsDef] = 1.0;
            h.Ctx.Needs.Set(buyer, needs);
            h.Ctx.Behavior.Set(buyer, new BehaviorData
            {
                Activity = ActivityKind.Buy, Phase = ActivityPhase.Doing,
                TargetBuilding = store, RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 12, timeScale: 600f);

            Assert.Equal(0, h.Ctx.Larder.Get(home), 9);
            h.Step(8);

            Assert.True(h.Ctx.Larder.Get(home) > 0, "buying provisions didn't stock the home larder");
        }

        [Fact]
        public void FarmHand_TakesHomeInKindProvisions()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int home = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1 });
            int farm = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Farm });

            var keeper = h.SpawnEntity("FarmKeeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = farm, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 5.0);

            var hand = h.SpawnEntity("Hand");
            h.Ctx.Residency.Set(hand, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            h.Ctx.Employment.Set(hand, new EmploymentData { Employer = keeper });
            h.Ctx.Coin.Set(hand, 0.0);
            h.Ctx.Needs.Set(hand, new NeedsData());
            h.Ctx.Behavior.Set(hand, new BehaviorData
            {
                Activity = ActivityKind.Farm, Phase = ActivityPhase.Doing,
                TargetBuilding = farm, RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 10, timeScale: 600f);

            h.Step(8);
            Assert.True(h.Ctx.Larder.Get(home) > 0, "a working farm hand brought home no provisions");
        }

        // --- S2: theft individuates a discrete carried loaf (no larder teleport) ---

        [Fact]
        public void Steal_IndividuatesACarriedLoaf_OwnedByKeeper_NotToLarder()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int home = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1 });
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore });
            h.Ctx.Stock.Set(store, Good.Provisions, 50);

            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());

            var thief = h.SpawnEntity("Thief");
            h.Ctx.Residency.Set(thief, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            h.Ctx.Coin.Set(thief, 0.0);   // broke — the whole point
            var needs = new NeedsData(); needs.V[NeedAxis.GoodsDef] = 1.2;
            h.Ctx.Needs.Set(thief, needs);
            h.Ctx.Behavior.Set(thief, new BehaviorData
            {
                Activity = ActivityKind.Steal, Phase = ActivityPhase.Doing,
                TargetBuilding = store, RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 12, timeScale: 600f);

            double stock0 = h.Ctx.Stock.Get(store, Good.Provisions);
            double keeper0 = h.Ctx.Coin.Get(keeper);
            h.Step(8);

            // Off the shelf, free — but the loot does NOT teleport into a larder. It
            // individuates into a discrete carried loaf owned by the keeper; that minting
            // is pinned clock-free in ItemTests (the harness clock is currently too slow
            // to accrue a whole loaf in a few ticks — pending the time-model rewrite).
            Assert.True(h.Ctx.Stock.Get(store, Good.Provisions) < stock0, "stolen goods didn't leave the shelf");
            Assert.Equal(0, h.Ctx.Larder.Get(home), 9);                           // no larder teleport
            Assert.Equal(0.0, h.Ctx.Coin.Get(thief), 9);                          // paid nothing
            Assert.Equal(keeper0, h.Ctx.Coin.Get(keeper), 9);                     // keeper got no coin
            Assert.Equal(0, h.Ctx.Ledger.Current.SalesRevenue, 9);               // not a sale
        }

        [Fact]
        public void Steal_SelfGatesToProvisionsHolders()
        {
            var h = new SimHarness();
            int general = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore, X = 5, Z = 5 });
            int smith = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.WeaponSmith, X = 5, Z = 5 });
            h.Ctx.Stock.Set(general, Good.Provisions, 20);
            h.Ctx.Stock.Set(smith, Good.Wares, 20);   // wares, not food

            var thief = h.SpawnEntity("Thief");
            h.Ctx.Position.Set(thief, 5, 0, 5, 0);
            h.Ctx.Residency.Set(thief, new ResidencyData { BuildingIndex = 999, Role = ResidentRole.Resident });
            h.Ctx.PlaceMemory.Learn(thief, general);
            h.Ctx.PlaceMemory.Learn(thief, smith);
            h.SeedClock(hour: 12, timeScale: 600f);
            h.Step(1);

            var ads = ActionDiscovery.GatherAds(h.Ctx, thief);
            // Stealable food at the general store; nothing edible at the smith.
            Assert.Contains(ads, a => a.Verb == ActivityKind.Steal && a.Building == general);
            Assert.DoesNotContain(ads, a => a.Verb == ActivityKind.Steal && a.Building == smith);

            // Empty the store's provisions → the Steal ad vanishes (nothing to take).
            h.Ctx.Stock.Set(general, Good.Provisions, 0);
            ads = ActionDiscovery.GatherAds(h.Ctx, thief);
            Assert.DoesNotContain(ads, a => a.Verb == ActivityKind.Steal);
        }

        // --- S2: the conscience tag biases the choice (the honest won't steal) ---

        /// Within-subject A/B: the SAME broke, goods-hungry agent at a stocked store,
        /// varying ONLY its theft conscience. With no qualm it steals; with a strong
        /// qualm it picks something else. Isolates the tag (Buy/Beg are identical in
        /// both runs), so it pins the mechanism, not magnitudes.
        static ActivityKind DecideWithStealCharge(double stealCharge)
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int home = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1, X = 5, Z = 5 });
            int store = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore, X = 5, Z = 5 });
            h.Ctx.Stock.Set(store, Good.Provisions, 50);

            var keeper = h.SpawnEntity("Keeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = store, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Needs.Set(keeper, new NeedsData());

            var agent = h.SpawnEntity("Agent");
            h.Ctx.Position.Set(agent, 5, 0, 5, 0);          // at the store — no travel
            h.Ctx.Residency.Set(agent, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            h.Ctx.PlaceMemory.Learn(agent, store);
            h.Ctx.PlaceMemory.Learn(agent, home);
            h.Ctx.Coin.Set(agent, 0.0);                     // broke → can't really Buy
            var needs = new NeedsData();
            needs.V[NeedAxis.GoodsDef] = 1.4;               // larder empty, wants food
            needs.V[NeedAxis.Hunger] = 0.0;                 // not hungry → EatHome unattractive
            h.Ctx.Needs.Set(agent, needs);
            var c = new ConscienceData();
            c.Charge[(int)ActivityKind.Beg] = 0.95;         // proud → won't beg, so it's Steal vs the rest
            c.Charge[(int)ActivityKind.Steal] = stealCharge;
            h.Ctx.Conscience.Set(agent, c);
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(3);                                       // decide → commit
            return h.Ctx.Behavior.TryGet(agent, out var b) ? b.Activity : ActivityKind.None;
        }

        [Fact]
        public void Conscience_SuppressesStealing_ForTheHonest()
        {
            Assert.Equal(ActivityKind.Steal, DecideWithStealCharge(0.0));        // no qualm → steals
            Assert.NotEqual(ActivityKind.Steal, DecideWithStealCharge(0.95));    // strong qualm → won't
        }
    }
}
