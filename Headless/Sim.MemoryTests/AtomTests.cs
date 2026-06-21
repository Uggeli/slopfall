using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomTests
    {
        [Fact]
        public void StoresTypeAndValue()
        {
            var a = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.5));
            Assert.Equal(new AtomTypeId(7), a.Type);
            Assert.Equal(Fixed.FromDouble(0.5), a.Value);
        }

        [Fact]
        public void Equality_ByTypeAndValue()
        {
            var a = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.5));
            var same = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.5));
            var diffType = new Atom(new AtomTypeId(8), Fixed.FromDouble(0.5));
            var diffValue = new Atom(new AtomTypeId(7), Fixed.FromDouble(0.6));

            Assert.Equal(same, a);
            Assert.Equal(same.GetHashCode(), a.GetHashCode());
            Assert.NotEqual(diffType, a);
            Assert.NotEqual(diffValue, a);
        }
    }
}
