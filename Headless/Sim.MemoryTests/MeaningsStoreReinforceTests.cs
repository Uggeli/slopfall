using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MeaningsStoreReinforceTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MeaningsStore Store() => new MeaningsStore(16, MeaningsConfig.Default);

        [Fact]
        public void Reinforce_FoldsPerceptIntoPredictedStats()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            for (int i = 0; i < 4; i++) s.Reinforce(id, Bag((1, 1.0), (2, 1.0)), Fixed.One);

            s.TryGetNode(id, out var node);
            var pred = node.Prediction(MeaningsConfig.Default);
            Assert.Equal(new[] { 1, 2 }, pred.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Reinforce_ConfirmingStream_RaisesConfidence_ConvergesValence()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            for (int i = 0; i < 40; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(1.0));

            s.TryGetNode(id, out var node);
            Assert.True(node.Confidence.ToDouble() > 0.5);                 // confidence climbed
            Assert.True(node.Valence.ToDouble() > 0.8);                    // valence converged toward +1
        }

        [Fact]
        public void Reinforce_ContradictingStream_LowersConfidence_WithoutCorrupting()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(1.0), Fixed.FromDouble(0.9), false);
            // Confirm to a high confidence and a firmly-positive valence first.
            for (int i = 0; i < 10; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(1.0));
            s.TryGetNode(id, out var node);
            double confAfterConfirm = node.Confidence.ToDouble();
            Assert.True(node.Valence.ToDouble() > 0.5);

            // Pure-contradiction phase: while valence is still positive (the outcome genuinely
            // contradicts the node's charge), each contradicting tick must STRICTLY lower
            // confidence — the spec's "weakens" — and never go out of bounds ("not corrupts").
            double prevConf = confAfterConfirm;
            for (int i = 0; i < 3; i++)
            {
                s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(-1.0));
                s.TryGetNode(id, out node);
                Assert.True(node.Valence.ToDouble() > 0.0);              // still genuinely contradicting
                Assert.True(node.Confidence.ToDouble() < prevConf);      // strictly weakened
                Assert.True(node.Confidence.ToDouble() >= 0.0);          // bounded, not corrupted
                prevConf = node.Confidence.ToDouble();
            }
        }

        [Fact]
        public void Reinforce_ConfidenceClampedToOne()
        {
            var s = Store();
            var id = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(1.0), Fixed.FromDouble(1.0), false);
            for (int i = 0; i < 50; i++) s.Reinforce(id, Bag((1, 1.0)), Fixed.FromDouble(1.0));
            s.TryGetNode(id, out var node);
            Assert.True(node.Confidence.ToDouble() <= 1.0);
        }

        [Fact]
        public void Reinforce_MissingId_ReturnsFalse()
        {
            var s = Store();
            Assert.False(s.Reinforce(new CategoryId(99), Bag((1, 1.0)), Fixed.One));
        }
    }
}
