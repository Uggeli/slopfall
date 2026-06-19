using System;
using System.Collections.Generic;
using System.Threading;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim.Web
{
    /// Drives a SimWorld on its own thread and publishes an immutable Frame after
    /// each batch — the replacement for the old SnapshotPublisher + SimThread +
    /// RenderSnapshot. The sim thread is the SOLE reader of the registries (it builds
    /// the Frame in the post-Step read phase); the web threads only ever touch the
    /// already-frozen Frame via Latest.
    ///
    /// The engine is fixed-timestep (≈0.1 game-seconds per tick), so wall-clock speed
    /// is purely how fast we step: ticksPerBatch is derived from the requested
    /// timeScale (game-seconds per real-second).
    public sealed class WorldRunner
    {
        public readonly struct AgentRow
        {
            public readonly int Id;
            public readonly float X, Z, Yaw;
            public readonly int Activity, Phase, Kind;
            public AgentRow(int id, float x, float z, float yaw, int activity, int phase, int kind)
            { Id = id; X = x; Z = z; Yaw = yaw; Activity = activity; Phase = phase; Kind = kind; }
        }

        public sealed class Frame
        {
            public long Tick;
            public int Hour, Minute;
            public bool Night;
            public float Sun;
            public WeatherKind Weather;
            public AgentRow[] Agents;
        }

        const int BatchSleepMs = 33;   // ~30 publishes/sec

        readonly SimWorld _world;
        volatile Frame _latest;
        volatile int _ticksPerBatch;
        volatile float _timeScale;
        bool _running = true;

        public Frame Latest => _latest;
        public float TimeScale => _timeScale;
        public SimWorld World => _world;

        public WorldRunner(SimWorld world, float timeScale)
        {
            _world = world;
            SetTimeScale(timeScale);
            _latest = Build(0);
        }

        public void SetTimeScale(float ts)
        {
            _timeScale = Math.Clamp(ts, 1f, 60000f);
            // ts game-sec/sec ÷ 0.1 game-sec/tick = ticks/sec; spread over ~30 batches/sec.
            _ticksPerBatch = Math.Clamp((int)MathF.Round(_timeScale / 3f), 1, 20000);
        }

        public void Start()
        {
            var t = new Thread(Loop) { IsBackground = true, Name = "sim" };
            t.Start();
        }

        public void Stop() => _running = false;

        void Loop()
        {
            long tick = 0;
            while (_running)
            {
                int n = _ticksPerBatch;
                for (int i = 0; i < n; i++) { _world.Step(); tick++; }
                _latest = Build(tick);   // built here, on the sim thread, in the read phase
                Thread.Sleep(BatchSleepMs);
            }
        }

        Frame Build(long tick)
        {
            var clk = _world.WorldClock.Current;
            var light = _world.Lighting.Current;
            var wx = _world.Weather.Current;

            var rows = new List<AgentRow>(_world.Position.Count);
            foreach (var kv in _world.Position.All)
            {
                var p = kv.Value;
                if (p == null) continue;
                int kind = 0, act = 0, phase = 0;
                if (_world.Identity.TryGet(kv.Key, out var id)) kind = (int)id.Kind;
                if (_world.Behavior.TryGet(kv.Key, out var b) && b != null) { act = (int)b.Activity; phase = (int)b.Phase; }
                rows.Add(new AgentRow(kv.Key.Value, p.X, p.Z, p.Yaw, act, phase, kind));
            }

            return new Frame
            {
                Tick = tick,
                Hour = clk.Hour, Minute = clk.Minute,
                Night = light.IsNight, Sun = light.SunIntensity,
                Weather = wx.Kind,
                Agents = rows.ToArray(),
            };
        }
    }
}
