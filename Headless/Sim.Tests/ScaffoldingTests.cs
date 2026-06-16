using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Tier-4 unit tests: the E0b data shapes are deterministic tables, so exact
    /// values ARE the contract here (unlike emergent behavior). The parity test
    /// in particular guards against DriveCatalog and ActivityCatalog drifting
    /// apart once the pipeline reads from DriveCatalog.
    public class ScaffoldingTests
    {
        [Fact]
        public void DriveCatalog_MatchesActivityCatalogConstants()
        {
            for (int axis = 0; axis < NeedAxis.Count; axis++)
            {
                Assert.Equal(ActivityCatalog.Weights[axis], DriveCatalog.Defs[axis].Weight, 6);
                Assert.Equal(ActivityCatalog.DriftPerHour[axis], DriveCatalog.Defs[axis].DriftPerHour, 6);
            }
            // CoinDef is the one derived (not depleting) axis.
            Assert.Equal(SatisfactionModel.Derived, DriveCatalog.Defs[NeedAxis.CoinDef].Satisfaction);
            Assert.Equal(SatisfactionModel.Deplete, DriveCatalog.Defs[NeedAxis.Hunger].Satisfaction);
            // Hunger/energy are the prepotent deficiency drives (gate leisure).
            Assert.True(DriveCatalog.Defs[NeedAxis.Hunger].Prepotent);
            Assert.True(DriveCatalog.Defs[NeedAxis.EnergyDef].Prepotent);
            Assert.False(DriveCatalog.Defs[NeedAxis.SocialDef].Prepotent);
        }

        [Fact]
        public void AffordanceCatalog_AdvertisesExpectedVerbs()
        {
            Assert.Contains(ActivityKind.EatTavern, AffordanceCatalog.Public(BuildingKind.Tavern));
            Assert.Contains(ActivityKind.Socialize, AffordanceCatalog.Public(BuildingKind.Tavern));
            Assert.Equal(new[] { ActivityKind.Visit, ActivityKind.Beg }, AffordanceCatalog.Public(BuildingKind.Temple));
            Assert.Empty(AffordanceCatalog.Public(BuildingKind.House1));   // homes are agent-relative, not public
            Assert.Contains(ActivityKind.Sleep, AffordanceCatalog.Home);
            Assert.Contains(ActivityKind.Work, AffordanceCatalog.Workplace);
        }

        [Fact]
        public void SpecFor_ResolvesVerbsToSpecs()
        {
            Assert.Same(ActivityCatalog.Sleep, ActivityCatalog.SpecFor(ActivityKind.Sleep));
            Assert.Same(ActivityCatalog.EatTavern, ActivityCatalog.SpecFor(ActivityKind.EatTavern));
            Assert.Null(ActivityCatalog.SpecFor(ActivityKind.None));
        }

        [Fact]
        public void PlaceMemory_LearnsRecallsAndDedupes()
        {
            var reg = new PlaceMemoryRegistry();
            var id = new EntityId(1);
            Assert.False(reg.Knows(id, 5));
            reg.Learn(id, 5);
            reg.Learn(id, 5);          // dup
            reg.Learn(id, 9);
            reg.Learn(id, -1);         // "nowhere" is never learned
            Assert.True(reg.Knows(id, 5));
            Assert.True(reg.Knows(id, 9));
            Assert.False(reg.Knows(id, -1));
            Assert.Equal(2, reg.CountFor(id));
        }
    }
}
