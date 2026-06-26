using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ReinforceScaleTests
    {
        static AtomBag Sig() => AtomBag.Create(new[] { new Atom(AtomName.EnemyMonster.ToId(), Fixed.One) });

        [Fact]
        public void Reinforce_Scaled_MovesLessThanFullTrust()
        {
            var a = new MeaningsStore(8, MeaningsConfig.Default);
            var b = new MeaningsStore(8, MeaningsConfig.Default);
            var ida = a.SeedInnate(Sig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));
            var idb = b.SeedInnate(Sig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));

            a.Reinforce(ida, Sig(), Fixed.FromDouble(-1.0), Fixed.One);             // full trust
            b.Reinforce(idb, Sig(), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.5)); // half trust

            a.RecognizedValence(Sig(), out var va, out _);
            b.RecognizedValence(Sig(), out var vb, out _);
            Assert.True(va.ToDouble() < vb.ToDouble());   // full-trust moved further toward -1.0
            Assert.True(vb.ToDouble() < -0.6);            // half-trust still moved (min-step keeps it un-stalled)
        }

        [Fact]
        public void ReinforceIntent_Scale_IsAppliedByTheRegistry()
        {
            var e = new EventBus();
            var mem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var agent = new EntityId(1);
            mem.Seed(agent);
            mem.SeedInnatePrior(agent, Sig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));

            e.Publish(new MemoryReinforceIntent
            { Perceiver = agent, Signature = Sig(), Outcome = Fixed.FromDouble(-1.0), Scale = Fixed.FromDouble(0.5) });
            e.Tick(); mem.Update(0);

            mem.TryGet(agent, out var m);
            m.Meanings.RecognizedValence(Sig(), out var v, out _);
            Assert.True(v.ToDouble() < -0.6);   // the scaled second-hand intent reinforced (deepened)
        }
    }
}
