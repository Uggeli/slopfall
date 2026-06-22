using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryReinforceSystemTests
    {
        [Fact]
        public void HelpGranted_EmitsPositiveReinforce_ForGiversSignature()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryReinforceSystem(e, perceivable);
            var asker = new EntityId(1); var giver = new EntityId(2);
            perceivable.Seed(giver, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);

            e.Publish(new HelpGrantedEvent { Asker = asker, Giver = giver, Amount = 1 });
            e.Tick(); perceivable.Update(0); sys.Update(0);
            e.Tick();

            var emitted = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Single(emitted);
            Assert.Equal(asker, emitted[0].Perceiver);
            Assert.True(emitted[0].Outcome.ToDouble() > 0);   // grant = positive
            Assert.Contains(emitted[0].Signature.Atoms, a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
        }

        [Fact]
        public void Refused_IsNegative_Greeting_IsBothWays()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryReinforceSystem(e, perceivable);
            foreach (var id in new[] { 1, 2, 3, 4 })
                perceivable.Seed(new EntityId(id), PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);

            e.Publish(new HelpRefusedEvent { Asker = new EntityId(1), Refuser = new EntityId(2) });
            e.Publish(new GreetingEvent { A = new EntityId(3), B = new EntityId(4) });
            e.Tick(); perceivable.Update(0); sys.Update(0);
            e.Tick();

            var emitted = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Contains(emitted, x => x.Perceiver.Value == 1 && x.Outcome.ToDouble() < 0);   // refused
            Assert.Contains(emitted, x => x.Perceiver.Value == 3 && x.Outcome.ToDouble() > 0);   // greet A<-B
            Assert.Contains(emitted, x => x.Perceiver.Value == 4 && x.Outcome.ToDouble() > 0);   // greet B<-A
        }
    }
}
