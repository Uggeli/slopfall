using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PerceivableAtomsTests
    {
        [Fact]
        public void Helpers_AreRangeCorrect_AndDistinct()
        {
            // The helper stamps the named atom; identity is the AtomName, not a band offset.
            Assert.Equal(AtomName.Civilian.ToId(), PerceivableAtoms.Kind(EntityKind.CivilianNPC));
            Assert.Equal(AtomName.Keeper.ToId(),   PerceivableAtoms.Role(ResidentRole.Keeper));
            Assert.Equal(AtomName.RaceKhajiit.ToId(), PerceivableAtoms.Race(7));
            Assert.Equal(AtomName.ActBeg.ToId(),   PerceivableAtoms.Activity(ActivityKind.Beg));

            Assert.Equal(AtomCategory.Kind,     AtomCatalog.For(PerceivableAtoms.Kind(EntityKind.CivilianNPC)).Category);
            Assert.Equal(AtomCategory.Role,     AtomCatalog.For(PerceivableAtoms.Role(ResidentRole.Keeper)).Category);
            Assert.Equal(AtomCategory.Race,     AtomCatalog.For(PerceivableAtoms.Race(7)).Category);
            Assert.Equal(AtomCategory.Activity, AtomCatalog.For(PerceivableAtoms.Activity(ActivityKind.Beg)).Category);

            // Distinct identities.
            var ids = new[]
            {
                PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value,
                PerceivableAtoms.Role(ResidentRole.Keeper).Value,
                PerceivableAtoms.Race(7).Value,
                PerceivableAtoms.Activity(ActivityKind.Beg).Value,
            };
            Assert.Equal(ids.Length, new System.Collections.Generic.HashSet<int>(ids).Count);
        }

        [Fact]
        public void Categories_DoNotCollide()
        {
            Assert.NotEqual(PerceivableAtoms.Kind(EntityKind.EnemyMonster), PerceivableAtoms.Role(ResidentRole.Resident));
            Assert.NotEqual(PerceivableAtoms.Role(ResidentRole.Resident), PerceivableAtoms.Activity(ActivityKind.Sleep));
        }

        [Fact]
        public void TryActivity_None_IsNoAtom()
        {
            Assert.False(PerceivableAtoms.TryActivity(ActivityKind.None, out _));
            Assert.True(PerceivableAtoms.TryActivity(ActivityKind.Work, out var a));
            Assert.Equal(PerceivableAtoms.Activity(ActivityKind.Work), a);
        }
    }
}
