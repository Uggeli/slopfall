using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Marker for an intent event. Events are STRUCTS (value types) — zero-GC, read
    /// back as a contiguous span. They are the ONLY channel between systems (which
    /// emit them) and registries (which apply them).
    public interface IEvent { }

    internal interface ITickBuffer
    {
        void Flip();
    }

    /// Double-buffered, per-type event queue. Writes land in `_incoming` (locked,
    /// because systems Publish in parallel during the read phase); reads come from
    /// `_processing` (this tick's frozen batch, read with no lock because nothing
    /// writes it during the read phase). Flip() swaps them at the tick barrier.
    internal sealed class EventBuffer<T> : ITickBuffer where T : struct, IEvent
    {
        List<T> _incoming = new List<T>(1024);
        List<T> _processing = new List<T>(1024);
        readonly Lock _lock = new Lock();

        public void Enqueue(T evt)
        {
            lock (_lock) _incoming.Add(evt);
        }

        public void Flip()
        {
            _processing.Clear();
            var tmp = _incoming;
            _incoming = _processing;
            _processing = tmp;
        }

        // Direct memory view over this tick's batch — no copy, no allocation.
        public ReadOnlySpan<T> GetSpan() => CollectionsMarshal.AsSpan(_processing);
    }

    /// The one channel. Per-type double-buffered struct queues. No Subscribe, no
    /// callbacks, no immediate/deferred split — an event emitted this tick is read by
    /// everyone next tick, after Tick() flips the buffers.
    public sealed class EventBus
    {
        readonly ConcurrentDictionary<Type, object> _buffers = new ConcurrentDictionary<Type, object>();
        readonly List<ITickBuffer> _bufferList = new List<ITickBuffer>();
        readonly Lock _registrationLock = new Lock();

        /// Emit an intent. Thread-safe: systems publish in parallel. Readable next tick.
        public void Publish<T>(T evt) where T : struct, IEvent
        {
            var buffer = (EventBuffer<T>)_buffers.GetOrAdd(typeof(T), _ =>
            {
                var b = new EventBuffer<T>();
                lock (_registrationLock) _bufferList.Add(b);
                return b;
            });
            buffer.Enqueue(evt);
        }

        /// This tick's batch of T (last tick's emissions). Empty span if none.
        public ReadOnlySpan<T> GetEvents<T>() where T : struct, IEvent
        {
            return _buffers.TryGetValue(typeof(T), out var b)
                ? ((EventBuffer<T>)b).GetSpan()
                : ReadOnlySpan<T>.Empty;
        }

        /// Flip every per-type buffer: last tick's emissions become this tick's
        /// readable batch; the write side is cleared for the new tick.
        public void Tick()
        {
            // Snapshot count under the lock is unnecessary — registration only appends,
            // and Tick runs at the barrier with no concurrent Publish creating buffers.
            for (int i = 0; i < _bufferList.Count; i++)
                _bufferList[i].Flip();
        }
    }
}
