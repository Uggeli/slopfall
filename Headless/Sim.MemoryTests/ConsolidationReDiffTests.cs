using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationReDiffTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        // Meanings store whose single node predicts {1: 1.0}.
        static MeaningsStore FoxPredicts1(out CategoryId id)
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 1.0)));
            return m;
        }

        static MemoryRecord Rec(long key, CategoryId cat, AtomBag delta, MemoryFlags flags = MemoryFlags.None)
            => new MemoryRecord(new MemoryKey(key), cat, delta, 100, 100, 100, flags);

        [Fact]
        public void Remove_DropsRecord_PreservingOrder()
        {
            var s = new MemoryStore(8);
            s.Encode(Rec(10, CategoryId.None, AtomBag.Empty));
            s.Encode(Rec(20, CategoryId.None, AtomBag.Empty));
            s.Encode(Rec(30, CategoryId.None, AtomBag.Empty));
            Assert.True(s.Remove(new MemoryKey(20)));
            Assert.False(s.Remove(new MemoryKey(99)));
            Assert.Equal(new long[] { 10, 30 }, Enumerable.Range(0, s.Count).Select(i => s[i].Key.Value).ToArray());
        }

        [Fact]
        public void ReDiff_AbsorbedRecord_IsDropped()
        {
            var m = FoxPredicts1(out var id);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, id, Bag((1, 1.0))));   // delta now exactly matches the prediction
            Consolidation.ReDiff(s, m);
            Assert.Equal(0, s.Count);               // information migrated into the fact -> gone
        }

        [Fact]
        public void ReDiff_RefusedRecord_KeepsItsDelta()
        {
            var m = FoxPredicts1(out var id);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, id, Bag((1, 0.0))));   // contradicts the prediction
            Consolidation.ReDiff(s, m);
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 1 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
            r.DeltaBag.TryGet(new AtomTypeId(1), out var v);
            Assert.Equal(Fixed.FromDouble(0.0), v);
        }

        [Fact]
        public void ReDiff_PartiallyAbsorbed_ShrinksDelta()
        {
            var m = FoxPredicts1(out var id);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, id, Bag((1, 1.0), (2, 0.0))));   // type 1 absorbed, type 2 not predicted
            Consolidation.ReDiff(s, m);
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 2 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // only the un-absorbed atom
        }

        [Fact]
        public void ReDiff_NovelRecord_IsUntouched()
        {
            var m = FoxPredicts1(out _);
            var s = new MemoryStore(8);
            s.Encode(Rec(10, CategoryId.None, Bag((5, 0.5))));
            Consolidation.ReDiff(s, m);
            Assert.Equal(1, s.Count);
            s.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 5 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
        }
    }
}
