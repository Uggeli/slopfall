using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AppearanceAtomTests
    {
        [Fact]
        public void BeastAndDrifter_AreDistinctNeutralKindAtoms()
        {
            var beast = AtomName.Beast.ToId();
            var drifter = AtomName.Drifter.ToId();
            Assert.NotEqual(beast, drifter);
            // both are identity/Kind atoms (so they form a signature + a category)
            Assert.Equal(AtomCategory.Kind, AtomCatalog.For(beast).Category);
            Assert.Equal(AtomCategory.Kind, AtomCatalog.For(drifter).Category);
            Assert.True(AtomCatalog.For(beast).IsIdentity);
            // distinct from a civilian's neutral identity
            Assert.NotEqual(beast, PerceivableAtoms.Kind(EntityKind.CivilianNPC));
            Assert.NotEqual(drifter, PerceivableAtoms.Kind(EntityKind.CivilianNPC));
            // round-trips through the reverse map
            Assert.Equal(AtomName.Beast, AtomCatalog.NameOf(beast));
            Assert.Equal(AtomName.Drifter, AtomCatalog.NameOf(drifter));
        }
    }
}
