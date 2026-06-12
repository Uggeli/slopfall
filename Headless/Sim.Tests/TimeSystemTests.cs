using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Utility;
using Xunit;

namespace Sim.Tests
{
    public class TimeSystemTests
    {
        [Fact]
        public void Unseeded_TimeSystem_IsNoOp()
        {
            var h = new SimHarness();
            h.Step(10);
            Assert.Equal(0, h.Ctx.WorldClock.Current.Year);
        }

        [Fact]
        public void Seed_WritesClockRegistry_SameTick()
        {
            var h = new SimHarness();
            h.SeedClock(year: 405, month: 0, day: 3, hour: 13, minute: 30);
            h.Step();

            var clock = h.Ctx.WorldClock.Current;
            Assert.Equal(405, clock.Year);
            Assert.Equal(0, clock.Month);
            Assert.Equal(3, clock.Day);
            Assert.Equal(13, clock.Hour);
        }

        [Fact]
        public void Clock_Advances_ByTickIntervalTimesTimeScale()
        {
            // 1s ticks at scale 60 -> one game-minute per tick.
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            h.SeedClock(hour: 13, minute: 0, timeScale: 60f);
            h.Step();                       // seed lands, then Update adds 1 minute
            h.Step(9);

            var clock = h.Ctx.WorldClock.Current;
            Assert.Equal(13, clock.Hour);
            Assert.Equal(10, clock.Minute);
        }

        [Fact]
        public void CrossingDawnHour_EmitsDawnAndNewHour()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var dawns = h.Collect<DawnSimEvent>();
            var hours = h.Collect<NewHourSimEvent>();

            h.SeedClock(hour: 5, minute: 59, timeScale: 60f);
            h.Step(5);                      // 1 min/tick: crosses 06:00 within a few ticks

            Assert.Single(dawns);
            Assert.Single(hours);
            Assert.Equal(DaggerfallDateTime.DawnHour, hours[0].Hour);
        }

        [Fact]
        public void MidnightRollover_EmitsNewDay()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var days = h.Collect<NewDaySimEvent>();
            var midnights = h.Collect<MidnightSimEvent>();

            h.SeedClock(day: 3, hour: 23, minute: 59, timeScale: 60f);
            h.Step(5);

            Assert.Single(days);
            Assert.Single(midnights);
            Assert.Equal(4, days[0].Day);
        }

        [Fact]
        public void KnownLimitation_HourJump_SkipsTransitionEvents()
        {
            // TimeSystem compares only the post-jump hour against the marker
            // hours. A tick spanning multiple hours (huge timescale or long
            // tick) skips dawn/dusk/etc. entirely. This test pins the current
            // behavior — when TimeSystem is fixed to walk skipped hours, flip
            // these assertions.
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var dawns = h.Collect<DawnSimEvent>();

            h.SeedClock(hour: 5, minute: 0, timeScale: 7200f);   // 2 hours per tick
            h.Step(2);                                           // 05:00 -> 07:00 -> 09:00

            Assert.True(h.Ctx.WorldClock.Current.Hour > DaggerfallDateTime.DawnHour);
            Assert.Empty(dawns);            // dawn was skipped — documented bug
        }

        [Fact]
        public void TimeTicked_FiresEveryTick_AfterSeed()
        {
            var h = new SimHarness();
            var ticks = h.Collect<TimeTickedEvent>();
            h.SeedClock();
            h.Step(5);
            // One per seeded tick, observed with one tick of latency: an event
            // emitted in Update(N) reaches handlers during Drain(N+1).
            Assert.Equal(4, ticks.Count);
        }
    }
}
