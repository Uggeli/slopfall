using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.MemoryTests
{
    public class DriveGraphTargetsTests
    {
        [Fact]
        public void HardCullTargets_IsSocialOnly_NotGoodsOrCoin()
        {
            Assert.Contains(NeedAxis.SocialDef, DriveGraph.HardCullTargets);
            Assert.DoesNotContain(NeedAxis.GoodsDef, DriveGraph.HardCullTargets);   // instrumental to hunger
            Assert.DoesNotContain(NeedAxis.CoinDef,  DriveGraph.HardCullTargets);   // instrumental to hunger
        }

        [Fact]
        public void HardCullSources_StillIncludeTheDeficiencyPoles()
        {
            Assert.Contains(NeedAxis.Hunger,    DriveGraph.HardCullSources);
            Assert.Contains(NeedAxis.EnergyDef, DriveGraph.HardCullSources);
            Assert.Contains(NeedAxis.Fear,      DriveGraph.HardCullSources);
        }
    }
}
