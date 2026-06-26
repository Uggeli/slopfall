using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class DamageReinforceTests
    {
        static AtomBag MonsterSig()
            => AtomBag.Create(new[] { new Atom(AtomName.Beast.ToId(), Fixed.One) });

        [Fact]
        public void Damage_EmitsNegativeReinforce_ForAttackersKind()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryReinforceSystem(e, perceivable);
            var victim = new EntityId(1); var monster = new EntityId(2);
            perceivable.Seed(monster, AtomName.Beast.ToId(), Fixed.One);

            e.Publish(new DamageEvent { Target = victim, Source = monster, Amount = 5, Type = DamageType.Physical });
            e.Tick(); perceivable.Update(0); sys.Update(0);
            e.Tick();

            var emitted = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Single(emitted);
            Assert.Equal(victim, emitted[0].Perceiver);                        // the VICTIM learns
            Assert.True(emitted[0].Outcome.ToDouble() < 0.0);                  // hurt = aversive
            Assert.Contains(emitted[0].Signature.Atoms,
                a => a.Type.Value == AtomName.Beast.ToId().Value);  // about the monster KIND
        }

        [Fact]
        public void Damage_DriftsTheVictimsSeededMonsterCategory_MoreNegative()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var agentMem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var sys = new MemoryReinforceSystem(e, perceivable);
            var victim = new EntityId(1); var monster = new EntityId(2);

            // the monster is perceivable as its kind (Beast); the victim is born with the innate monster prior
            perceivable.Seed(monster, AtomName.Beast.ToId(), Fixed.One);
            agentMem.Seed(victim);
            agentMem.SeedInnatePrior(victim, MonsterSig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));
            agentMem.TryGet(victim, out var mem);
            Assert.True(mem.Meanings.RecognizedValence(MonsterSig(), out var before, out _));

            // a monster attacks → the reinforce intent flows and is applied
            e.Publish(new DamageEvent { Target = victim, Source = monster, Amount = 5, Type = DamageType.Physical });
            e.Tick(); perceivable.Update(0); sys.Update(0);     // system emits MemoryReinforceIntent
            e.Tick(); agentMem.Update(1);                       // registry applies it → Reinforce

            mem.Meanings.RecognizedValence(MonsterSig(), out var after, out _);
            Assert.True(after.ToDouble() < before.ToDouble());  // "this kind hurt me" — drifted toward -1.0
        }
    }
}
