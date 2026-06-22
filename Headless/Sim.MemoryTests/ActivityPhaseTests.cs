using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.MemoryTests
{
    public class ActivityPhaseTests
    {
        [Fact]
        public void Queued_IsAppended_WireStable()
        {
            Assert.Equal(0, (int)ActivityPhase.Moving);
            Assert.Equal(1, (int)ActivityPhase.Doing);
            Assert.Equal(2, (int)ActivityPhase.Queued);
        }
    }
}
