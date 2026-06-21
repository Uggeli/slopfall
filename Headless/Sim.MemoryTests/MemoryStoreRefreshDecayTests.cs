using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreRefreshDecayTests
    {
        static MemoryRecord Rec(long key, byte strength, MemoryFlags flags = MemoryFlags.None, long tick = 100)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Empty, strength, tick, tick, flags);

        [Fact]
        public void Refresh_BumpsStrength_AndLastRefresh()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100));
            Assert.True(s.Refresh(new MemoryKey(10), 40, 500));
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)140, r.Strength);
            Assert.Equal(500, r.LastRefresh);
            Assert.Equal(100, r.WrittenAt);
        }

        [Fact]
        public void Refresh_ClampsAt255_AndMissingKeyReturnsFalse()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 250));
            s.Refresh(new MemoryKey(10), 50, 500);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)255, r.Strength);
            Assert.False(s.Refresh(new MemoryKey(99), 10, 500));
        }

        [Fact]
        public void Decay_SubtractsNormalRate_FromOrdinaryRecords()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100));
            s.Decay(30, 10);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)70, r.Strength);
            Assert.Equal(100, r.LastRefresh);   // decay does not refresh
        }

        [Fact]
        public void Decay_SurpriseRecords_DecaySlower()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100, MemoryFlags.None));
            s.Encode(Rec(20, 100, MemoryFlags.Surprise));
            s.Decay(40, 10);

            s.TryGet(new MemoryKey(10), out var ordinary);
            s.TryGet(new MemoryKey(20), out var surprising);
            Assert.Equal((byte)60, ordinary.Strength);    // 100 - 40
            Assert.Equal((byte)90, surprising.Strength);   // 100 - 10
        }

        [Fact]
        public void Decay_DropsRecordsThatReachZero_PreservingKeyOrder()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 20));
            s.Encode(Rec(20, 100));
            s.Encode(Rec(30, 15));
            s.Decay(30, 5);   // 10 -> -10 dropped, 20 -> 70, 30 -> -15 dropped

            Assert.Equal(1, s.Count);
            Assert.Equal(new long[] { 20 }, Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void Decay_InnateRecords_AreImmune()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 5, MemoryFlags.Innate));
            s.Decay(255, 255);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(1, s.Count);
            Assert.Equal((byte)5, r.Strength);   // untouched
        }
    }
}
