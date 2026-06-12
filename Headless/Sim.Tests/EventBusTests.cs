using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    sealed class PingEvent : ISimEvent { public int N; }

    public class EventBusTests
    {
        [Fact]
        public void Emit_IsDeferred_UntilFlushThenDrain()
        {
            var bus = new EventBus();
            var received = new List<int>();
            bus.Subscribe<PingEvent>(e => received.Add(e.N));

            bus.Emit(new PingEvent { N = 1 });
            bus.Drain();
            Assert.Empty(received);          // still in front buffer

            bus.Flush();                     // front -> back
            bus.Drain();                     // back fires
            Assert.Equal(new[] { 1 }, received);
        }

        [Fact]
        public void EmitImmediate_FiresOnNextDrain_WithoutFlush()
        {
            var bus = new EventBus();
            var received = new List<int>();
            bus.Subscribe<PingEvent>(e => received.Add(e.N));

            bus.EmitImmediate(new PingEvent { N = 7 });
            bus.Drain();
            Assert.Equal(new[] { 7 }, received);
        }

        [Fact]
        public void EmitDuringDrain_DefersToNextTick_NoReentrancy()
        {
            var bus = new EventBus();
            int fired = 0;
            bus.Subscribe<PingEvent>(e =>
            {
                fired++;
                if (fired < 3) bus.Emit(new PingEvent { N = fired });
            });

            bus.EmitImmediate(new PingEvent { N = 0 });
            bus.Drain();
            Assert.Equal(1, fired);          // handler's Emit didn't fire within the same drain

            bus.Flush();
            bus.Drain();
            Assert.Equal(2, fired);          // cascades one tick at a time
        }

        [Fact]
        public void MultipleHandlers_AllReceive_InSubscriptionOrder()
        {
            var bus = new EventBus();
            var order = new List<string>();
            bus.Subscribe<PingEvent>(e => order.Add("a"));
            bus.Subscribe<PingEvent>(e => order.Add("b"));

            bus.EmitImmediate(new PingEvent());
            bus.Drain();
            Assert.Equal(new[] { "a", "b" }, order);
        }

        [Fact]
        public void Drain_ClearsBackBuffer_EventsFireOnlyOnce()
        {
            var bus = new EventBus();
            int fired = 0;
            bus.Subscribe<PingEvent>(e => fired++);

            bus.EmitImmediate(new PingEvent());
            bus.Drain();
            bus.Drain();
            Assert.Equal(1, fired);
        }
    }
}
