using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryRecordTests
    {
        [Fact]
        public void MemoryKey_OrdersAndEqualsByValue()
        {
            Assert.True(new MemoryKey(1).CompareTo(new MemoryKey(2)) < 0);
            Assert.True(new MemoryKey(5) == new MemoryKey(5));
            Assert.True(new MemoryKey(5) != new MemoryKey(6));
            Assert.Equal(new MemoryKey(5).GetHashCode(), new MemoryKey(5).GetHashCode());

            var list = new List<MemoryKey> { new MemoryKey(3), new MemoryKey(1), new MemoryKey(2) };
            list.Sort();
            Assert.Equal(new long[] { 1, 2, 3 }, list.ConvertAll(k => k.Value));
        }

        [Fact]
        public void CategoryId_None_IsZero_AndNovelty()
        {
            Assert.True(CategoryId.None.IsNone);
            Assert.Equal(0, CategoryId.None.Value);
            Assert.False(new CategoryId(7).IsNone);
            Assert.True(new CategoryId(7) == new CategoryId(7));
        }

        static MemoryRecord Rec(byte strength, MemoryFlags flags, CategoryId cat)
            => new MemoryRecord(new MemoryKey(10), cat, AtomBag.Empty, strength, 100, 100, flags);

        [Fact]
        public void Record_ExposesFlagsAndNovelty()
        {
            var innate = Rec(200, MemoryFlags.Innate, new CategoryId(3));
            Assert.True(innate.IsInnate);
            Assert.False(innate.IsSurprise);
            Assert.False(innate.IsNovel);

            var novelSurprise = Rec(50, MemoryFlags.Surprise, CategoryId.None);
            Assert.True(novelSurprise.IsSurprise);
            Assert.False(novelSurprise.IsInnate);
            Assert.True(novelSurprise.IsNovel);    // CategoryRef.None => stored verbatim
        }

        [Fact]
        public void WithStrength_ReplacesStrengthAndLastRefresh_KeepsRest()
        {
            var bag = AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) });
            var r = new MemoryRecord(new MemoryKey(10), new CategoryId(3), bag, 100, 100, 100, MemoryFlags.Surprise);
            var r2 = r.WithStrength(140, 250);

            Assert.Equal((byte)140, r2.Strength);
            Assert.Equal(250, r2.LastRefresh);
            Assert.Equal(100, r2.WrittenAt);          // unchanged
            Assert.Equal(new MemoryKey(10), r2.Key);  // unchanged
            Assert.Equal(new CategoryId(3), r2.CategoryRef);
            Assert.Same(bag, r2.DeltaBag);
            Assert.True(r2.IsSurprise);
        }
    }
}
