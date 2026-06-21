using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemorySeedsTests
    {
        static AtomBag Sig(int type, double v)
            => AtomBag.Create(new[] { new Atom(new AtomTypeId(type), Fixed.FromDouble(v)) });

        [Fact]
        public void Install_AddsInnateSeedNodes()
        {
            var store = new MeaningsStore(MemorySeeds.Count + 4, MeaningsConfig.Default);
            MemorySeeds.Install(store);

            Assert.Equal(MemorySeeds.Count, store.Count);
            for (int i = 0; i < store.Count; i++)
                Assert.True(store[i].Innate);   // every seed is INNATE
        }

        [Fact]
        public void Install_SeedsAreRecognizable_AndCarryValence()
        {
            var store = new MeaningsStore(MemorySeeds.Count + 4, MeaningsConfig.Default);
            MemorySeeds.Install(store);

            // A predator-scented percept recognizes the predator seed, which is aversive.
            CategoryId pred = store.Recognize(Sig((int)MemorySeeds.SeedAtom.Predator, 1.0));
            Assert.False(pred.IsNone);
            store.TryGetNode(pred, out var node);
            Assert.True(node.Valence.ToDouble() < 0.0);   // predator = aversive

            CategoryId food = store.Recognize(Sig((int)MemorySeeds.SeedAtom.Food, 1.0));
            Assert.False(food.IsNone);
            store.TryGetNode(food, out var foodNode);
            Assert.True(foodNode.Valence.ToDouble() > 0.0);   // food = appetitive
        }

        [Fact]
        public void Install_TooSmallStore_Throws()
        {
            var store = new MeaningsStore(1, MeaningsConfig.Default);
            Assert.Throws<System.InvalidOperationException>(() => MemorySeeds.Install(store));
        }
    }
}
