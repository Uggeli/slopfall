using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddQueueLeaveTests
    {
        [Fact]
        public void SameShopBuyWins_StaysInQueue()
            => Assert.True(OddSystem.ShouldStayInQueue(true, 7, ActivityKind.Buy, 7));

        [Fact]
        public void DifferentBuildingWins_LeavesQueue()
            => Assert.False(OddSystem.ShouldStayInQueue(true, 7, ActivityKind.Buy, 9));

        [Fact]
        public void NonBuyWins_LeavesQueue()
            => Assert.False(OddSystem.ShouldStayInQueue(true, 7, ActivityKind.Sleep, 7));

        [Fact]
        public void NotInQueue_NeverStays()
            => Assert.False(OddSystem.ShouldStayInQueue(false, 7, ActivityKind.Buy, 7));
    }
}
