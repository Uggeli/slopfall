using System;
using System.Diagnostics;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Background thread that drives the sim TickLoop at a fixed rate.
    /// Wall-clock-based pacing with catch-up cap so a long pause doesn't
    /// trigger a tick storm.
    public sealed class SimThread
    {
        readonly TickLoop _loop;
        readonly SimulationContext _ctx;
        readonly SnapshotPublisher _publisher;
        readonly double _tickIntervalSeconds;
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
            _tickIntervalSeconds = ctx.Time.TickIntervalSeconds;
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
                while (_running)
                {
                    var now = _wallClock.Elapsed.TotalSeconds;
                    if (now >= nextTickAt)
                    {
                        _loop.Step();
                        PublishSnapshot();
                        nextTickAt += _tickIntervalSeconds;
                        // Catch-up cap: if we're > 1s behind (debugger pause, GC),
                        // resync rather than firing a tick storm.
                        if (now - nextTickAt > 1.0) nextTickAt = now + _tickIntervalSeconds;
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
