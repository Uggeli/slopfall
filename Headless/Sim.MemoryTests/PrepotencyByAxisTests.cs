using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class PrepotencyByAxisTests
    {
        [Fact]
        public void SocialAd_IsHardCullTarget()
            => Assert.True(OddSystem.IsHardCullTarget(NeedAxis.SocialDef));

        [Fact]
        public void GoodsAd_IsNotHardCullTarget()    // Buy serves goods — must NEVER be culled (food chain)
            => Assert.False(OddSystem.IsHardCullTarget(NeedAxis.GoodsDef));

        [Fact]
        public void NonGatedAd_IsNotHardCullTarget()
            => Assert.False(OddSystem.IsHardCullTarget(-1));
    }
}
