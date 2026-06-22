using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class MovementQueuedTests
    {
        [Fact]
        public void MovingAndQueued_BothMove()
        {
            Assert.True(MovementSystem.PhaseMoves(ActivityPhase.Moving));
            Assert.True(MovementSystem.PhaseMoves(ActivityPhase.Queued));
        }

        [Fact]
        public void Doing_DoesNotMove()
            => Assert.False(MovementSystem.PhaseMoves(ActivityPhase.Doing));

        [Fact]
        public void OnlyMoving_PublishesArrival()
        {
            Assert.True(MovementSystem.PhaseArrives(ActivityPhase.Moving));
            Assert.False(MovementSystem.PhaseArrives(ActivityPhase.Queued));   // waiter doesn't re-arrive
            Assert.False(MovementSystem.PhaseArrives(ActivityPhase.Doing));
        }
    }
}
