using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryStoreRefreshDecayTests
    {
        static readonly AtomTypeId A = new AtomTypeId(1);
        static readonly AtomTypeId B = new AtomTypeId(2);

        // Single-atom record: derived record.Strength == that atom's strength (max).
        static MemoryRecord Rec(long key, byte strength, MemoryFlags flags = MemoryFlags.None, long tick = 100)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, AtomBag.Create(new[] { new Atom(A, Fixed.One) }),
                                strength, tick, tick, flags);

        // Two-atom record with independent per-atom meta.
        static MemoryRecord Rec2(long key, byte sa, MemoryFlags fa, byte sb, MemoryFlags fb, long tick = 100)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None,
                                AtomBag.Create(new[] { new Atom(A, Fixed.One), new Atom(B, Fixed.One) }),
                                new[] { new AtomMeta(sa, fa), new AtomMeta(sb, fb) }, tick, tick);

        [Fact]
        public void Refresh_BumpsEveryNonInnateAtom_AndLastRefresh()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec2(10, 100, MemoryFlags.None, 120, MemoryFlags.Surprise));
            Assert.True(s.Refresh(new MemoryKey(10), 40, 500));
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)140, r.Meta[0].Strength);   // 100 + 40
            Assert.Equal((byte)160, r.Meta[1].Strength);   // 120 + 40
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
            Assert.Equal((byte)255, r.Meta[0].Strength);
            Assert.False(s.Refresh(new MemoryKey(99), 10, 500));
        }

        [Fact]
        public void Decay_ScalesWithStrength_TrivialFadesFast_ImportantBarely()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 200));   // important
            s.Encode(Rec(20, 60));    // ordinary
            s.Decay(30, 10);

            s.TryGet(new MemoryKey(10), out var important);
            s.TryGet(new MemoryKey(20), out var ordinary);
            // dec = max(1, 30*(255-S)/255): S=200 -> 6 -> 194;  S=60 -> 22 -> 38
            Assert.Equal((byte)194, important.Meta[0].Strength);
            Assert.Equal((byte)38, ordinary.Meta[0].Strength);
            Assert.Equal(100, important.LastRefresh);   // decay does not refresh
        }

        [Fact]
        public void Decay_SurpriseAtom_DecaysSlowerThanOrdinary()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 100, MemoryFlags.None));
            s.Encode(Rec(20, 100, MemoryFlags.Surprise));
            s.Decay(40, 10);
            s.TryGet(new MemoryKey(10), out var ordinary);
            s.TryGet(new MemoryKey(20), out var surprising);
            // ordinary: 40*155/255=24 -> 76 ; surprise: 10*155/255=6 -> 94
            Assert.Equal((byte)76, ordinary.Meta[0].Strength);
            Assert.Equal((byte)94, surprising.Meta[0].Strength);
            Assert.True(surprising.Meta[0].Strength > ordinary.Meta[0].Strength);
        }

        [Fact]
        public void Decay_PerAtom_DropsTrivialAtom_KeepsImportantPeer_InSameRecord()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec2(10, 10, MemoryFlags.None, 250, MemoryFlags.None));   // trivial A, important B
            s.Decay(40, 10);
            s.TryGet(new MemoryKey(10), out var r);
            // A: 40*245/255=38 -> 10-38<0 dropped;  B: 40*5/255=0 -> max1 -> 249 survives
            Assert.Equal(1, r.DeltaBag.Count);
            Assert.True(r.DeltaBag.Contains(B));
            Assert.False(r.DeltaBag.Contains(A));
            Assert.Equal((byte)249, r.Meta[0].Strength);
        }

        [Fact]
        public void Decay_DropsRecord_WhenItsLastAtomDies_PreservingKeyOrder()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 20));
            s.Encode(Rec(20, 200));
            s.Encode(Rec(30, 15));
            s.Decay(40, 5);   // S=20 and S=15 both die (dec>S); S=200 survives
            Assert.Equal(1, s.Count);
            Assert.Equal(new long[] { 20 }, Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void Decay_InnateAtom_IsImmune()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, 5, MemoryFlags.Innate));
            s.Decay(255, 255);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(1, s.Count);
            Assert.Equal((byte)5, r.Meta[0].Strength);   // untouched
        }
    }
}
