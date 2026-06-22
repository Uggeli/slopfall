using System;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class QueueSerializesSaleTests
    {
        static readonly QueueAnchor Shop = new QueueAnchor(7, ActivityKind.Buy);

        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly SharedActivityRegistry Shared;
            public readonly BehaviorRegistry Behavior;
            public readonly SharedActivitySystem System;
            public Rig()
            {
                Shared = new SharedActivityRegistry(E);
                Behavior = new BehaviorRegistry(E);
                System = new SharedActivitySystem(E, Shared, Behavior);
            }
            public void Queue(int id)
            {
                E.Publish(new BehaviorSetIntent { Id = new EntityId(id), Data = new BehaviorData
                    { Activity = ActivityKind.Buy, Phase = ActivityPhase.Queued, TargetBuilding = 7,
                      TargetX = 10, TargetZ = 20 } });
                E.Publish(new QueueJoinIntent { Agent = new EntityId(id), Anchor = Shop,
                    Capacity = 1, AnchorX = 10, AnchorZ = 20 });
            }
            public void Leave(int id) => E.Publish(new QueueLeaveIntent { Agent = new EntityId(id) });
            public void Step(long t) { E.Tick(); Behavior.Update(t); Shared.Update(t); System.Update(t); }
            // Count agents currently at the counter (Served in registry AND behavior == Doing).
            // BehaviorData stays Doing after Leave until overwritten; the registry Served list
            // is the authoritative capacity gate — so we intersect both to avoid stale reads.
            public int DoingCount(int[] ids) => ids.Count(i =>
                Shared.IsServed(new EntityId(i))
                && Behavior.TryGet(new EntityId(i), out var b) && b.Phase == ActivityPhase.Doing
                && b.TargetBuilding == 7);
        }

        [Fact]
        public void ThreeBuyers_OneCounter_NeverMoreThanOneDoing()
        {
            var r = new Rig();
            var ids = new[] { 1, 2, 3 };
            foreach (var i in ids) r.Queue(i);

            int maxDoing = 0;
            for (long t = 0; t < 12; t++)
            {
                r.Step(t);
                maxDoing = Math.Max(maxDoing, r.DoingCount(ids));
                // whoever is being served finishes and leaves, freeing the counter
                foreach (var i in ids)
                    if (r.Behavior.TryGet(new EntityId(i), out var b) && b.Phase == ActivityPhase.Doing)
                        r.Leave(i);
            }
            Assert.True(maxDoing <= 1, $"capacity 1, but saw {maxDoing} agents Doing at once");
        }
    }
}
