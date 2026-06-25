using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SeedInnateTests
    {
        static AtomBag Sig(AtomName kind)
            => AtomBag.Create(new[] { new Atom(kind.ToId(), Fixed.One) });

        [Fact]
        public void SeedInnate_IsRecognized_ThenDriftsWithReinforce()
        {
            var store = new MeaningsStore(8, MeaningsConfig.Default);
            var sig = Sig(AtomName.Civilian);
            var id = store.SeedInnate(sig, Fixed.FromDouble(0.2), Fixed.FromDouble(0.3));

            Assert.True(store.RecognizedValence(sig, out var v, out var c));   // born believing
            Assert.True(v.ToDouble() > 0.0);
            Assert.True(c.ToDouble() > 0.0);

            // a bad encounter drifts the innate node negative (it learns)
            store.Reinforce(id, sig, Fixed.FromDouble(-1.0));
            store.RecognizedValence(sig, out var v2, out _);
            Assert.True(v2.ToDouble() < v.ToDouble());
        }
    }
}
