using System;
using System.Diagnostics;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Background thread that drives the sim TickLoop at a FIXED timestep.
    ///
    /// One tick always advances the same sim time (ctx.Time.TickIntervalSeconds,
    /// 0.1s) — never varied. Speed (TimeScale = sim-seconds per real-second) is
    /// achieved purely by how fast ticks are FIRED: realInterval = tickStep /
    /// TimeScale, so 1× fires at 10 tps, 12× at 120 tps, etc. There is no tps cap —
    /// at high speed the loop ticks as fast as the CPU allows (no sleep while behind
    /// schedule), so the only limit is raw throughput. Pause (TimeScale ≤ 0): the
    /// loop keeps ticking at a base rate so inputs/unpause process, but TimeSystem
    /// advances 0. Soak/tests drive the loop directly and get the same fixed step.
    public sealed class SimThread
    {
        const double PublishInterval = 0.05;   // build snapshots ≤20 Hz regardless of tick rate

        readonly TickLoop _loop;
        readonly SimulationContext _ctx;
        readonly SnapshotPublisher _publisher;
        readonly Thread _thread;
        readonly Stopwatch _wallClock = new Stopwatch();

        volatile bool _running;
        volatile Exception _lastException;

        public long TicksRun => _ctx.Time.Tick;
        public long LastTickMs => _loop.LastTickElapsedMs;
        public Exception LastException => _lastException;
        public bool IsRunning => _running;

        readonly Thread _snapshotThread;

        public SimThread(TickLoop loop, SimulationContext ctx, SnapshotPublisher publisher)
        {
            _loop = loop;
            _ctx = ctx;
            _publisher = publisher;
            _thread = new Thread(Run) { Name = "DFU-Sim", IsBackground = true };
            _snapshotThread = new Thread(SnapshotLoop) { Name = "DFU-Snapshot", IsBackground = true };
        }

        public void Start()
        {
            _running = true;
            _wallClock.Start();
            _thread.Start();
            _snapshotThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread.Join(TimeSpan.FromSeconds(2));
            _snapshotThread.Join(TimeSpan.FromSeconds(2));
        }

        // Snapshots are a CLIENT read, not the sim's job: build them on their own
        // thread at a steady ~20 Hz, independent of the tick rate. Safe to read the
        // registries concurrently with the sim thread — they're ConcurrentDictionary
        // with atomically-swapped immutable values, so a reader sees coherent values
        // (across-registry coherence is loose by a sub-tick, invisible for a view).
        void SnapshotLoop()
        {
            while (_running)
            {
                try { PublishSnapshot(); } catch (Exception ex) { _lastException = ex; }
                Thread.Sleep((int)(PublishInterval * 1000));
            }
        }

        void Run()
        {
            try
            {
                double tickStep = _ctx.Time.TickIntervalSeconds;   // fixed sim-seconds per tick
                double nextTickAt = 0;
                while (_running)
                {
                    // Fixed step; speed = how fast we fire ticks. No cap: when behind
                    // schedule the loop steps every iteration (no sleep), so high
                    // speeds run as fast as the CPU can tick. Paused/unseeded (ts ≤ 0):
                    // tick at a base rate so inputs (seed, unpause) still process —
                    // TimeSystem advances 0. Snapshots are built on a separate thread.
                    double ts = _ctx.WorldClock.Current.TimeScale;
                    double interval = ts <= 0.0 ? 0.1 : tickStep / ts;

                    var now = _wallClock.Elapsed.TotalSeconds;
                    if (now >= nextTickAt)
                    {
                        _loop.Step();
                        nextTickAt += interval;
                        // Catch-up cap: if we're > 1s behind (GC, can't keep up at high
                        // speed), resync rather than firing a tick storm.
                        if (now - nextTickAt > 1.0) nextTickAt = now + interval;
                    }
                    else
                    {
                        var sleepMs = (int)Math.Max(1, (nextTickAt - now) * 1000.0);
                        Thread.Sleep(sleepMs);
                    }
                }
            }
            catch (Exception ex)
            {
                _lastException = ex;
                _running = false;
            }
        }

        void PublishSnapshot()
        {
            _publisher.Publish(SnapshotBuilder.Build(_ctx, _wallClock.Elapsed.TotalSeconds));
        }
    }
}
