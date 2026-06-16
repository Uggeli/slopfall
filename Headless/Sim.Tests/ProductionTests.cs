using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Stage 5 — Production & Trade. Laborers work a real workplace (a farm) that
    /// makes food scaled by the hands working it; the farm pays those hands out of its
    /// till; and the settlement's tax recirculates back to residents as a civic
    /// dividend. These assert the new loop functions and conserves coin.
    public class ProductionTests
    {
        static double Supply(SimHarness h)
        {
            double s = 0;
            foreach (var kv in h.Ctx.Coin.All) s += kv.Value;
            return s + h.Ctx.Treasury.Total;
        }

        [Fact]
        public void Farm_ProducesFood_AndPaysItsHands()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int farm = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Farm });

            var keeper = h.SpawnEntity("FarmKeeper");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = farm, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 2.0);                 // the farm till the hands are paid from
            h.Ctx.Needs.Set(keeper, new NeedsData());
            h.Ctx.Behavior.Set(keeper, new BehaviorData
            {
                Activity = ActivityKind.Work, Phase = ActivityPhase.Doing,
                TargetBuilding = farm, RemainingGameMinutes = 100000,
            });

            var hand = h.SpawnEntity("Hand");
            h.Ctx.Coin.Set(hand, 0.0);
            h.Ctx.Needs.Set(hand, new NeedsData());
            h.Ctx.Employment.Set(hand, new EmploymentData { Employer = keeper });
            h.Ctx.Behavior.Set(hand, new BehaviorData
            {
                Activity = ActivityKind.Farm, Phase = ActivityPhase.Doing,
                TargetBuilding = farm, RemainingGameMinutes = 100000,
            });

            double food0 = h.Ctx.Stock.Get(farm, Good.Provisions);
            double hand0 = h.Ctx.Coin.Get(hand);
            h.SeedClock(hour: 12, timeScale: 600f);
            h.Step(5);

            // The working hand makes the farm produce food, and earns a share of the
            // farm's till — real work with real output and real income.
            Assert.True(h.Ctx.Stock.Get(farm, Good.Provisions) > food0, "farm produced no food from its hands");
            Assert.True(h.Ctx.Coin.Get(hand) > hand0, "the farmhand wasn't paid from the farm till");
        }

        [Fact]
        public void CivicDividend_RecirculatesTreasuryToResidents()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var town = h.Ctx.Settlements.Add("Townton", "Test", SettlementKind.City);

            var r = h.SpawnEntity("Resident");
            h.Ctx.Coin.Set(r, 0.1);
            h.Ctx.Needs.Set(r, new NeedsData());
            town.Residents.Add(r);
            h.Ctx.Treasury.Set(town.Treasury, 5.0);      // a treasury to recirculate

            h.SeedClock(hour: 12, timeScale: 600f);
            double supply0 = Supply(h);
            double treasury0 = h.Ctx.Treasury.Get(town.Treasury);
            double r0 = h.Ctx.Coin.Get(r);
            h.Step(5);

            // The treasury pays residents a civic dividend (poor relief) — tax
            // recirculates instead of hoarding. A pure transfer: supply is unchanged.
            Assert.True(h.Ctx.Treasury.Get(town.Treasury) < treasury0, "treasury didn't recirculate");
            Assert.True(h.Ctx.Coin.Get(r) > r0, "resident received no civic dividend");
            Assert.Equal(supply0, Supply(h), 6);
        }
    }
}
