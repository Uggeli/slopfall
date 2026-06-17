using System;
using System.Diagnostics;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Background thread that drives the sim TickLoop.
    ///
    /// Speed (TimeScale = game-seconds per real-second) is achieved by running
    /// ticks FASTER, not by advancing more game-time per tick: each tick keeps a
    /// small fixed game-step (DesiredStep) so movement/decisions stay fine-grained
    /// at any speed, and the real tick rate scales with TimeScale (capped at MaxTps
    /// for CPU; past the cap the step grows, degrading gracefully). The per-tick
    /// step is handed to TimeSystem via ctx.Time.LiveStepGameSeconds. Soak/tests
    /// drive the loop directly (never set the override) and keep the classic
    /// interval*scale stepping. Wall-clock pacing with a catch-up cap.
    public sealed class SimThread
    {
        // Target game-seconds advanced per tick at/below the tps cap. Small =
        // fine-grained sim; the loop ticks faster to reach the requested speed.
        const double DesiredStep = 0.1;
        const double MinTps = 10.0;     // floor so slow speeds still update smoothly
        const double MaxTps = 200.0;    // ceiling so high speeds don't peg a core
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

        public SimThread(TickLoop loop, SimulationContext ctx, SnapshotPublisher publisher)
        {
            _loop = loop;
            _ctx = ctx;
            _publisher = publisher;
            _thread = new Thread(Run) { Name = "DFU-Sim", IsBackground = true };
        }

        public void Start()
        {
            _running = true;
            _wallClock.Start();
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        void Run()
        {
            try
            {
                double nextTickAt = 0;
                double nextPublishAt = 0;
                while (_running)
                {
                    // Decide this tick's real cadence + game-step from the current
                    // speed. timeScale = R * step; keep step≈DesiredStep by varying
                    // R within [MinTps, MaxTps]; past the cap, step grows.
                    double ts = _ctx.WorldClock.Current.TimeScale;
                    double r, step;
                    if (ts <= 0.0)            // paused: keep ticking (snapshots flow) but freeze time
                    {
                        r = MinTps; step = 0.0;
                    }
                    else
                    {
                        r = ts / DesiredStep;
                        if (r < MinTps) r = MinTps;
                        else if (r > MaxTps) r = MaxTps;
                        step = ts / r;
                    }
                    double interval = 1.0 / r;

                    var now = _wallClock.Elapsed.TotalSeconds;
                    if (now >= nextTickAt)
                    {
                        _ctx.Time.LiveStepGameSeconds = step;   // TimeSystem advances this much this tick
                        _loop.Step();
                        // Decouple snapshot building from the tick rate: at high tps
                        // we step the sim finely but only publish ~20 Hz (the viewer
                        // consumes ~5 Hz). Always publish the very first tick.
                        if (now >= nextPublishAt) { PublishSnapshot(); nextPublishAt = now + PublishInterval; }
                        nextTickAt += interval;
                        // Catch-up cap: if we're > 1s behind (debugger pause, GC),
                        // resync rather than firing a tick storm.
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
