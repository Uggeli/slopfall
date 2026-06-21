using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomTypeIdTests
    {
        [Fact]
        public void None_IsZero()
        {
            Assert.True(AtomTypeId.None.IsNone);
            Assert.Equal(0, AtomTypeId.None.Value);
            Assert.False(new AtomTypeId(1).IsNone);
        }

        [Fact]
        public void Equality_ByValue()
        {
            Assert.True(new AtomTypeId(5) == new AtomTypeId(5));
            Assert.True(new AtomTypeId(5) != new AtomTypeId(6));
            Assert.Equal(new AtomTypeId(5).GetHashCode(), new AtomTypeId(5).GetHashCode());
        }

        [Fact]
        public void CompareTo_OrdersByValue()
        {
            Assert.True(new AtomTypeId(1).CompareTo(new AtomTypeId(2)) < 0);
            Assert.True(new AtomTypeId(2).CompareTo(new AtomTypeId(2)) == 0);
            Assert.True(new AtomTypeId(3).CompareTo(new AtomTypeId(2)) > 0);
        }

        [Fact]
        public void Sorts_Ascending_ByValue()
        {
            var list = new List<AtomTypeId> { new AtomTypeId(3), new AtomTypeId(1), new AtomTypeId(2) };
            list.Sort();
            Assert.Equal(new[] { 1, 2, 3 }, list.ConvertAll(a => a.Value));
        }
    }
}
