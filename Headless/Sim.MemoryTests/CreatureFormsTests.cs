using System.Linq;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class CreatureFormsTests
    {
        [Fact]
        public void Beast_CarriesFangedFastAndAGradedSize()
        {
            var bag = CreatureForms.Beast;
            // weapons present, full presence value
            Assert.Contains(bag, f => f.Type == AtomName.Fanged.ToId() && f.Value == Fixed.One);
            Assert.Contains(bag, f => f.Type == AtomName.Fast.ToId()   && f.Value == Fixed.One);
            // size graded in (0,1)
            var size = bag.Single(f => f.Type == AtomName.Size.ToId());
            Assert.True(size.Value.ToDouble() > 0.0 && size.Value.ToDouble() < 1.0);
        }
    }
}
