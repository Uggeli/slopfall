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
        public void Civilian_HasInGroupAndMonsterPriors()
        {
            var civ = InnatePriors.For(EntityKind.CivilianNPC);
            Assert.Equal(2, civ.Length);
            Assert.Contains(civ, p => p.Target == PriorTarget.Self && p.Valence.ToDouble() > 0.0);  // in-group warmth
            var monster = civ.Single(p => p.Target == PriorTarget.Kind);
            Assert.Equal(AtomName.Beast, monster.Kind);
            Assert.True(monster.Valence.ToDouble() < 0.0);            // innate wariness of beast-kind
            Assert.True(monster.Confidence.ToDouble() > 0.0);
            Assert.Empty(InnatePriors.For(EntityKind.EnemyMonster));  // monsters don't socially interpret
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
