using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    // ODD's PLACES scoring helpers: remembered danger lowers an ad's gate (aversion), remembered-empty
    // provisioning lowers it gently (preference). Pure, clamped — mirrors RelationFactor/ConscienceFactor.
    public class PlaceScoringTests
    {
        [Fact]
        public void PlaceAversion_NoDanger_IsNeutral()
            => Assert.Equal(1.0, OddSystem.PlaceAversion(0.0), 6);

        [Fact]
        public void PlaceAversion_FullDanger_HitsFloor()
            => Assert.Equal(0.1, OddSystem.PlaceAversion(1.0), 6);   // DangerFloor: avoided hard, never zero

        [Fact]
        public void PlaceAversion_MonotoneDecreasing_AndClamped()
        {
            Assert.True(OddSystem.PlaceAversion(0.5) < OddSystem.PlaceAversion(0.2));
            Assert.True(OddSystem.PlaceAversion(0.9) < OddSystem.PlaceAversion(0.5));
            Assert.True(OddSystem.PlaceAversion(2.0) >= 0.1);    // clamps below floor
            Assert.True(OddSystem.PlaceAversion(-1.0) <= 1.0);   // clamps above 1
        }

        [Fact]
        public void ProvisionPreference_Full_Neutral_Empty_GentlyPenalized()
        {
            Assert.Equal(1.0, OddSystem.ProvisionPreference(1.0), 6);
            Assert.True(OddSystem.ProvisionPreference(0.0) < 1.0);
            Assert.True(OddSystem.ProvisionPreference(0.0) >= 0.5);   // gentle, not a hard gate
            Assert.True(OddSystem.ProvisionPreference(0.5) > OddSystem.ProvisionPreference(0.0));
        }
    }
}
