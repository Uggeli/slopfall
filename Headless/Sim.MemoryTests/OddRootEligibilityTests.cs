using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddRootEligibilityTests
    {
        // A stock-empty sale ad is NOT root-eligible (don't pursue it standalone)...
        [Fact]
        public void StockEmptySaleAd_NotRootEligible()
            => Assert.False(OddSystem.IsRootEligible(coin: 10, saleCost: 1, larderGated: false, larder: 0, inStock: false));

        // ...but a stocked, affordable one IS.
        [Fact]
        public void StockedAffordableSaleAd_IsRootEligible()
            => Assert.True(OddSystem.IsRootEligible(coin: 10, saleCost: 1, larderGated: false, larder: 0, inStock: true));

        // Broke agent: a coin-sink ad is not a root.
        [Fact]
        public void Broke_SaleAd_NotRootEligible()
            => Assert.False(OddSystem.IsRootEligible(coin: 0, saleCost: 1, larderGated: false, larder: 0, inStock: true));

        // Larder-empty home meal is not a root.
        [Fact]
        public void EmptyLarder_HomeMeal_NotRootEligible()
            => Assert.False(OddSystem.IsRootEligible(coin: 0, saleCost: 0, larderGated: true, larder: 0, inStock: true));

        // Non-sale, non-larder ad (e.g. Work) is always root-eligible.
        [Fact]
        public void PlainAd_IsRootEligible()
            => Assert.True(OddSystem.IsRootEligible(coin: 0, saleCost: 0, larderGated: false, larder: 0, inStock: true));
    }
}
