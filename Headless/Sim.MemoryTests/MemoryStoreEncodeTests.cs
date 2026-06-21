using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreEncodeTests
    {
        static MemoryRecord Rec(long key, byte strength, long tick = 100, MemoryFlags flags = MemoryFlags.None)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Empty, strength, tick, tick, flags);

        [Fact]
        public void NewStore_IsEmpty_WithCapacity()
        {
            var s = new MemoryStore(4);
            Assert.Equal(0, s.Count);
            Assert.Equal(4, s.Capacity);
            Assert.False(s.TryGet(new MemoryKey(1), out _));
        }

        [Fact]
        public void Encode_KeepsRecordsKeySorted()
        {
            var s = new MemoryStore(8);
            Assert.True(s.Encode(Rec(30, 10)));
            Assert.True(s.Encode(Rec(10, 10)));
            Assert.True(s.Encode(Rec(20, 10)));

            Assert.Equal(3, s.Count);
            Assert.Equal(new long[] { 10, 20, 30 },
                Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void Encode_ExistingKey_ReplacesInPlace_NoGrowth()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 50));
            Assert.True(s.Encode(Rec(10, 200)));   // same key, absolute insert
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)200, r.Strength);
        }

        [Fact]
        public void TryGet_FindsPresent_MissesAbsent()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 1));
            s.Encode(Rec(20, 2));
            s.Encode(Rec(30, 3));

            Assert.True(s.TryGet(new MemoryKey(20), out var mid));
            Assert.Equal((byte)2, mid.Strength);
            Assert.False(s.TryGet(new MemoryKey(5), out _));
            Assert.False(s.TryGet(new MemoryKey(25), out _));
            Assert.False(s.TryGet(new MemoryKey(99), out _));
        }

        [Fact]
        public void Encode_IntoFullStore_NewKey_ReturnsFalse_ForNow()
        {
            var s = new MemoryStore(2);
            Assert.True(s.Encode(Rec(10, 10)));
            Assert.True(s.Encode(Rec(20, 10)));
            Assert.False(s.Encode(Rec(30, 10)));   // full; eviction lands in Task 4
            Assert.Equal(2, s.Count);
        }
    }
}
