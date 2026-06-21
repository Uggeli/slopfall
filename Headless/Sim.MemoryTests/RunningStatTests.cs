using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class RunningStatTests
    {
        [Fact]
        public void Empty_HasZeroCount_MeanZero_VarianceZero()
        {
            var s = new RunningStat();
            Assert.Equal(0, s.Count);
            Assert.Equal(Fixed.Zero, s.Mean());
            Assert.Equal(0, s.VarianceRaw());
        }

        [Fact]
        public void Add_AccumulatesCountSumSumSq()
        {
            var s = new RunningStat();
            s.Add(Fixed.FromDouble(1.0));   // raw 256
            s.Add(Fixed.FromInt(0));        // raw 0
            Assert.Equal(2, s.Count);
            Assert.Equal(256, s.Sum);
            Assert.Equal(65536, s.SumSq);   // 256^2 + 0
        }

        [Fact]
        public void Mean_OfConstantStream_IsThatValue()
        {
            var s = new RunningStat();
            for (int i = 0; i < 5; i++) s.Add(Fixed.FromDouble(0.5));
            Assert.Equal(Fixed.FromDouble(0.5), s.Mean());
        }

        [Fact]
        public void Variance_OfConstantStream_IsZero()
        {
            var s = new RunningStat();
            for (int i = 0; i < 5; i++) s.Add(Fixed.FromDouble(0.5));
            Assert.Equal(0, s.VarianceRaw());
        }

        [Fact]
        public void Variance_OfSingleSample_IsZero()
        {
            var s = new RunningStat();
            s.Add(Fixed.FromDouble(0.9));
            Assert.Equal(0, s.VarianceRaw());
        }

        [Fact]
        public void Variance_OfZeroAndOne_IsQuarter()
        {
            // samples {0.0, 1.0}: population variance = 0.25 -> Q16 raw = 0.25 * 65536 = 16384
            var s = new RunningStat();
            s.Add(Fixed.FromInt(0));
            s.Add(Fixed.FromInt(1));
            Assert.Equal(Fixed.FromDouble(0.5), s.Mean());
            Assert.Equal(16384, s.VarianceRaw());
        }

        [Fact]
        public void Variance_FractionalQ8Mean_IsApproximateButNonNegative()
        {
            // raws {0, 0, 256}: Sum=256, Count=3, meanRaw=85 (trunc), meanSq=7225,
            // SumSq=65536, SumSq/Count=21845 (trunc) -> v = 14620. True pop-variance of
            // {0,0,1.0} is 2/9 ≈ 0.2222 -> Q16 ≈ 14563; the integer mean truncation makes
            // the result approximate, but it stays positive (never trips the clamp).
            var s = new RunningStat();
            s.Add(Fixed.FromInt(0));
            s.Add(Fixed.FromInt(0));
            s.Add(Fixed.FromInt(1));
            Assert.Equal(14620, s.VarianceRaw());
            Assert.True(s.VarianceRaw() >= 0);
        }

        [Fact]
        public void LowSpread_StreamHasSmallVariance_HighSpread_HasLarge()
        {
            var low = new RunningStat();
            foreach (var v in new[] { 0.50, 0.51, 0.49, 0.50 }) low.Add(Fixed.FromDouble(v));

            var high = new RunningStat();
            foreach (var v in new[] { 0.05, 0.95, 0.10, 0.90 }) high.Add(Fixed.FromDouble(v));

            Assert.True(low.VarianceRaw() < high.VarianceRaw());
        }
    }
}
