using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class SharedActivitySystemTests
    {
        [Fact]
        public void ServedAgent_StillQueued_IsFlippedToDoing()
        {
            var e = new EventBus();
            var shared = new SharedActivityRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var sys = new SharedActivitySystem(e, shared, behavior);

            // Agent arrived & queued for Buy.
            e.Publish(new BehaviorSetIntent { Id = new EntityId(1), Data = new BehaviorData
                { Activity = ActivityKind.Buy, Phase = ActivityPhase.Queued, TargetBuilding = 7,
                  TargetX = 10, TargetZ = 20 } });
            e.Publish(new QueueJoinIntent { Agent = new EntityId(1),
                Anchor = new QueueAnchor(7, ActivityKind.Buy), Capacity = 1, AnchorX = 10, AnchorZ = 20 });
            e.Tick(); behavior.Update(0); shared.Update(0);   // apply behaviour + promote to Served

            sys.Update(1);                                    // system sees Served-but-Queued → publishes flip
            e.Tick(); behavior.Update(1);                     // apply the flip

            behavior.TryGet(new EntityId(1), out var b);
            Assert.Equal(ActivityPhase.Doing, b.Phase);
            Assert.True(b.RemainingGameMinutes > 0);          // service clock started
        }

        [Fact]
        public void Waiter_TargetSetToSlot()
        {
            var e = new EventBus();
            var shared = new SharedActivityRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var sys = new SharedActivitySystem(e, shared, behavior);

            foreach (var id in new[] { 1, 2 })
                e.Publish(new BehaviorSetIntent { Id = new EntityId(id), Data = new BehaviorData
                    { Activity = ActivityKind.Buy, Phase = ActivityPhase.Queued, TargetBuilding = 7,
                      TargetX = 10, TargetZ = 20 } });
            foreach (var id in new[] { 1, 2 })
                e.Publish(new QueueJoinIntent { Agent = new EntityId(id),
                    Anchor = new QueueAnchor(7, ActivityKind.Buy), Capacity = 1, AnchorX = 10, AnchorZ = 20 });
            e.Tick(); behavior.Update(0); shared.Update(0);   // agent 1 served, agent 2 waiting at pos 0

            sys.Update(1);
            e.Tick(); behavior.Update(1);

            behavior.TryGet(new EntityId(2), out var w);
            Assert.Equal(20f - 0 * 1.5f, w.TargetZ);          // slot 0 = AnchorZ - 0*spacing
            Assert.Equal(ActivityPhase.Queued, w.Phase);      // still waiting
        }
    }
}
