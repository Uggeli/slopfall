using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ReputationCueTests
    {
        // Build the registry set Interpret needs; seed the perceiver's learned belief about the kind.
        static EntityRead Read(double learnedValence, double confidence)
        {
            var e = new EventBus();
            var relations = new RelationsRegistry(e);
            var affects = new AffectsRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var personality = new PersonalityRegistry(e);
            var perceivable = new PerceivableRegistry(e);
            var agentMem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var self = new EntityId(1); var other = new EntityId(2);

            // `other` is perceivable ONLY as a neutral Drifter — NO weapon/form atoms (form-threat = 0).
            perceivable.Seed(other, AtomName.Drifter.ToId(), Fixed.One);
            var sig = AtomBag.Create(new[] { new Atom(AtomName.Drifter.ToId(), Fixed.One) });
            agentMem.Seed(self);
            agentMem.SeedInnatePrior(self, sig, Fixed.FromDouble(learnedValence), Fixed.FromDouble(confidence));
            e.Tick(); perceivable.Update(0); agentMem.Update(0);

            return SubjectiveSystem.Interpret(relations, affects, behavior, personality, perceivable, agentMem, self, other);
        }

        [Fact]
        public void AversiveLearnedKind_ReadsAsThreat_WithNoFormAtoms()
        {
            // a believed-dangerous drifter reads as a THREAT, sourced purely from the learned belief
            // (no weapon/form atoms, so any Threat > 0 is 100% the reputation cue)
            Assert.True(Read(-0.8, 0.9).Threat > 0.0);
        }

        [Fact]
        public void NeutralLearnedKind_ReadsNoThreat()
        {
            // a not-yet-learned drifter is read as harmless — no form-threat, no reputation threat
            Assert.Equal(0.0, Read(0.0, 0.3).Threat);
        }

        [Fact]
        public void ReputationThreat_ScalesWithConfidence()
        {
            // the same aversion, half-believed, reads as less of a threat
            Assert.True(Read(-0.8, 0.2).Threat < Read(-0.8, 0.9).Threat);
        }
    }
}
