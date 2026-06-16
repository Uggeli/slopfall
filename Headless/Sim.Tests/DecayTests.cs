using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// L1.1 — the deterministic decay primitive (docs/living_world_L1_entropy.md).
    /// A pure algorithm, so a thin unit layer is the right test tier.
    public class DecayTests
    {
        [Fact]
        public void MovesTowardBaseline_ByTheRetainedFraction()
        {
            // retained = (1 - 0.1)^1 = 0.9
            double v = Decay.TowardBaseline(1.0, 0.0, 0.1, 1.0);
            Assert.True(v < 1.0 && v > 0.0, "v=" + v);
            Assert.Equal(0.9, v, 10);
        }

        [Fact]
        public void BelowBaseline_RisesTowardIt_Symmetrically()
        {
            double v = Decay.TowardBaseline(-1.0, 0.0, 0.1, 1.0);
            Assert.True(v > -1.0 && v < 0.0, "v=" + v);
            Assert.Equal(-0.9, v, 10);
        }

        [Fact]
        public void NonNeutralBaseline_IsThePullPoint()
        {
            // value above baseline falls toward it; below rises toward it
            Assert.True(Decay.TowardBaseline(1.0, 0.5, 0.2, 1.0) < 1.0);
            Assert.True(Decay.TowardBaseline(0.0, 0.5, 0.2, 1.0) > 0.0);
        }

        [Fact]
        public void Composes_SplitEqualsWhole()
        {
            // Decaying 3 separate hours equals decaying 3 hours at once —
            // this is what lets DecayRelations run on any cadence.
            double whole = Decay.TowardBaseline(1.0, 0.0, 0.05, 3.0);
            double step = Decay.TowardBaseline(1.0, 0.0, 0.05, 1.0);
            step = Decay.TowardBaseline(step, 0.0, 0.05, 1.0);
            step = Decay.TowardBaseline(step, 0.0, 0.05, 1.0);
            Assert.Equal(whole, step, 10);
        }

        [Fact]
        public void ApproachesBaseline_OverManyHours()
        {
            Assert.True(Decay.TowardBaseline(1.0, 0.0, 0.1, 200.0) < 0.001);
        }

        [Fact]
        public void NoOp_WhenRateOrHoursNonPositive_AndSnaps_WhenRateAtLeastOne()
        {
            Assert.Equal(1.0, Decay.TowardBaseline(1.0, 0.0, 0.0, 5.0), 10);
            Assert.Equal(1.0, Decay.TowardBaseline(1.0, 0.0, 0.2, 0.0), 10);
            Assert.Equal(1.0, Decay.TowardBaseline(1.0, 0.0, -0.1, 5.0), 10);
            Assert.Equal(0.5, Decay.TowardBaseline(1.0, 0.5, 1.0, 1.0), 10); // snap
        }
    }
}
