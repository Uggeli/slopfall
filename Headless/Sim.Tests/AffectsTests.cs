using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// S2 emotion gates (docs/cognitive_substrate_S2_emotion.md). The test that
    /// matters: is AffectsSystem wired, do entities acquire DIRECTED emotions from
    /// interactions, and does the acute feeling color interpret() (emotion-as-
    /// controller)? Magnitudes are frozen placeholders — not asserted.
    public class AffectsTests
    {
        [Fact]
        public void Help_MintsGratitude_AndColorsInterpret()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var asker = h.SpawnEntity("Asker");
            var giver = h.SpawnEntity("Giver");
            h.SeedClock(hour: 12, timeScale: 60f);
            h.Step(1);                                  // seed the clock

            // A help happened — AffectsSystem turns it into directed gratitude.
            h.Ctx.Events.Emit(new HelpGrantedEvent { Asker = asker, Giver = giver, Amount = 0.25 });
            h.Step(4);                                  // collect → mint affect + regard impulse → apply

            Assert.True(h.Ctx.Affects.TryGet(asker, out var af), "no Affects for asker — system not wired/used");
            Assert.Contains(af.Active, x => x.Target == giver && x.Kind == AffectKind.Gratitude);

            // Emotion-as-controller: the acute gratitude colors how the asker reads
            // the giver, on top of chronic regard — the read flows to the decider.
            Assert.True(SubjectiveSystem.Interpret(h.Ctx, asker, giver).Valence > 0,
                "gratitude didn't color the read");
        }

        [Fact]
        public void Refusal_MintsResentment_ButOnlyMildly()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var asker = h.SpawnEntity("Asker");
            var refuser = h.SpawnEntity("Refuser");
            h.SeedClock(hour: 12, timeScale: 60f);
            h.Step(1);

            h.Ctx.Events.Emit(new HelpRefusedEvent { Asker = asker, Refuser = refuser });
            h.Step(4);

            Assert.True(h.Ctx.Affects.TryGet(asker, out var af), "no Affects for asker");
            Assert.Contains(af.Active, x => x.Target == refuser && x.Kind == AffectKind.Resentment);

            // Routine begging-refusal is low-stakes (S2): the read is negative but
            // mild — broadcast begging no longer mass-sours the town.
            double v = SubjectiveSystem.Interpret(h.Ctx, asker, refuser).Valence;
            Assert.True(v < 0 && v > -0.5, "refusal read not mildly negative: " + v);
        }
    }
}
