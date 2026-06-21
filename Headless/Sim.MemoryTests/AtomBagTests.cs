using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomBagTests
    {
        static Atom A(int type, double v) => new Atom(new AtomTypeId(type), Fixed.FromDouble(v));

        [Fact]
        public void Empty_HasZeroCount()
        {
            Assert.Equal(0, AtomBag.Empty.Count);
            Assert.False(AtomBag.Empty.TryGet(new AtomTypeId(1), out _));
        }

        [Fact]
        public void Create_SortsByType()
        {
            var bag = AtomBag.Create(new[] { A(3, 0.3), A(1, 0.1), A(2, 0.2) });
            Assert.Equal(new[] { 1, 2, 3 }, bag.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Create_EmptyInput_ReturnsEmptySingleton()
        {
            Assert.Same(AtomBag.Empty, AtomBag.Create(Array.Empty<Atom>()));
        }

        [Fact]
        public void Create_DuplicateType_Throws()
        {
            Assert.Throws<ArgumentException>(() => AtomBag.Create(new[] { A(1, 0.1), A(1, 0.2) }));
        }

        [Fact]
        public void TryGet_FindsPresent_AndMissesAbsent()
        {
            var bag = AtomBag.Create(new[] { A(1, 0.1), A(5, 0.5), A(9, 0.9) });

            Assert.True(bag.TryGet(new AtomTypeId(5), out var mid));
            Assert.Equal(Fixed.FromDouble(0.5), mid);
            Assert.True(bag.TryGet(new AtomTypeId(1), out _));   // first
            Assert.True(bag.TryGet(new AtomTypeId(9), out _));   // last

            Assert.False(bag.TryGet(new AtomTypeId(0), out _));  // below min
            Assert.False(bag.TryGet(new AtomTypeId(3), out _));  // between
            Assert.False(bag.TryGet(new AtomTypeId(99), out _)); // above max
        }

        [Fact]
        public void Contains_MatchesTryGet()
        {
            var bag = AtomBag.Create(new[] { A(2, 0.2) });
            Assert.True(bag.Contains(new AtomTypeId(2)));
            Assert.False(bag.Contains(new AtomTypeId(1)));
        }
    }
}
