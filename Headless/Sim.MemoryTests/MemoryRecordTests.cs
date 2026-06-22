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

        static readonly AtomTypeId A = new AtomTypeId(1);
        static readonly AtomTypeId B = new AtomTypeId(2);
        static AtomBag Bag1 => AtomBag.Create(new[] { new Atom(A, Fixed.One) });

        static MemoryRecord Rec(byte strength, MemoryFlags flags, CategoryId cat)
            => new MemoryRecord(new MemoryKey(10), cat, Bag1, strength, 100, 100, flags);

        [Fact]
        public void Record_ExposesFlagsAndNovelty()
        {
            // Record-level flag queries are "any atom has it" (the broadcast ctor sets every atom).
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
        public void PerAtomMeta_IndependentStrengthFlags_AndDerivedEvictionStrength()
        {
            var bag = AtomBag.Create(new[] { new Atom(A, Fixed.One), new Atom(B, Fixed.One) });
            var meta = new[] { new AtomMeta(40, MemoryFlags.None), new AtomMeta(210, MemoryFlags.Surprise) };
            var r = new MemoryRecord(new MemoryKey(10), CategoryId.None, bag, meta, 100, 100);

            Assert.Equal((byte)40, r.Meta[0].Strength);
            Assert.False(r.Meta[0].IsInnate);
            Assert.True(r.Meta[1].IsSurprise);
            Assert.Equal(210, r.EvictionStrength);   // max of atom strengths
            Assert.False(r.AnyInnate);

            // An INNATE atom makes the whole record evict-immune (EvictionStrength saturates).
            var meta2 = new[] { new AtomMeta(40, MemoryFlags.Innate), new AtomMeta(10, MemoryFlags.None) };
            var r2 = new MemoryRecord(new MemoryKey(11), CategoryId.None, bag, meta2, 100, 100);
            Assert.True(r2.AnyInnate);
            Assert.Equal(255, r2.EvictionStrength);
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
