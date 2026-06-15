using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// G5: institutions are service-businesses. A Visit to a temple/guild/bank is
    /// paid patronage (offering/dues/fee → keeper), stockless — no shelf to draw.
    /// A library/palace stays a free public good. Hand-set a Doing Visit so the
    /// visitor keeps patronising while EconomySystem runs.
    public class ServiceTests
    {
        static EntityId Visitor(SimHarness h, int building, double coin)
        {
            var v = h.SpawnEntity("Visitor");
            h.Ctx.Coin.Set(v, coin);
            h.Ctx.Needs.Set(v, new NeedsData());
            h.Ctx.Behavior.Set(v, new BehaviorData
            {
                Activity = ActivityKind.Visit, Phase = ActivityPhase.Doing,
                TargetBuilding = building, RemainingGameMinutes = 100000,
            });
            return v;
        }

        [Fact]
        public void TempleVisit_PaysTheKeeper_AsService_NoStock()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int temple = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Temple });
            var priest = h.SpawnEntity("Priest");
            h.Ctx.Residency.Set(priest, new ResidencyData { BuildingIndex = temple, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(priest, 0.5);
            var visitor = Visitor(h, temple, 1.0);
            h.SeedClock(hour: 12, timeScale: 600f);

            double priest0 = h.Ctx.Coin.Get(priest);
            h.Step(8);

            Assert.True(h.Ctx.Coin.Get(priest) > priest0, "temple earned no offerings");
            Assert.True(h.Ctx.Coin.Get(visitor) < 1.0, "visitor paid nothing");
            Assert.True(h.Ctx.Ledger.Current.ServiceRevenue > 0, "service not tallied");
            // It's a service, not a goods sale: no goods revenue, no stock anywhere.
            Assert.Equal(0, h.Ctx.Ledger.Current.SalesRevenue, 9);
        }

        [Fact]
        public void LibraryVisit_IsFree()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int library = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Library });
            var keeper = h.SpawnEntity("Librarian");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = library, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            var visitor = Visitor(h, library, 1.0);
            h.SeedClock(hour: 12, timeScale: 600f);

            double keeper0 = h.Ctx.Coin.Get(keeper);
            h.Step(8);

            // A library is a free public good — no fee, no service revenue.
            Assert.Equal(keeper0, h.Ctx.Coin.Get(keeper), 9);
            Assert.Equal(0, h.Ctx.Ledger.Current.ServiceRevenue, 9);
        }

        [Fact]
        public void BrokeVisitor_StillWelcome_PaysNothing()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int temple = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Temple });
            var priest = h.SpawnEntity("Priest");
            h.Ctx.Residency.Set(priest, new ResidencyData { BuildingIndex = temple, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(priest, 0.5);
            var pauper = Visitor(h, temple, 0.0);     // empty purse
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(8);

            // The poor visit free (service is ungated): no fee extracted, none owed.
            Assert.Equal(0, h.Ctx.Ledger.Current.ServiceRevenue, 9);
            Assert.Equal(0.5, h.Ctx.Coin.Get(priest), 9);
        }
    }
}
