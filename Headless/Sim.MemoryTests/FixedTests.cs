using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class FixedTests
    {
        [Fact]
        public void Scale_Is256_EightFractionalBits()
        {
            Assert.Equal(8, Fixed.FractionalBits);
            Assert.Equal(256, Fixed.Scale);
        }

        [Fact]
        public void Zero_And_One_HaveExpectedRaw()
        {
            Assert.Equal(0, Fixed.Zero.Raw);
            Assert.Equal(256, Fixed.One.Raw);
        }

        [Fact]
        public void FromInt_ScalesByWholeUnits()
        {
            Assert.Equal(256, Fixed.FromInt(1).Raw);
            Assert.Equal(768, Fixed.FromInt(3).Raw);
            Assert.Equal(-512, Fixed.FromInt(-2).Raw);
        }

        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(1.0, 256)]
        [InlineData(0.5, 128)]
        [InlineData(0.9, 230)]      // 0.9*256 = 230.4 -> 230 (round to nearest)
        [InlineData(-0.5, -128)]
        [InlineData(-0.9, -230)]    // ties-away-from-zero on the negative side
        public void FromDouble_RoundsToNearest(double v, int expectedRaw)
        {
            Assert.Equal(expectedRaw, Fixed.FromDouble(v).Raw);
        }

        [Fact]
        public void ToDouble_RoundTripsWithinResolution()
        {
            var f = Fixed.FromDouble(0.9);
            Assert.True(System.Math.Abs(f.ToDouble() - 0.9) <= 1.0 / 256);
        }

        [Fact]
        public void Addition_And_Subtraction_AreRawInteger()
        {
            var a = Fixed.FromDouble(0.5);
            var b = Fixed.FromDouble(0.25);
            Assert.Equal(Fixed.FromDouble(0.75), a + b);
            Assert.Equal(Fixed.FromDouble(0.25), a - b);
            Assert.Equal(Fixed.FromDouble(-0.5), -a);
        }

        [Fact]
        public void Equality_ByRaw()
        {
            Assert.True(Fixed.FromInt(2) == new Fixed(512));
            Assert.True(Fixed.FromInt(2) != Fixed.FromInt(3));
            Assert.Equal(Fixed.FromInt(2), new Fixed(512));
            Assert.Equal(Fixed.FromInt(2).GetHashCode(), new Fixed(512).GetHashCode());
        }
    }
}
