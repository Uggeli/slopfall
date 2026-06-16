using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// S3 MEANINGS gates (docs/cognitive_substrate_S3_meanings.md). The test that
    /// matters: does experience fold into a learned CATEGORY, and does interpret()
    /// apply that category to a STRANGER of the same kind? (System wired + entities
    /// use it.) Magnitudes are frozen placeholders.
    public class MeaningsTests
    {
        [Fact]
        public void RepeatedRefusals_TeachAnAversiveCategory_AppliedToStrangersOfThatKind()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var learner = h.SpawnEntity("Learner");
            var keeperA = h.SpawnEntity("KeeperA");
            var keeperB = h.SpawnEntity("KeeperB");   // a DIFFERENT keeper the learner never meets
            h.Ctx.Residency.Set(keeperA, new ResidencyData { BuildingIndex = 1, Role = ResidentRole.Keeper });
            h.Ctx.Residency.Set(keeperB, new ResidencyData { BuildingIndex = 2, Role = ResidentRole.Keeper });
            h.SeedClock(hour: 12, timeScale: 60f);
            h.Step(1);

            // The learner is turned away by keeperA over and over → learns that
            // keepers (the KIND) are not to be counted on.
            for (int i = 0; i < 20; i++)
            {
                h.Ctx.Events.Emit(new HelpRefusedEvent { Asker = learner, Refuser = keeperA });
                h.Step(2);
            }

            // The category was learned (entities use MeaningsSystem).
            Assert.True(h.Ctx.Meanings.TryGet(learner, out var m), "no Meanings for learner — system not wired/used");
            Assert.True(m.Nodes.TryGetValue((int)ResidentRole.Keeper, out var node), "no keeper category learned");
            Assert.True(node.Valence < 0, "keeper category not aversive: " + node.Valence);

            // And it is APPLIED to a STRANGER of that kind: the learner never met
            // keeperB, yet interpret() reads them through the learned stereotype.
            Assert.True(SubjectiveSystem.Interpret(h.Ctx, learner, keeperB).Valence < 0,
                "learned category not applied to a stranger keeper");
        }

        [Fact]
        public void UnknownKind_WithNoLearning_ReadsNeutral()
        {
            var h = new SimHarness();
            var a = new EntityId(1);
            var stranger = new EntityId(2);
            // No dossier, no learned category → a blank slate reads neutral.
            Assert.Equal(0.0, SubjectiveSystem.Interpret(h.Ctx, a, stranger).Valence, 6);
        }
    }
}
