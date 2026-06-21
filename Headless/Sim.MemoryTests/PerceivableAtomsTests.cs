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
            Assert.Equal(1000 + (int)EntityKind.CivilianNPC, PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
            Assert.Equal(2000 + (int)ResidentRole.Keeper, PerceivableAtoms.Role(ResidentRole.Keeper).Value);
            Assert.Equal(3000 + 7, PerceivableAtoms.Race(7).Value);
            Assert.Equal(4000 + (int)ActivityKind.Beg, PerceivableAtoms.Activity(ActivityKind.Beg).Value);
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
