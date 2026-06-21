using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MeaningsStoreRecognizeTests
    {
        static AtomBag Sig(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MeaningsStore Store() => new MeaningsStore(16, MeaningsConfig.Default);

        [Fact]
        public void AddNode_AssignsAscendingIds()
        {
            var s = Store();
            var a = s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            var b = s.AddNode(Sig((2, 1.0)), Fixed.Zero, Fixed.Zero, false);
            Assert.Equal(new CategoryId(1), a);
            Assert.Equal(new CategoryId(2), b);
            Assert.Equal(2, s.Count);
            Assert.True(s.TryGetNode(a, out var na));
            Assert.Equal(new CategoryId(1), na.Id);
        }

        [Fact]
        public void Recognize_ReturnsNearestPrototypeWithinThreshold()
        {
            var s = Store();
            var fox = s.AddNode(Sig((1, 1.0)), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), true);
            var rabbit = s.AddNode(Sig((2, 1.0)), Fixed.FromDouble(0.0), Fixed.FromDouble(0.9), true);

            // A near-fox signature (type 1 ~ 0.95) recognizes the fox.
            Assert.Equal(fox, s.Recognize(Sig((1, 0.95))));
            // A near-rabbit signature recognizes the rabbit.
            Assert.Equal(rabbit, s.Recognize(Sig((2, 0.98))));
        }

        [Fact]
        public void Recognize_NoMatch_IsNovel()
        {
            var s = Store();
            s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, true);
            // A signature far from every prototype (different type entirely) is novel.
            Assert.True(s.Recognize(Sig((9, 1.0))).IsNone);
        }

        [Fact]
        public void Recognize_TieBreaksToLowestId()
        {
            // Two identical prototypes -> equal distance -> the lower id wins (deterministic).
            var s = Store();
            var first = s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            Assert.Equal(first, s.Recognize(Sig((1, 1.0))));
        }

        [Fact]
        public void AddNode_Full_Throws()
        {
            var s = new MeaningsStore(1, MeaningsConfig.Default);
            s.AddNode(Sig((1, 1.0)), Fixed.Zero, Fixed.Zero, false);
            Assert.Throws<System.InvalidOperationException>(
                () => s.AddNode(Sig((2, 1.0)), Fixed.Zero, Fixed.Zero, false));
        }
    }
}
