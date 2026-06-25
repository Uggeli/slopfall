using Xunit;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;

namespace Sim.MemoryTests
{
    /// <summary>
    /// Phase A's reason, made a test: a category seeded on the SAME atom perception emits is
    /// recognized. Under the old id-bands this was structurally impossible — seeds lived at ids
    /// 1-4 while perception emitted 1000+, so SignatureDistance never hit zero. With AtomName the
    /// seed and the percept share one key by construction.
    /// </summary>
    public class RecognitionPayoffTests
    {
        static AtomBag Sig(AtomTypeId atom) => AtomBag.Create(new[] { new Atom(atom, Fixed.One) });

        [Fact]
        public void Recognize_Fires_WhenSeededOnTheAtomPerceptionEmits()
        {
            var store = new MeaningsStore(8, MeaningsConfig.Default);

            // Seed a category whose prototype is the very atom a civilian broadcasts.
            AtomTypeId civilian = PerceivableAtoms.Kind(EntityKind.CivilianNPC);
            store.AddNode(Sig(civilian), Fixed.FromDouble(0.1), Fixed.FromDouble(0.9), true);

            // Perceiving that same atom recognizes the seeded category.
            Assert.NotEqual(CategoryId.None, store.Recognize(Sig(civilian)));
        }

        [Fact]
        public void Recognize_Misses_OnADifferentAtom()
        {
            var store = new MeaningsStore(8, MeaningsConfig.Default);
            store.AddNode(Sig(PerceivableAtoms.Kind(EntityKind.CivilianNPC)),
                          Fixed.FromDouble(0.1), Fixed.FromDouble(0.9), true);

            // A place-kind atom is far from the civilian prototype — no recognition.
            Assert.Equal(CategoryId.None, store.Recognize(Sig(PlaceAtoms.Kind(BuildingKind.Tavern))));
        }
    }
}
