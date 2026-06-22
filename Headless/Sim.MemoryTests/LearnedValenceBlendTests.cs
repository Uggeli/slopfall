using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class LearnedValenceBlendTests
    {
        [Fact]
        public void Confidence0_IsAllOld() => Assert.Equal(-0.7, SubjectiveSystem.BlendValence(-0.7, 0.9, 0.0), 6);

        [Fact]
        public void Confidence1_IsAllNew() => Assert.Equal(0.9, SubjectiveSystem.BlendValence(-0.7, 0.9, 1.0), 6);

        [Fact]
        public void ConfidenceHalf_IsMidpoint() => Assert.Equal(0.1, SubjectiveSystem.BlendValence(-0.7, 0.9, 0.5), 6);
    }
}
