using System.Collections.Concurrent;

namespace DaggerfallWorkshop.Sim
{
    /// Lock-free queue for main → sim communication. Unity main thread enqueues
    /// intent events; sim thread drains at the top of every tick.
    public sealed class InputBus
    {
        readonly ConcurrentQueue<ISimEvent> _queue = new ConcurrentQueue<ISimEvent>();

        public void Enqueue(ISimEvent evt) => _queue.Enqueue(evt);
        public bool TryDequeue(out ISimEvent evt) => _queue.TryDequeue(out evt);
    }
}
