using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MeaningsStoreFoldTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Fold_UpdatesPredictedStats_LeavingValenceAndConfidence()
        {
            var s = new MeaningsStore(8, MeaningsConfig.Default);
            var id = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), false);

            for (int i = 0; i < 4; i++) Assert.True(s.Fold(id, Bag((1, 1.0), (2, 1.0))));

            s.TryGetNode(id, out var node);
            Assert.Equal(2, node.Predicted.TypeCount);
            Assert.Equal(2, node.Prediction(MeaningsConfig.Default).Count);   // both constant types predicted
            Assert.Equal(Fixed.FromDouble(-1.0), node.Valence);               // untouched
            Assert.Equal(Fixed.FromDouble(0.9), node.Confidence);             // untouched
        }

        [Fact]
        public void Fold_MissingId_ReturnsFalse()
        {
            var s = new MeaningsStore(8, MeaningsConfig.Default);
            Assert.False(s.Fold(new CategoryId(99), Bag((1, 1.0))));
        }
    }
}
