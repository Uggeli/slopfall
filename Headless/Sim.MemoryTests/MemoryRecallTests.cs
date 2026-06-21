using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryRecallTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        // A meanings store with one node whose prediction is the folded constant features.
        static MeaningsStore Meanings(out CategoryId id, params (int type, double v)[] features)
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            for (int i = 0; i < 4; i++) m.Fold(id, Bag(features));
            return m;
        }

        [Fact]
        public void Reconstruct_Recognized_MergesPredictionAndDelta()
        {
            var m = Meanings(out var id, (1, 1.0), (2, 1.0));
            var rec = new MemoryRecord(new MemoryKey(10), id, Bag((3, 0.5)), 100, 100, 100, MemoryFlags.None);

            var recon = MemoryRecall.Reconstruct(rec, m);
            Assert.Equal(new[] { 1, 2, 3 }, recon.Atoms.Select(a => a.Type.Value).ToArray());
            recon.TryGet(new AtomTypeId(1), out var v1);
            recon.TryGet(new AtomTypeId(3), out var v3);
            Assert.Equal(Fixed.FromDouble(1.0), v1);   // from the prediction
            Assert.Equal(Fixed.FromDouble(0.5), v3);   // from the delta
        }

        [Fact]
        public void Reconstruct_Novel_IsVerbatimDelta()
        {
            var m = Meanings(out _, (1, 1.0));
            var bag = Bag((5, 0.7));
            var rec = new MemoryRecord(new MemoryKey(10), CategoryId.None, bag, 100, 100, 100, MemoryFlags.None);
            Assert.Same(bag, MemoryRecall.Reconstruct(rec, m));   // nothing to merge against
        }

        [Fact]
        public void Reconstruct_DriftedCategory_ProducesConfidentFalseMemory()
        {
            var m = Meanings(out var id, (1, 1.0));   // prediction {1: 1.0}
            var rec = new MemoryRecord(new MemoryKey(10), id, Bag((2, 0.3)), 100, 100, 100, MemoryFlags.None);

            var before = MemoryRecall.Reconstruct(rec, m);
            before.TryGet(new AtomTypeId(1), out var v1Before);
            Assert.Equal(Fixed.FromDouble(1.0), v1Before);   // recalled "1" == 1.0 at encode time

            // Drift the category toward 0.9 (stays low-variance, so type 1 keeps predicting).
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 0.9)));

            var after = MemoryRecall.Reconstruct(rec, m);
            after.TryGet(new AtomTypeId(1), out var v1After);
            after.TryGet(new AtomTypeId(2), out var v2After);
            Assert.True(v1After.ToDouble() < 1.0 && v1After.ToDouble() > 0.8);   // confidently WRONG: the drifted prediction
            Assert.Equal(Fixed.FromDouble(0.3), v2After);                        // the stored specific is unchanged
        }

        [Fact]
        public void Recall_Reconsolidates_BumpsStrengthAndRecency()
        {
            var m = Meanings(out var id, (1, 1.0));
            var store = new MemoryStore(8);
            store.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((2, 0.3)), 100, 100, 100, MemoryFlags.None));

            Assert.True(MemoryRecall.Recall(store, m, new MemoryKey(10), 20, 500, out var recon));
            Assert.True(recon.Count > 0);
            store.TryGet(new MemoryKey(10), out var r);
            Assert.Equal((byte)120, r.Strength);     // reconsolidation bumped strength
            Assert.Equal(500, r.LastRefresh);
        }

        [Fact]
        public void Recall_MissingKey_ReturnsFalse()
        {
            var m = Meanings(out _, (1, 1.0));
            var store = new MemoryStore(8);
            Assert.False(MemoryRecall.Recall(store, m, new MemoryKey(99), 20, 500, out var recon));
            Assert.Same(AtomBag.Empty, recon);
        }
    }
}
