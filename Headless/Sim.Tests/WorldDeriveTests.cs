using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class WorldDeriveTests
    {
        [Fact]
        public void WeatherChange_EmitsTransitionEvent()
        {
            var h = new SimHarness();
            var changes = h.Collect<WeatherChangedSimEvent>();

            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
            h.Step();                       // first observation seeds, no event
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Rain, IsRaining = true });
            h.Step(2);

            Assert.Single(changes);
            Assert.Equal(WeatherKind.Sunny, changes[0].From);
            Assert.Equal(WeatherKind.Rain, changes[0].To);
        }

        [Fact]
        public void Sunlight_IsZeroAtNight()
        {
            var h = new SimHarness();
            h.SeedClock(hour: 23, minute: 0);
            h.Step();

            var light = h.Ctx.Lighting.Current;
            Assert.True(light.IsNight);
            Assert.Equal(0f, light.SunIntensity);
        }

        [Fact]
        public void Sunlight_PeaksNearMidday_AttenuatedByRain()
        {
            var h = new SimHarness();
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
            h.SeedClock(hour: 12, minute: 0);
            h.Step();

            var clear = h.Ctx.Lighting.Current;
            Assert.False(clear.IsNight);
            Assert.True(clear.DaylightScale > 0.95f);
            Assert.Equal(1f, clear.WeatherScale);

            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Rain, IsRaining = true });
            h.Step();

            var rainy = h.Ctx.Lighting.Current;
            Assert.Equal(0.45f, rainy.WeatherScale, 3);
            Assert.True(rainy.SunIntensity < clear.SunIntensity);
        }
    }

    public class CoreTests
    {
        [Fact]
        public void EntityId_NoneAndEquality()
        {
            Assert.True(EntityId.None.IsNone);
            Assert.True(new EntityId(0) == EntityId.None);
            Assert.True(new EntityId(3) == new EntityId(3));
            Assert.True(new EntityId(3) != new EntityId(4));
        }

        [Fact]
        public void IdentityRegistry_AllocateIsSequentialAndNeverNone()
        {
            var reg = new IdentityRegistry();
            var a = reg.Allocate();
            var b = reg.Allocate();
            Assert.False(a.IsNone);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void PlayerId_ClearOnlyClearsMatchingId()
        {
            var reg = new IdentityRegistry();
            var p1 = reg.Allocate();
            var p2 = reg.Allocate();

            reg.SetPlayer(p1);
            reg.SetPlayer(p2);              // respawn registered before despawn ran
            reg.ClearPlayer(p1);            // stale despawn must not clobber p2
            Assert.Equal(p2, reg.PlayerId);

            reg.ClearPlayer(p2);
            Assert.True(reg.PlayerId.IsNone);
        }

        [Fact]
        public void SimRandom_SameSeed_SameSequence()
        {
            var a = new SimRandom(42);
            var b = new SimRandom(42);
            for (int i = 0; i < 100; i++)
                Assert.Equal(a.NextInt(1000), b.NextInt(1000));
        }

        [Fact]
        public void SimThread_TicksAndStopsCleanly()
        {
            var events = new EventBus();
            var time = new SimulationTime(0.005);    // 200 Hz so the test is quick
            var ctx = new SimulationContext(events, time, new SimRandom(1), new InputBus());
            var loop = new TickLoop(ctx);
            var publisher = new SnapshotPublisher();
            var thread = new SimThread(loop, ctx, publisher);

            thread.Start();
            System.Threading.Thread.Sleep(250);
            thread.Stop();

            Assert.Null(thread.LastException);
            Assert.True(ctx.Time.Tick > 10);
            Assert.NotNull(publisher.Latest);
            Assert.Equal(ctx.Time.Tick, publisher.Latest.Tick);
        }
    }
}
