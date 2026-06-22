using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class ExecutionQueueRoutingTests
    {
        // Seed the clock so ExecutionSystem's clock.Year==0 guard doesn't early-return.
        static void InitClock(EventBus e, WorldClockRegistry clock)
        {
            e.Publish(new WorldClockSetIntent { Year = 405, Month = 1, Day = 1, Hour = 8,
                Minute = 0, Second = 0, TimeScale = 12f, DeltaGameSeconds = 1.2 });
            e.Tick(); clock.Update(0);
        }

        [Fact]
        public void BuyArrival_GoesToQueued_AndJoins()
        {
            var e = new EventBus();
            var intent = new IntentRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var position = new PositionRegistry(e);
            var clock = new WorldClockRegistry(e);
            var shared = new SharedActivityRegistry(e);
            var sys = new ExecutionSystem(e, intent, behavior, position, clock);

            InitClock(e, clock);

            e.Publish(new BehaviorSetIntent { Id = new EntityId(1), Data = new BehaviorData
                { Activity = ActivityKind.Buy, Phase = ActivityPhase.Moving, TargetBuilding = 7,
                  TargetX = 10, TargetZ = 20 } });
            e.Tick(); behavior.Update(0);

            e.Publish(new ArrivedAtTargetEvent { Entity = new EntityId(1) });
            e.Tick(); behavior.Update(1);                 // make the arrival event visible to the system
            sys.Update(1);

            // Flip the bus so sys.Update's emissions (BehaviorSetIntent + QueueJoinIntent)
            // move from _incoming into _processing, where GetEvents can read them.
            e.Tick();

            // Direct assertion on the QueueJoinIntent fields — verified before any registry
            // consumes them (GetEvents is non-destructive; the span is still there after).
            var joinEvents = e.GetEvents<QueueJoinIntent>().ToArray();
            Assert.Single(joinEvents);
            Assert.Equal(new EntityId(1), joinEvents[0].Agent);
            Assert.Equal(new QueueAnchor(7, ActivityKind.Buy), joinEvents[0].Anchor);
            Assert.Equal(1, joinEvents[0].Capacity);
            Assert.Equal(10f, joinEvents[0].AnchorX);
            Assert.Equal(20f, joinEvents[0].AnchorZ);

            behavior.Update(2); shared.Update(2);

            // Effect-based assertions: the registry settled the intent correctly.
            behavior.TryGet(new EntityId(1), out var b);
            Assert.Equal(ActivityPhase.Queued, b.Phase);
            Assert.True(shared.AnchorOf(new EntityId(1), out var a));
            Assert.Equal(new QueueAnchor(7, ActivityKind.Buy), a);
        }

        [Fact]
        public void NonBuyArrival_GoesToDoing_Unchanged()
        {
            var e = new EventBus();
            var intent = new IntentRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var position = new PositionRegistry(e);
            var clock = new WorldClockRegistry(e);
            var shared = new SharedActivityRegistry(e);
            var sys = new ExecutionSystem(e, intent, behavior, position, clock);

            InitClock(e, clock);

            e.Publish(new BehaviorSetIntent { Id = new EntityId(2), Data = new BehaviorData
                { Activity = ActivityKind.EatHome, Phase = ActivityPhase.Moving, TargetBuilding = 5,
                  TargetX = 1, TargetZ = 2 } });
            e.Tick(); behavior.Update(0);

            e.Publish(new ArrivedAtTargetEvent { Entity = new EntityId(2) });
            e.Tick(); behavior.Update(1);
            sys.Update(1);
            e.Tick(); behavior.Update(2);

            behavior.TryGet(new EntityId(2), out var b);
            Assert.Equal(ActivityPhase.Doing, b.Phase);
            Assert.Equal(ActivityKind.EatHome, b.Activity);
        }
    }
}
