using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// E3: the public sector. A monthly progressive tax drains wealth above an
    /// exemption into the Town treasury (recirculation against concentration);
    /// the treasury pays guards a salary (putting it back into circulation). Both
    /// are conserved transfers — the treasury is part of the money supply.
    public class PublicSectorTests
    {
        static double MoneySupply(SimHarness h)
        {
            double s = 0;
            foreach (var kv in h.Ctx.Coin.All) s += kv.Value;
            return s + h.Ctx.Treasury.Total;
        }

        [Fact]
        public void MonthlyTax_DrainsWealthAboveExemption_IntoTreasury()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            // Tax is collected per settlement, so the taxed purses must belong to one.
            var town = h.Ctx.Settlements.Add("Testton", "Test", SettlementKind.City);
            var rich = h.SpawnEntity("Rich"); h.Ctx.Coin.Set(rich, 10.0); h.Ctx.Needs.Set(rich, new NeedsData()); town.Residents.Add(rich);
            var poor = h.SpawnEntity("Poor"); h.Ctx.Coin.Set(poor, 0.2); h.Ctx.Needs.Set(poor, new NeedsData()); town.Residents.Add(poor);
            h.SeedClock(year: 405, month: 0, day: 1, hour: 12, timeScale: 600f);
            h.Step(2);

            double supplyBefore = MoneySupply(h);
            h.Ctx.Events.Emit(new NewMonthSimEvent { Month = 1, Year = 405 });   // tax man arrives
            h.Step(2);

            Assert.True(h.Ctx.Coin.Get(rich) < 10.0, "the wealthy weren't taxed");
            Assert.True(h.Ctx.Coin.Get(poor) >= 0.2, "the poor were taxed");   // below exemption → not taxed (may gain a little civic relief)
            Assert.True(h.Ctx.Treasury.Get(town.Treasury) > 0, "the settlement treasury collected no tax");
            Assert.True(h.Ctx.Ledger.Current.Taxes > 0, "tax not tallied");
            // Tax and the civic dividend are both transfers: the money supply is unchanged.
            Assert.Equal(supplyBefore, MoneySupply(h), 6);
        }

        [Fact]
        public void Guard_IsPaidASalary_FromTheTreasury()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var guard = h.SpawnEntity("Guard");
            h.Ctx.Coin.Set(guard, 0.1);
            h.Ctx.Needs.Set(guard, new NeedsData());
            h.Ctx.Employment.Set(guard, new EmploymentData { Employer = EntityId.None, PublicOwner = OwnerId.Town });
            h.Ctx.Treasury.Set(OwnerId.Town, 5.0);
            h.Ctx.Behavior.Set(guard, new BehaviorData
            {
                Activity = ActivityKind.Idle, Phase = ActivityPhase.Doing, RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 12, timeScale: 600f);

            double guard0 = h.Ctx.Coin.Get(guard);
            double treasury0 = h.Ctx.Treasury.Get(OwnerId.Town);
            h.Step(5);

            // F1: the guard is paid in full out of the treasury, which DEPLETES (the
            // crown is only a lender of last resort, so with a funded treasury it mints
            // nothing). Tax actually recirculates instead of the treasury staying flat.
            Assert.True(h.Ctx.Coin.Get(guard) > guard0, "guard wasn't paid");
            Assert.True(h.Ctx.Ledger.Current.GuardPay > 0, "guard pay not tallied");
            Assert.True(h.Ctx.Treasury.Get(OwnerId.Town) < treasury0, "treasury didn't fund the payroll");
            Assert.Equal(0, h.Ctx.Ledger.Current.CrownSubsidy, 9);   // funded treasury → no crown mint
        }

        [Fact]
        public void EmptyTreasury_CannotMakePayroll()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var guard = h.SpawnEntity("Guard");
            h.Ctx.Coin.Set(guard, 0.5);
            h.Ctx.Needs.Set(guard, new NeedsData());
            h.Ctx.Employment.Set(guard, new EmploymentData { Employer = EntityId.None, PublicOwner = OwnerId.Town });
            h.Ctx.Treasury.Set(OwnerId.Town, 0.0);   // broke treasury
            h.Ctx.Behavior.Set(guard, new BehaviorData
            {
                Activity = ActivityKind.Idle, Phase = ActivityPhase.Doing, RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(5);

            Assert.Equal(0, h.Ctx.Ledger.Current.GuardPay, 9);   // no coin, no salary
        }
    }
}
