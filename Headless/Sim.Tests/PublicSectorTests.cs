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
            var rich = h.SpawnEntity("Rich"); h.Ctx.Coin.Set(rich, 10.0); h.Ctx.Needs.Set(rich, new NeedsData());
            var poor = h.SpawnEntity("Poor"); h.Ctx.Coin.Set(poor, 0.2); h.Ctx.Needs.Set(poor, new NeedsData());
            h.SeedClock(year: 405, month: 0, day: 1, hour: 12, timeScale: 600f);
            h.Step(2);

            double supplyBefore = MoneySupply(h);
            h.Ctx.Events.Emit(new NewMonthSimEvent { Month = 1, Year = 405 });   // tax man arrives
            h.Step(2);

            Assert.True(h.Ctx.Coin.Get(rich) < 10.0, "the wealthy weren't taxed");
            Assert.Equal(0.2, h.Ctx.Coin.Get(poor), 6);                 // below exemption → untouched
            Assert.True(h.Ctx.Treasury.Get(OwnerId.Town) > 0, "treasury collected no tax");
            Assert.True(h.Ctx.Ledger.Current.Taxes > 0, "tax not tallied");
            // A transfer, not a sink: the money supply is unchanged.
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
            h.Step(5);

            // The guard is paid out of the treasury; the crown reimburses the
            // treasury for that payroll each tick (G6), so the balance holds while
            // coin flows out to the guard.
            Assert.True(h.Ctx.Coin.Get(guard) > guard0, "guard wasn't paid");
            Assert.True(h.Ctx.Ledger.Current.GuardPay > 0, "guard pay not tallied");
            Assert.True(h.Ctx.Ledger.Current.CrownSubsidy > 0, "crown didn't reimburse the payroll");
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
