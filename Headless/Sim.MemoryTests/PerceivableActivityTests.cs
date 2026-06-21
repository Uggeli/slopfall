using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PerceivableActivityTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly BehaviorRegistry Behavior;
            public readonly PerceivableRegistry Perceivable;
            public readonly PerceivableActivitySystem System;

            public Rig()
            {
                Behavior = new BehaviorRegistry(E);
                Perceivable = new PerceivableRegistry(E);
                System = new PerceivableActivitySystem(E, Behavior, Perceivable);
            }

            // One engine step: flip events, run the registry write phase, then the system read phase.
            public void Step()
            {
                E.Tick();
                Behavior.Update(0);
                Perceivable.Update(0);
                System.Update(0);
            }

            public void SetActivity(EntityId id, ActivityKind a)
                => E.Publish(new BehaviorSetIntent { Id = id, Data = new BehaviorData { Activity = a } });

            public int[] ActivityAtoms(EntityId id)
                => Perceivable.Bag(id).Atoms
                    .Where(x => x.Type.Value >= PerceivableAtoms.ActivityBase && x.Type.Value < PerceivableAtoms.ActivityBase + 1000)
                    .Select(x => x.Type.Value).ToArray();
        }

        [Fact]
        public void Activity_IsStamped_FromBehavior()
        {
            var r = new Rig();
            var id = new EntityId(1);
            r.SetActivity(id, ActivityKind.Work);
            r.Step();   // behavior applies; system emits stamp
            r.Step();   // perceivable applies stamp

            Assert.Equal(new[] { PerceivableAtoms.Activity(ActivityKind.Work).Value }, r.ActivityAtoms(id));
        }

        [Fact]
        public void Activity_Swaps_OnChange_OldClearedNewStamped()
        {
            var r = new Rig();
            var id = new EntityId(1);
            r.SetActivity(id, ActivityKind.Work);
            r.Step(); r.Step();
            Assert.Single(r.ActivityAtoms(id));

            r.SetActivity(id, ActivityKind.Sleep);
            r.Step(); r.Step();

            Assert.Equal(new[] { PerceivableAtoms.Activity(ActivityKind.Sleep).Value }, r.ActivityAtoms(id));   // exactly one, swapped
        }

        [Fact]
        public void Activity_None_LeavesNoAtom()
        {
            var r = new Rig();
            var id = new EntityId(1);
            r.SetActivity(id, ActivityKind.None);
            r.Step(); r.Step();
            Assert.Empty(r.ActivityAtoms(id));
        }
    }
}
