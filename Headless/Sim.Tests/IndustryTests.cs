using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Industry layers (docs/industry_layers.md): the recipe graph — producers
    /// consume inputs to make outputs, must source the inputs they don't make, and
    /// value climbs the chain. Wool → Cloth → Clothes is the proof chain.
    public class IndustryTests
    {
        [Fact]
        public void ClothChain_Recipes_Sourcing_AndPriceLadder()
        {
            // Pasture: primary — wool from the land, no inputs, sources nothing.
            Assert.True(GoodsCatalog.IsPrimaryWorkplace(BuildingKind.Pasture));
            Assert.Contains(Good.Wool, GoodsCatalog.Produces(BuildingKind.Pasture));
            Assert.Empty(GoodsCatalog.Inputs(BuildingKind.Pasture));
            Assert.Empty(GoodsCatalog.B2BNeeds(BuildingKind.Pasture));

            // Weaver: wool → cloth; must source wool B2B.
            Assert.Contains(Good.Cloth, GoodsCatalog.Produces(BuildingKind.Weaver));
            Assert.Contains(Good.Wool, GoodsCatalog.Inputs(BuildingKind.Weaver));
            Assert.Contains(Good.Wool, GoodsCatalog.B2BNeeds(BuildingKind.Weaver));

            // ClothingStore: cloth → clothes (no longer the Wares monolith); sources cloth B2B.
            Assert.Contains(Good.Clothes, GoodsCatalog.Produces(BuildingKind.ClothingStore));
            Assert.Contains(Good.Cloth, GoodsCatalog.Inputs(BuildingKind.ClothingStore));
            Assert.Contains(Good.Cloth, GoodsCatalog.B2BNeeds(BuildingKind.ClothingStore));

            // Value compounds up the tiers: wool < cloth < clothes.
            Assert.True(GoodsCatalog.PriceOf(Good.Cloth, PriceTier.Wholesale)
                      > GoodsCatalog.PriceOf(Good.Wool, PriceTier.Wholesale));
            Assert.True(GoodsCatalog.PriceOf(Good.Clothes, PriceTier.Wholesale)
                      > GoodsCatalog.PriceOf(Good.Cloth, PriceTier.Wholesale));
        }

        [Fact]
        public void Weaver_MakesCloth_FromWool_AndStopsWhenItRunsOut()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int weaver = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Weaver });
            h.Ctx.Stock.Set(weaver, Good.Wool, 10);

            var keeper = h.SpawnEntity("Weaver");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = weaver, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 1.0);
            h.Ctx.Needs.Set(keeper, new NeedsData());
            h.Ctx.Behavior.Set(keeper, new BehaviorData
            {
                Activity = ActivityKind.Work, Phase = ActivityPhase.Doing,
                TargetBuilding = weaver, RemainingGameMinutes = 100000,
            });
            // Hands at the loom — a staffed workshop's output scales with them (the lone
            // keeper just runs it; the laborers do the weaving).
            for (int i = 0; i < 3; i++)
                h.Ctx.Behavior.Set(h.SpawnEntity("Hand" + i), new BehaviorData
                {
                    Activity = ActivityKind.Labor, Phase = ActivityPhase.Doing,
                    TargetBuilding = weaver, RemainingGameMinutes = 100000,
                });
            h.SeedClock(hour: 12, timeScale: 600f);

            Assert.Equal(0, h.Ctx.Stock.Get(weaver, Good.Cloth), 9);
            h.Step(8);
            // Wool is spun into cloth — output appears, input is drawn down (the recipe).
            Assert.True(h.Ctx.Stock.Get(weaver, Good.Cloth) > 0, "weaver made no cloth from its wool");
            Assert.True(h.Ctx.Stock.Get(weaver, Good.Wool) < 10, "weaver didn't consume wool");
        }

        [Fact]
        public void Weaver_WithNoWool_MakesNoCloth()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            int weaver = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Weaver });
            // No wool, and no pasture to source it from.
            var keeper = h.SpawnEntity("Weaver");
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = weaver, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 1.0);
            h.Ctx.Needs.Set(keeper, new NeedsData());
            h.Ctx.Behavior.Set(keeper, new BehaviorData
            {
                Activity = ActivityKind.Work, Phase = ActivityPhase.Doing,
                TargetBuilding = weaver, RemainingGameMinutes = 100000,
            });
            for (int i = 0; i < 3; i++)
                h.Ctx.Behavior.Set(h.SpawnEntity("Hand" + i), new BehaviorData
                {
                    Activity = ActivityKind.Labor, Phase = ActivityPhase.Doing,
                    TargetBuilding = weaver, RemainingGameMinutes = 100000,
                });
            h.SeedClock(hour: 12, timeScale: 600f);

            h.Step(8);
            Assert.Equal(0, h.Ctx.Stock.Get(weaver, Good.Cloth), 9);   // no input → no output (even fully staffed)
        }
    }
}
