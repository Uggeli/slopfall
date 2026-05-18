using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Sim-thread-only event bus with deferred-by-default semantics.
    ///
    /// Two-buffer flow:
    ///   - Systems call Emit() during Update. Events land in the front buffer.
    ///   - TickLoop calls Flush() at end of tick: front becomes the new back buffer.
    ///   - TickLoop calls Drain() at start of next tick: back buffer events fire to handlers.
    ///
    /// This breaks within-tick cycles (Emit → handler → Emit) and gives a stable
    /// "all systems see the same world state during ProcessEvents/Update" guarantee.
    ///
    /// Input events from the main thread bypass this and arrive via EmitImmediate() —
    /// they land directly in the back buffer so they fire this tick, not next tick.
    public sealed class EventBus
    {
        readonly Dictionary<Type, List<Action<ISimEvent>>> _handlers = new Dictionary<Type, List<Action<ISimEvent>>>();
        List<ISimEvent> _front = new List<ISimEvent>();
        List<ISimEvent> _back = new List<ISimEvent>();

        public void Subscribe<T>(Action<T> handler) where T : ISimEvent
        {
            var t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
            {
                list = new List<Action<ISimEvent>>();
                _handlers[t] = list;
            }
            list.Add(e => handler((T)e));
        }

        /// Emit during Update. Fires next tick.
        public void Emit<T>(T evt) where T : ISimEvent
        {
            _front.Add(evt);
        }

        /// Drop an event into the current tick's queue. Used by TickLoop when draining
        /// the cross-thread InputBus so input intents fire this tick rather than next.
        public void EmitImmediate<T>(T evt) where T : ISimEvent
        {
            _back.Add(evt);
        }

        /// Called by TickLoop at start of tick. Fires events queued at the end of last tick.
        public void Drain()
        {
            for (int i = 0; i < _back.Count; i++)
            {
                var evt = _back[i];
                if (_handlers.TryGetValue(evt.GetType(), out var list))
                {
                    for (int h = 0; h < list.Count; h++)
                        list[h](evt);
                }
            }
            _back.Clear();
        }

        /// Called by TickLoop at end of tick. Front (this tick's emits) becomes next tick's back.
        public void Flush()
        {
            var tmp = _back;
            _back = _front;
            _front = tmp;
        }
    }
}
