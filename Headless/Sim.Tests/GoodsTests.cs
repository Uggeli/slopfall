using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// G1: goods are an authored data table (the prices ARE the contract, like
    /// the other E0b catalogs) and a per-building stock registry. No behavior
    /// rides on them yet — supply (G2) and purchases (G3) come next — so these
    /// pin the foundation the rest of the goods economy hangs on.
    public class GoodsTests
    {
        [Fact]
        public void Prices_RiseAlongTheChain()
        {
            foreach (Good g in new[] { Good.Provisions, Good.Drink, Good.Wares })
            {
                double import = GoodsCatalog.PriceOf(g, PriceTier.Import);
                double wholesale = GoodsCatalog.PriceOf(g, PriceTier.Wholesale);
                double retail = GoodsCatalog.PriceOf(g, PriceTier.Retail);
                Assert.True(import > 0, g + " import price must be positive");
                Assert.True(import < wholesale, g + " import !< wholesale");
                Assert.True(wholesale < retail, g + " wholesale !< retail");
            }
        }

        [Fact]
        public void Stocks_MapsBuildingsToTheGoodsTheySell()
        {
            Assert.Contains(Good.Provisions, GoodsCatalog.Stocks(BuildingKind.Tavern));
            Assert.Contains(Good.Drink, GoodsCatalog.Stocks(BuildingKind.Tavern));
            Assert.Equal(new[] { Good.Wares }, GoodsCatalog.Stocks(BuildingKind.WeaponSmith));
            Assert.Empty(GoodsCatalog.Stocks(BuildingKind.Temple));   // services, not goods (G5)
            Assert.Empty(GoodsCatalog.Stocks(BuildingKind.House1));   // homes hold no shop stock
        }

        [Fact]
        public void StockRegistry_AddsAndDrawsDownClampedAtZero()
        {
            var reg = new StockRegistry();
            Assert.Equal(0, reg.Get(3, Good.Wares));
            reg.Set(3, Good.Wares, 5);
            Assert.Equal(5, reg.Get(3, Good.Wares));
            Assert.Equal(7, reg.Add(3, Good.Wares, 2));
            Assert.Equal(0, reg.Add(3, Good.Wares, -100));   // a draw can't go negative
            // Goods are independent slots on the same building.
            Assert.Equal(0, reg.Get(3, Good.Provisions));
        }
    }
}
