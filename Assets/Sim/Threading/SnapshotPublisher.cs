using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Immutable snapshot of sim state for render-thread consumption. Sim publishes
    /// one at end of every tick. Unity main thread reads via SnapshotPublisher.Latest
    /// and interpolates between consecutive snapshots.
    ///
    /// Phase 0: tick counter + timing only. Phase 1+ adds entity render data.
    public sealed class RenderSnapshot
    {
        public long Tick;
        public double SimSeconds;
        public double WallClockSeconds;
    }

    /// Single-slot atomic publisher. Sim writes via Publish (Interlocked.Exchange),
    /// main thread reads via Latest (Volatile.Read).
    public sealed class SnapshotPublisher
    {
        RenderSnapshot _latest;

        public RenderSnapshot Latest => Volatile.Read(ref _latest);

        public void Publish(RenderSnapshot snapshot) => Interlocked.Exchange(ref _latest, snapshot);
    }
}
