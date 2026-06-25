using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class InnatePriorsTests
    {
        [Fact]
        public void Civilian_HasOneInGroupPrior_OthersEmpty()
        {
            var civ = InnatePriors.For(EntityKind.CivilianNPC);
            Assert.Single(civ);
            Assert.Equal(PriorTarget.Self, civ[0].Target);
            Assert.True(civ[0].Valence.ToDouble() > 0.0);            // in-group warmth
            Assert.True(civ[0].Confidence.ToDouble() > 0.0);
            Assert.Empty(InnatePriors.For(EntityKind.EnemyMonster)); // monster prior deferred to Phase D
        }

        [Fact]
        public void SeedInnatePrior_WritesARecognizableNode()
        {
            var e = new EventBus();
            var mem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var agent = new EntityId(1);
            mem.Seed(agent);
            var sig = AtomBag.Create(new[] { new Atom(AtomName.Civilian.ToId(), Fixed.One) });

            mem.SeedInnatePrior(agent, sig, Fixed.FromDouble(0.2), Fixed.FromDouble(0.3));

            Assert.True(mem.TryGet(agent, out var m));
            Assert.True(m.Meanings.RecognizedValence(sig, out var v, out _));
            Assert.True(v.ToDouble() > 0.0);
        }
    }
}
