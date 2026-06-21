using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AtomBagMergeDiffTests
    {
        static Atom A(int type, double v) => new Atom(new AtomTypeId(type), Fixed.FromDouble(v));

        [Fact]
        public void Merge_Disjoint_IsSortedUnion()
        {
            var p = AtomBag.Create(new[] { A(1, 0.1), A(3, 0.3) });
            var d = AtomBag.Create(new[] { A(2, 0.2), A(4, 0.4) });
            var m = AtomBag.Merge(p, d);
            Assert.Equal(new[] { 1, 2, 3, 4 }, m.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Merge_Overlap_DeltaWins()
        {
            var p = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            var d = AtomBag.Create(new[] { A(2, 0.9) });
            var m = AtomBag.Merge(p, d);

            Assert.Equal(2, m.Count);
            m.TryGet(new AtomTypeId(2), out var v);
            Assert.Equal(Fixed.FromDouble(0.9), v);          // delta won
            m.TryGet(new AtomTypeId(1), out var v1);
            Assert.Equal(Fixed.FromDouble(0.1), v1);         // prediction kept
        }

        [Fact]
        public void Merge_WithEmpty_ReturnsOther()
        {
            var p = AtomBag.Create(new[] { A(1, 0.1) });
            Assert.Equal(1, AtomBag.Merge(p, AtomBag.Empty).Count);
            Assert.Equal(1, AtomBag.Merge(AtomBag.Empty, p).Count);
            Assert.Same(AtomBag.Empty, AtomBag.Merge(AtomBag.Empty, AtomBag.Empty));
        }

        [Fact]
        public void Diff_IdenticalBags_IsEmpty()
        {
            var bag = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            Assert.Same(AtomBag.Empty, AtomBag.Diff(bag, bag));
        }

        [Fact]
        public void Diff_KeepsNewAndChanged_DropsMatched()
        {
            var percept    = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.9), A(3, 0.3) });
            var prediction = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            var diff = AtomBag.Diff(percept, prediction);

            // type 1 matched -> dropped; type 2 changed -> kept; type 3 new -> kept
            Assert.Equal(new[] { 2, 3 }, diff.Atoms.Select(a => a.Type.Value).ToArray());
            diff.TryGet(new AtomTypeId(2), out var v2);
            Assert.Equal(Fixed.FromDouble(0.9), v2);
        }

        [Fact]
        public void Diff_AtomsOnlyInPrediction_AreExcluded()
        {
            var percept    = AtomBag.Create(new[] { A(1, 0.1) });
            var prediction = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            Assert.Same(AtomBag.Empty, AtomBag.Diff(percept, prediction));
        }

        [Fact]
        public void MergePredictionWithDiff_ReconstructsPercept()
        {
            var percept    = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.9), A(3, 0.3) });
            var prediction = AtomBag.Create(new[] { A(1, 0.1), A(2, 0.2) });
            var delta = AtomBag.Diff(percept, prediction);
            var recon = AtomBag.Merge(prediction, delta);

            Assert.Equal(percept.Atoms.Select(a => a.Type.Value).ToArray(),
                         recon.Atoms.Select(a => a.Type.Value).ToArray());
            foreach (var a in percept.Atoms)
            {
                Assert.True(recon.TryGet(a.Type, out var rv));
                Assert.Equal(a.Value, rv);
            }
        }
    }
}
