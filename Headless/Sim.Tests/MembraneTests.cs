using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// L3 gates (docs/living_world_L3_membrane.md): the subjective place-read.
    /// Pure math, so a thin unit layer is the right tier — different histories
    /// yield different value (the subjective view), bounded so the term colors a
    /// choice without flipping its sign or swamping the need gap. No tuned
    /// magnitudes; direction and bounds only.
    public class MembraneTests
    {
        [Fact]
        public void MeanRegard_ReadsCompany_ExcludesSelf_StrangersNeutral()
        {
            var self = new EntityId(1);
            var friend = new EntityId(2);
            var enemy = new EntityId(3);
            var stranger = new EntityId(4);

            var dossier = new RelationsData();
            dossier.Of[friend] = new RelationData { Regard = 0.8 };
            dossier.Of[enemy] = new RelationData { Regard = -0.6 };

            Assert.Equal(0.8, OddSystem.MeanRegard(dossier, new List<EntityId> { self, friend }, self), 6); // self excluded
            Assert.Equal(-0.6, OddSystem.MeanRegard(dossier, new List<EntityId> { enemy }, self), 6);
            Assert.Equal(0.4, OddSystem.MeanRegard(dossier, new List<EntityId> { friend, stranger }, self), 6); // (0.8+0)/2
            Assert.Equal(0.0, OddSystem.MeanRegard(dossier, new List<EntityId>(), self), 6);                 // empty
            Assert.Equal(0.0, OddSystem.MeanRegard(null, new List<EntityId> { friend }, self), 6);           // no dossier
        }

        [Fact]
        public void SamePlace_DifferentHistories_DivergentValue()
        {
            var self = new EntityId(1);
            var c = new EntityId(9);
            var crowd = new List<EntityId> { c };

            var helped = new RelationsData(); helped.Of[c] = new RelationData { Regard = 0.9 };
            var refused = new RelationsData(); refused.Of[c] = new RelationData { Regard = -0.9 };

            // Same place, same world — but the one C helped values it more than
            // the one C refused. That gap is the subjective view.
            double fHelped = OddSystem.RelationFactor(OddSystem.MeanRegard(helped, crowd, self));
            double fRefused = OddSystem.RelationFactor(OddSystem.MeanRegard(refused, crowd, self));
            Assert.True(fHelped > 1.0, "helped not boosted: " + fHelped);
            Assert.True(fRefused < 1.0, "refused not damped: " + fRefused);
            Assert.True(fHelped > fRefused, "no divergence");
        }

        [Fact]
        public void RelationFactor_ColorsButNeverFlipsOrSwamps()
        {
            Assert.Equal(1.0, OddSystem.RelationFactor(0.0), 6);   // neutral company → no effect
            for (double r = -2.0; r <= 2.0; r += 0.25)
            {
                double f = OddSystem.RelationFactor(r);
                Assert.True(f >= 0.5 && f <= 1.5, "out of band at " + r + ": " + f);
                Assert.True(f > 0, "would flip the gate's sign at " + r);
            }
            Assert.True(OddSystem.RelationFactor(-0.5) < OddSystem.RelationFactor(0.5), "not monotone");
        }
    }
}
