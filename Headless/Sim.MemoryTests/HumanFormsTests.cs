using System.Linq;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class HumanFormsTests
    {
        [Fact]
        public void Civilian_CarriesAHumanBodySize_SmallerThanBeast()
        {
            var size = HumanForms.Civilian.Single(f => f.Type == AtomName.Size.ToId());
            double s = size.Value.ToDouble();
            Assert.True(s > 0.0 && s < 0.5);   // a human body, smaller than the generic Beast (0.5)
        }
    }
}
