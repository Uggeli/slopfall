using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Scenario tests for the CAN-side resolver: given an agent and a thing, what
    /// can it do there? Synthetic (no game data) and deterministic — exactly the
    /// per-thing "what can I do to this" the design called for.
    public class ActionDiscoveryTests
    {
        static SimulationContext NewCtx() =>
            new SimulationContext(new EventBus(), new SimulationTime(0.1), new SimRandom(1), new InputBus());

        static bool Has(List<Ad> ads, ActivityKind v)
        {
            foreach (var a in ads) if (a.Verb == v) return true;
            return false;
        }

        static List<Ad> DiscoverOne(SimulationContext ctx, EntityId agent, int building)
        {
            var ads = new List<Ad>();
            ActionDiscovery.Discover(ctx, agent, building, ads);
            return ads;
        }

        [Fact]
        public void Tavern_OffersFoodAndCompany_NotHomeOrWork()
        {
            var ctx = NewCtx();
            int tavern = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 10, Z = 0 });
            var agent = new EntityId(1);
            ctx.Residency.Set(agent, new ResidencyData { BuildingIndex = 999, Role = ResidentRole.Resident });

            var ads = DiscoverOne(ctx, agent, tavern);

            Assert.True(Has(ads, ActivityKind.EatTavern));
            Assert.True(Has(ads, ActivityKind.Socialize));
            Assert.False(Has(ads, ActivityKind.Sleep));     // not their home
            Assert.False(Has(ads, ActivityKind.Work));
        }

        [Fact]
        public void OwnHome_OffersSleepAndEat_ResidentGetsNoWork()
        {
            var ctx = NewCtx();
            int home = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1, X = 0, Z = 0 });
            var agent = new EntityId(1);
            ctx.Residency.Set(agent, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });

            var ads = DiscoverOne(ctx, agent, home);

            Assert.True(Has(ads, ActivityKind.Sleep));
            Assert.True(Has(ads, ActivityKind.EatHome));
            Assert.False(Has(ads, ActivityKind.Work));      // residents don't work
        }

        [Fact]
        public void KeeperWorkplace_OffersWorkAndHome()
        {
            var ctx = NewCtx();
            int shop = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.GeneralStore, X = 5, Z = 0 });
            var keeper = new EntityId(2);
            ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = shop, Role = ResidentRole.Keeper });

            var ads = DiscoverOne(ctx, keeper, shop);

            Assert.True(Has(ads, ActivityKind.Work));       // keeper at own workplace
            Assert.True(Has(ads, ActivityKind.Sleep));      // and lives above the shop
            Assert.False(Has(ads, ActivityKind.Visit));     // you don't VISIT your own shop
        }

        [Fact]
        public void GatherAds_IncludesInnateAndKnownPlaces()
        {
            var ctx = NewCtx();
            int tavern = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 10, Z = 0 });
            int home = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1, X = 0, Z = 0 });
            int temple = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Temple, X = 20, Z = 0 });
            ctx.Stock.Set(tavern, Good.Provisions, 10);    // stocked, so the meal is on offer (G3)
            var agent = new EntityId(1);
            ctx.Residency.Set(agent, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            ctx.Position.Set(agent, 0f, 0f, 0f, 0f);
            ctx.WorldClock.Set(new WorldClockData { Year = 405, Hour = 12 });   // noon: tavern/visit open
            ctx.PlaceMemory.Learn(agent, tavern);
            ctx.PlaceMemory.Learn(agent, home);
            ctx.PlaceMemory.Learn(agent, temple);

            var ads = ActionDiscovery.GatherAds(ctx, agent);

            Assert.True(Has(ads, ActivityKind.Idle));       // innate floor
            Assert.True(Has(ads, ActivityKind.Wander));
            Assert.True(Has(ads, ActivityKind.EatTavern));  // from the known tavern
            Assert.True(Has(ads, ActivityKind.Sleep));      // from own home
            Assert.True(Has(ads, ActivityKind.Visit));      // from the temple
            foreach (var a in ads) Assert.NotNull(a.Spec);  // every ad resolved its spec
        }

        [Fact]
        public void Decomposition_ActivitiesMapToActions()
        {
            Assert.Equal(new[] { ActionKind.Sustain }, ActionCatalog.DoingSteps(ActivityKind.Sleep));
            Assert.Contains(ActionKind.Transfer, ActionCatalog.DoingSteps(ActivityKind.EatTavern));
            Assert.Contains(ActionKind.Sustain, ActionCatalog.DoingSteps(ActivityKind.EatTavern));
            Assert.Contains(ActionKind.Ask, ActionCatalog.DoingSteps(ActivityKind.SeekHelp));
            Assert.Empty(ActionCatalog.DoingSteps(ActivityKind.Wander));   // wander is movement only
        }
    }
}
