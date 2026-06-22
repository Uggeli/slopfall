using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    // Sleep gating: a sleeping agent suppresses percept-driven re-evaluation.
    // SleepShouldWake encodes the ONLY triggers that wake a sleeper — hit, dawn/dusk,
    // duration expiry, staggered cap — and explicitly has NO preemptTick parameter,
    // ensuring percept cadence can never wake a sleeper.
    public class OddSleepGatingTests
    {
        [Fact]
        public void Hit_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(wasHit: true, reDecideAll: false,
                                                     durationExpired: false, capReached: false));

        [Fact]
        public void Dawn_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(false, reDecideAll: true, false, false));

        [Fact]
        public void DurationExpired_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(false, false, durationExpired: true, false));

        [Fact]
        public void CapReached_WakesSleeper()
            => Assert.True(OddSystem.SleepShouldWake(false, false, false, true));

        [Fact]
        public void NothingSalient_StaysAsleep()   // percepts suppressed: no cadence wake here
            => Assert.False(OddSystem.SleepShouldWake(false, false, false, false));
    }
}
