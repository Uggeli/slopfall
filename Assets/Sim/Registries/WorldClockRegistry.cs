using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    public sealed class WorldClockData
    {
        public int Year;
        public int Month;          // 0-11 in DFU's DaggerfallDateTime
        public int Day;            // 0-based day of month
        public int Hour;           // 0-23
        public int Minute;         // 0-59
        public float Second;       // 0-59.999
        public float TimeScale;    // game-seconds advanced per real-second (speed)

        /// Game-seconds advanced by the tick that produced this clock value. The
        /// single source of per-tick game-dt: every system reads this instead of
        /// recomputing TickIntervalSeconds * TimeScale, so the loop can decouple
        /// step size from real cadence (fine steps + faster ticks when sped up).
        public double DeltaGameSeconds;
    }

    /// Single-global world time. No per-entity storage. Phase 1: mirrored from
    /// DaggerfallUnity.Instance.WorldTime each frame by WorldClockMirror.
    public sealed class WorldClockRegistry
    {
        WorldClockData _data = new WorldClockData();

        public WorldClockData Current => Volatile.Read(ref _data);
        public void Set(WorldClockData data) => Interlocked.Exchange(ref _data, data);
    }
}
