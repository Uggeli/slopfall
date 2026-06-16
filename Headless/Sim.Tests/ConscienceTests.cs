using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// S4 conscience gates (docs/cognitive_substrate_S4_conscience.md). The live
    /// behaviors: SHAME (a charge on a verb penalizes it at the marketplace, so a
    /// proud agent avoids begging) and STIGMA (others read a beggar through their
    /// own disposition — disdain vs pity). Magnitudes are frozen placeholders.
    public class ConscienceTests
    {
        static double[] Warmth(double w)
        {
            var t = new double[TraitIndex.Count];
            for (int i = 0; i < t.Length; i++) t[i] = 0.5;
            t[TraitIndex.Warmth] = w;
            return t;
        }

        [Fact]
        public void ShameFactor_PenalizesAShamedVerb_ProportionalToPride()
        {
            Assert.Equal(1.0, OddSystem.ConscienceFactor(0.0), 6);                  // no qualm → no penalty
            Assert.True(OddSystem.ConscienceFactor(0.8) < 0.5, "proud not penalized");
            Assert.True(OddSystem.ConscienceFactor(0.8) < OddSystem.ConscienceFactor(0.2), "not proportional to pride");
            Assert.True(OddSystem.ConscienceFactor(2.0) >= 0.0, "not bounded (would cull/flip)");
        }

        [Fact]
        public void Conscience_Charge_IsPerAgentPerVerb()
        {
            var h = new SimHarness();
            var proud = new EntityId(1);
            var data = new ConscienceData();
            data.Charge[(int)ActivityKind.Beg] = 0.7;
            h.Ctx.Conscience.Set(proud, data);

            Assert.Equal(0.7, h.Ctx.Conscience.ChargeFor(proud, ActivityKind.Beg), 6);
            Assert.Equal(0.0, h.Ctx.Conscience.ChargeFor(proud, ActivityKind.Work), 6);          // no qualm about working
            Assert.Equal(0.0, h.Ctx.Conscience.ChargeFor(new EntityId(2), ActivityKind.Beg), 6); // unseeded → none
        }

        [Fact]
        public void Beggar_StigmatizedByTheCold_PitiedByTheWarm()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var beggar = h.SpawnEntity("Beggar");
            var cold = h.SpawnEntity("Cold");
            var warm = h.SpawnEntity("Warm");
            h.Ctx.Behavior.Set(beggar, new BehaviorData
            {
                Activity = ActivityKind.Beg, Phase = ActivityPhase.Doing,
                TargetBuilding = -1, RemainingGameMinutes = 100000,
            });
            h.Ctx.Personality.Set(cold, PersonalityData.Derive(Warmth(0.1)));
            h.Ctx.Personality.Set(warm, PersonalityData.Derive(Warmth(0.9)));

            // Same beggar, opposite read — stigma vs pity, from the membrane (S1)
            // reading the beggar's molecule through the perceiver's disposition.
            double coldRead = SubjectiveSystem.Interpret(h.Ctx, cold, beggar).Valence;
            double warmRead = SubjectiveSystem.Interpret(h.Ctx, warm, beggar).Valence;
            Assert.True(coldRead < warmRead, "cold doesn't read the beggar more harshly: cold=" + coldRead + " warm=" + warmRead);
            Assert.True(coldRead < 0, "the cold don't disdain the beggar: " + coldRead);
        }
    }
}
