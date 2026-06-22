using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class SharedActivityRegistryTests
    {
        static (EventBus, SharedActivityRegistry) New()
        {
            var e = new EventBus();
            return (e, new SharedActivityRegistry(e));
        }
        static void Tick(EventBus e, SharedActivityRegistry r) { e.Tick(); r.Update(0); }
        static QueueAnchor Shop => new QueueAnchor(7, ActivityKind.Buy);
        static QueueJoinIntent Join(int agent) => new QueueJoinIntent
            { Agent = new EntityId(agent), Anchor = Shop, Capacity = 1, AnchorX = 10, AnchorZ = 20 };

        [Fact]
        public void Join_FirstAgent_IsPromotedToServed()
        {
            var (e, r) = New();
            e.Publish(Join(1));
            Tick(e, r);
            Assert.True(r.IsServed(new EntityId(1)));
            Assert.Equal(-1, r.PositionOf(new EntityId(1)));   // served, not waiting
        }

        [Fact]
        public void Join_BeyondCapacity_Waits_InOrder()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(2)); e.Publish(Join(3));
            Tick(e, r);
            Assert.True(r.IsServed(new EntityId(1)));            // capacity 1
            Assert.Equal(0, r.PositionOf(new EntityId(2)));      // head of line
            Assert.Equal(1, r.PositionOf(new EntityId(3)));
        }

        [Fact]
        public void Leave_Served_PromotesNextHead()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(2)); Tick(e, r);
            e.Publish(new QueueLeaveIntent { Agent = new EntityId(1) }); Tick(e, r);
            Assert.False(r.IsServed(new EntityId(1)));
            Assert.True(r.IsServed(new EntityId(2)));
        }

        [Fact]
        public void Leave_MidLine_ClosesGap()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(2)); e.Publish(Join(3)); Tick(e, r);
            e.Publish(new QueueLeaveIntent { Agent = new EntityId(2) }); Tick(e, r);
            Assert.Equal(0, r.PositionOf(new EntityId(3)));      // 3 moves up
        }

        [Fact]
        public void Leave_LastMember_ReapsInstance()
        {
            var (e, r) = New();
            e.Publish(Join(1)); Tick(e, r);
            e.Publish(new QueueLeaveIntent { Agent = new EntityId(1) }); Tick(e, r);
            Assert.False(r.TryGet(Shop, out _));
        }

        [Fact]
        public void AnchorOf_TracksMembership()
        {
            var (e, r) = New();
            e.Publish(Join(1)); Tick(e, r);
            Assert.True(r.AnchorOf(new EntityId(1), out var a));
            Assert.Equal(Shop, a);
            Assert.False(r.AnchorOf(new EntityId(99), out _));
        }

        [Fact]
        public void Join_Idempotent_NoDuplicateMembership()
        {
            var (e, r) = New();
            e.Publish(Join(1)); e.Publish(Join(1)); Tick(e, r);
            Assert.True(r.IsServed(new EntityId(1)));
            Assert.Single(r.ServedSnapshot());
        }
    }
}
