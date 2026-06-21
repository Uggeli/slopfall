using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PredictedStatsTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Fold_AccumulatesPerType()
        {
            var ps = new PredictedStats();
            ps.Fold(Bag((1, 1.0), (2, 0.0)));
            ps.Fold(Bag((1, 1.0), (2, 1.0)));

            Assert.Equal(2, ps.TypeCount);
            Assert.True(ps.TryGetStat(new AtomTypeId(1), out var s1));
            Assert.Equal(2, s1.Count);
            Assert.Equal(Fixed.FromDouble(1.0), s1.Mean());
        }

        [Fact]
        public void Prediction_IncludesLowSpread_ExcludesHighSpread()
        {
            // Type 1 (the "fox"): always ~1.0 -> low spread -> predicted.
            // Type 2 (the "spot"): swings across [0,1] -> high spread -> NOT predicted.
            var ps = new PredictedStats();
            double[] foxes = { 1.00, 0.99, 1.00, 0.98, 1.00, 0.99 };
            double[] spots = { 0.10, 0.90, 0.20, 0.80, 0.05, 0.95 };
            for (int i = 0; i < foxes.Length; i++)
                ps.Fold(Bag((1, foxes[i]), (2, spots[i])));

            long varThreshold = 655;   // ~ std 0.1, Q16
            var pred = ps.Prediction(varThreshold, minCount: 3);

            Assert.Equal(new[] { 1 }, pred.Atoms.Select(a => a.Type.Value).ToArray());   // only the fox
            pred.TryGet(new AtomTypeId(1), out var mean);
            Assert.True(System.Math.Abs(mean.ToDouble() - 0.99) < 0.05);
        }

        [Fact]
        public void Prediction_RespectsMinCount()
        {
            var ps = new PredictedStats();
            ps.Fold(Bag((1, 1.0)));
            ps.Fold(Bag((1, 1.0)));   // only 2 samples
            Assert.Equal(0, ps.Prediction(655, minCount: 3).Count);   // below minCount -> not predicted
            ps.Fold(Bag((1, 1.0)));
            Assert.Equal(1, ps.Prediction(655, minCount: 3).Count);   // now 3 -> predicted
        }

        [Fact]
        public void Prediction_IsEmpty_ForFreshStats()
        {
            Assert.Same(AtomBag.Empty, new PredictedStats().Prediction(655, 1));
        }
    }
}
