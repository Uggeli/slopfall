using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationPassTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MeaningsStore Predicts1(out CategoryId id)
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 1.0)));
            return m;
        }

        [Fact]
        public void ConfirmingRecord_DissolvesFasterThanSurprising()
        {
            var m = Predicts1(out var id);
            var s = new MemoryStore(8);
            // Confirming: delta matches the prediction -> re-diffs to empty -> dropped in the pass.
            s.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((1, 1.0)), 100, 0, 0, MemoryFlags.None));
            // Surprising: delta contradicts -> survives re-diff, SURPRISE resists decay.
            s.Encode(new MemoryRecord(new MemoryKey(20), id, Bag((1, 0.0)), 100, 0, 0, MemoryFlags.Surprise));

            Consolidation.Pass(s, m, ConsolidationConfig.Default);

            Assert.False(s.TryGet(new MemoryKey(10), out _));     // confirming dissolved
            Assert.True(s.TryGet(new MemoryKey(20), out var surviving));
            Assert.Equal((byte)95, surviving.Strength);           // 100 - surprise-rate 5
        }

        [Fact]
        public void HighSpreadAtom_NeverEntersTheFact_RidesInTheRecord()
        {
            // Type 1 is stable (always 1.0 -> predicted); type 99 (the "Tuesday") swings -> never predicted.
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            var id = m.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.FromDouble(0.5), false);
            double[] tuesdays = { 0.1, 0.9, 0.2, 0.8 };
            for (int i = 0; i < 4; i++) m.Fold(id, Bag((1, 1.0), (99, tuesdays[i])));

            m.TryGetNode(id, out var node);
            var prediction = node.Prediction(MeaningsConfig.Default);
            Assert.True(prediction.Contains(new AtomTypeId(1)));    // the fact keeps the stable feature
            Assert.False(prediction.Contains(new AtomTypeId(99)));  // the Tuesday never enters the fact

            var s = new MemoryStore(8);
            s.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((99, 0.5)), 100, 0, 0, MemoryFlags.Surprise));
            Consolidation.Pass(s, m, ConsolidationConfig.Default);

            s.TryGet(new MemoryKey(10), out var r);                // re-diff can't absorb 99 (not predicted)
            Assert.Equal(new[] { 99 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void RepeatedPasses_KeepStoreBounded_AndDecayDropsWeakRecords()
        {
            var m = Predicts1(out var id);
            var s = new MemoryStore(8);
            // A weak refused record should decay to nothing over a few passes; an INNATE one persists.
            s.Encode(new MemoryRecord(new MemoryKey(10), id, Bag((1, 0.0)), 30, 0, 0, MemoryFlags.None));
            s.Encode(new MemoryRecord(new MemoryKey(20), id, Bag((1, 0.0)), 50, 0, 0, MemoryFlags.Innate));

            for (int p = 0; p < 3; p++) Consolidation.Pass(s, m, ConsolidationConfig.Default);

            Assert.True(s.Count <= s.Capacity);
            Assert.False(s.TryGet(new MemoryKey(10), out _));      // 30 - 3*20 -> dropped
            Assert.True(s.TryGet(new MemoryKey(20), out var innate));
            Assert.Equal((byte)50, innate.Strength);              // INNATE immune to decay
        }
    }
}
