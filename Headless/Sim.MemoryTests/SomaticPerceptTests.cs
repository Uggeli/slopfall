using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SomaticPerceptTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly NeedsRegistry Needs;
            public readonly PerceivableRegistry Perceivable;
            public readonly SomaticPerceptSystem System;
            public Rig()
            {
                Needs = new NeedsRegistry(E);
                Perceivable = new PerceivableRegistry(E);
                System = new SomaticPerceptSystem(E, Needs);
            }
            public void SetHunger(int id, double v)
            {
                var d = new NeedsData();
                d.V[NeedAxis.Hunger] = v;
                E.Publish(new NeedsSetIntent { Id = new EntityId(id), Data = d });
            }
            public void Step(long t) { E.Tick(); Needs.Update(t); Perceivable.Update(t); System.Update(t); }
        }

        [Fact]
        public void HungerNeed_IsStampedIntoOwnBag()
        {
            var r = new Rig();
            r.SetHunger(1, 0.7);
            r.Step(0);     // needs applied; system stamps at tick % SenseEveryTicks == 0
            r.Step(1);     // perceivable applies the StampAtomIntent published on the previous step

            var bag = r.Perceivable.Bag(new EntityId(1));
            Assert.True(bag.TryGet(SomaticAtoms.Hunger, out var v));
            Assert.True(v.Raw > Fixed.Zero.Raw);
        }

        [Fact]
        public void Hunger_Cleared_WhenNeedReturnsToZero()
        {
            var r = new Rig();
            r.SetHunger(1, 0.7);
            r.Step(0); r.Step(1);                                   // stamped
            Assert.True(r.Perceivable.Bag(new EntityId(1)).Contains(SomaticAtoms.Hunger));

            r.SetHunger(1, 0.0);                                    // need recovered
            for (long t = 2; t < 14 && r.Perceivable.Bag(new EntityId(1)).Contains(SomaticAtoms.Hunger); t++) // step past the next sense-tick (bound > SenseEveryTicks)
                r.Step(t);                                          // step past the next sense-tick; clear publishes + applies
            Assert.False(r.Perceivable.Bag(new EntityId(1)).Contains(SomaticAtoms.Hunger));
        }
    }
}
