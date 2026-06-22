using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddPreemptionTests
    {
        const double Hyst = 0.15;

        [Fact]
        public void Committed_UrgentWinner_Switches()      // a clearly better option interrupts
            => Assert.True(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 2.0, Hyst));

        [Fact]
        public void Committed_MarginalWinner_DoesNotSwitch()   // anti-thrash
            => Assert.False(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 1.10, Hyst));

        [Fact]
        public void Committed_WinnerBelowCurrent_DoesNotSwitch()
            => Assert.False(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 0.8, Hyst));

        [Fact]
        public void NotCommitted_AlwaysPicksFreely()
            => Assert.True(OddSystem.ShouldSwitchCommitment(false, currentScore: 5.0, winnerScore: 0.1, Hyst));

        [Fact]
        public void Committed_ExactlyAtThreshold_DoesNotSwitch()
            => Assert.False(OddSystem.ShouldSwitchCommitment(true, currentScore: 1.0, winnerScore: 1.15, Hyst));
    }
}
