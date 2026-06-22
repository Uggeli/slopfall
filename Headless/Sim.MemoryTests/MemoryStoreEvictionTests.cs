using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreEvictionTests
    {
        static readonly AtomTypeId A = new AtomTypeId(1);
        static MemoryRecord Rec(long key, byte strength, long lastRefresh = 100, MemoryFlags flags = MemoryFlags.None)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Create(new[] { new Atom(A, Fixed.One) }),
                                strength, lastRefresh, lastRefresh, flags);

        [Fact]
        public void Encode_Full_StrongerNewRecord_EvictsWeakest()
        {
            var s = new MemoryStore(3);
            s.Encode(Rec(10, 80));
            s.Encode(Rec(20, 20));   // weakest by strength
            s.Encode(Rec(30, 60));

            Assert.True(s.Encode(Rec(40, 50)));   // 50 > weakest 20 -> evict key 20
            Assert.Equal(3, s.Count);
            Assert.False(s.TryGet(new MemoryKey(20), out _));
            Assert.True(s.TryGet(new MemoryKey(40), out _));
            Assert.Equal(new long[] { 10, 30, 40 },
                Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());   // still sorted
        }

        [Fact]
        public void Encode_Full_WeakerNewRecord_DoesNotTake()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 80));
            s.Encode(Rec(20, 60));
            Assert.False(s.Encode(Rec(30, 30)));   // 30 < weakest 60 -> rejected
            Assert.Equal(2, s.Count);
            Assert.False(s.TryGet(new MemoryKey(30), out _));
        }

        [Fact]
        public void Eviction_TieBreak_PrefersOlderLastRefresh()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 50, lastRefresh: 100));   // same strength, older -> least keepable
            s.Encode(Rec(20, 50, lastRefresh: 300));
            // new record strength 50, lastRefresh 900 (freshest): beats the strength-50/oldest record
            Assert.True(s.Encode(Rec(30, 50, lastRefresh: 900)));
            Assert.False(s.TryGet(new MemoryKey(10), out _));   // the oldest strength-50 evicted
            Assert.True(s.TryGet(new MemoryKey(20), out _));
        }

        [Fact]
        public void Eviction_EqualStrength_OlderIncoming_DoesNotTake()
        {
            // The rejection half of "beat the weakest or bust": an incoming record that ties the
            // weakest on strength but is OLDER than it must not evict (strict-keepability boundary).
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 50, lastRefresh: 100));   // weakest (oldest of the strength-50 pair)
            s.Encode(Rec(20, 50, lastRefresh: 300));
            Assert.False(s.Encode(Rec(30, 50, lastRefresh: 50)));   // strength tie, older -> rejected
            Assert.Equal(2, s.Count);
            Assert.False(s.TryGet(new MemoryKey(30), out _));
            Assert.True(s.TryGet(new MemoryKey(10), out _));        // weakest survives
        }

        [Fact]
        public void Eviction_KeyTieBreak_EvictsLowestKeyAmongFullTies()
        {
            // strength AND lastRefresh tie across the store -> the Key tie-break decides the
            // weakest deterministically (lowest key), and a same-strength/same-refresh newcomer
            // (higher key) beats it.
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 50, lastRefresh: 100));
            s.Encode(Rec(20, 50, lastRefresh: 100));
            Assert.True(s.Encode(Rec(30, 50, lastRefresh: 100)));
            Assert.False(s.TryGet(new MemoryKey(10), out _));   // lowest key evicted
            Assert.True(s.TryGet(new MemoryKey(20), out _));
            Assert.True(s.TryGet(new MemoryKey(30), out _));
        }

        [Fact]
        public void Eviction_SkipsInnate_EvictsWeakestNonInnate()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 5, flags: MemoryFlags.Innate));   // weakest by strength but INNATE -> immune
            s.Encode(Rec(20, 90));
            Assert.True(s.Encode(Rec(30, 95)));                // must evict the non-INNATE key 20
            Assert.True(s.TryGet(new MemoryKey(10), out _));   // innate survives
            Assert.False(s.TryGet(new MemoryKey(20), out _));
            Assert.True(s.TryGet(new MemoryKey(30), out _));
        }

        [Fact]
        public void Eviction_AllInnate_RejectsWrite()
        {
            var s = new MemoryStore(2);
            s.Encode(Rec(10, 200, flags: MemoryFlags.Innate));
            s.Encode(Rec(20, 200, flags: MemoryFlags.Innate));
            Assert.False(s.Encode(Rec(30, 255)));   // nothing evictable
            Assert.Equal(2, s.Count);
        }
    }
}
