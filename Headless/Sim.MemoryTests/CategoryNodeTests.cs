using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class CategoryNodeTests
    {
        [Fact]
        public void Default_Config_HasScaffoldingKnobs()
        {
            var c = MeaningsConfig.Default;
            Assert.True(c.MatchThresholdRaw > 0);
            Assert.True(c.VarianceThresholdRaw > 0);
            Assert.True(c.MinPredictCount >= 1);
            Assert.True(c.LearnShift >= 1);
            Assert.True(c.ConfidenceGainRaw > 0);
            Assert.True(c.NeutralBandRaw >= 0);
        }

        [Fact]
        public void Node_HoldsIdentityValenceConfidence_AndEmptyPredictedStart()
        {
            var proto = AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) });
            var node = new CategoryNode(new CategoryId(5), proto, Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), innate: true);

            Assert.Equal(new CategoryId(5), node.Id);
            Assert.Same(proto, node.Prototype);
            Assert.Equal(Fixed.FromDouble(-1.0), node.Valence);
            Assert.Equal(Fixed.FromDouble(0.9), node.Confidence);
            Assert.True(node.Innate);
            Assert.Equal(0, node.Predicted.TypeCount);
        }

        [Fact]
        public void Node_Prediction_UsesConfigThresholds()
        {
            var node = new CategoryNode(new CategoryId(1), AtomBag.Empty, Fixed.Zero, Fixed.Zero, false);
            for (int i = 0; i < 4; i++)
                node.Predicted.Fold(AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) }));
            var pred = node.Prediction(MeaningsConfig.Default);
            Assert.Equal(1, pred.Count);   // constant low-spread type predicted under default knobs
        }
    }
}
