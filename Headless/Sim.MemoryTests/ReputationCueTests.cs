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

            // `other` is perceivable ONLY as a neutral Drifter — NO weapon/form atoms (form-threat ≈ 0).
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
            var feared = Read(-0.8, 0.9);
            Assert.True(feared.Threat > 0.0);     // a believed-dangerous drifter reads as a THREAT...

            var neutral = Read(0.0, 0.3);
            Assert.Equal(0.0, neutral.Threat);    // ...while a not-yet-learned drifter does not

            var tentative = Read(-0.8, 0.2);
            Assert.True(tentative.Threat < feared.Threat);   // fear scales with confidence
        }
    }
}
