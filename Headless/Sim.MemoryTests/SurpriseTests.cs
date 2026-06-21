using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SurpriseTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Maximal_IsOneOne()
        {
            Assert.Equal(Fixed.One, Surprise.Maximal.Attention);
            Assert.Equal(Fixed.One, Surprise.Maximal.Encode);
        }

        [Fact]
        public void PerfectMatch_IsZeroSurprise()
        {
            var bag = Bag((1, 1.0), (2, 0.5));
            var s = Surprise.Against(bag, bag);
            Assert.Equal(Fixed.Zero, s.Attention);
            Assert.Equal(Fixed.Zero, s.Encode);
        }

        [Fact]
        public void BothEmpty_IsZero()
        {
            var s = Surprise.Against(AtomBag.Empty, AtomBag.Empty);
            Assert.Equal(Fixed.Zero, s.Attention);
            Assert.Equal(Fixed.Zero, s.Encode);
        }

        [Fact]
        public void NotchedEar_SpikesAttention_ButLowEncode()
        {
            // Prediction: 5 stable features at 1.0. Percept: those 5 (matching) + 1 new atom (the
            // notched ear) at 1.0. MAX over {0,0,0,0,0,1.0} = 1.0 (attention spikes); MEAN = 1/6.
            var prediction = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (99, 1.0));
            var s = Surprise.Against(percept, prediction);

            Assert.Equal(Fixed.One, s.Attention);                  // MAX = the new atom, full spike
            Assert.True(s.Encode.ToDouble() < 0.2);                // MEAN diluted by the 5 matches
            Assert.True(s.Encode.ToDouble() > 0.0);                // but non-zero (something WAS new)
        }

        [Fact]
        public void TotallyWrong_IsHighOnBothOperators()
        {
            // Every predicted feature (1.0) is contradicted (percept 0.0): error 1.0 on all 5.
            var prediction = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            var percept = Bag((1, 0.0), (2, 0.0), (3, 0.0), (4, 0.0), (5, 0.0));
            var s = Surprise.Against(percept, prediction);
            Assert.Equal(Fixed.One, s.Attention);
            Assert.True(s.Encode.ToDouble() > 0.9);                // MEAN of all-1.0 errors ~ 1.0
        }

        [Fact]
        public void MissingExpectedFeature_CountsAsError()
        {
            // Prediction expects type 2 at 1.0; percept lacks it -> error = the expected magnitude.
            var prediction = Bag((1, 1.0), (2, 1.0));
            var percept = Bag((1, 1.0));
            var s = Surprise.Against(percept, prediction);
            Assert.Equal(Fixed.One, s.Attention);                  // the absent feature is a full violation
            Assert.True(s.Encode.ToDouble() > 0.4);                // MEAN over {0 (type1), 1.0 (type2)} = 0.5
        }
    }
}
