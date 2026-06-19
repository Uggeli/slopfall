using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The engine is fixed-timestep (each Step advances 0.1 game-seconds), so "speed"
    /// is nothing but how fast we call Step(): a target tick rate in ticks/real-second.
    /// 0 pauses (we simply stop stepping); a negative target runs flat out, CPU-bound.
    /// Publishing the Frame is decoupled — it refreshes at a steady cadence regardless
    /// of the tick rate, and the web pump samples Latest at its own (slower) rate.
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

        const double PublishHz = 30.0;   // snapshot refresh rate, independent of tick rate
        const int UnlimitedTps = -1;     // run the engine flat out, CPU-bound

        readonly SimWorld _world;
        volatile Frame _latest;
        volatile int _targetTps;         // ticks/real-second; 0 = paused, <0 = unlimited
        bool _running = true;

        public Frame Latest => _latest;
        public int TargetTps => _targetTps;
        public SimWorld World => _world;

        public WorldRunner(SimWorld world, int tps)
        {
            _world = world;
            SetTickRate(tps);
            _latest = Build(0);
        }

        // The viewer's speed control: how fast to tick the engine, in ticks/real-second.
        // 0 pauses (the loop stops stepping); a negative value runs flat out. There is
        // no game-time multiplier — each tick always advances the engine's fixed step.
        public void SetTickRate(int tps) => _targetTps = tps < 0 ? UnlimitedTps : Math.Min(tps, 100000);

        public void Start()
        {
            var t = new Thread(Loop) { IsBackground = true, Name = "sim" };
            t.Start();
        }

        public void Stop() => _running = false;

        void Loop()
        {
            var sw = Stopwatch.StartNew();
            long tick = 0;
            double prev = sw.Elapsed.TotalSeconds, tickAcc = 0, nextPublish = 0;
            while (_running)
            {
                double now = sw.Elapsed.TotalSeconds;
                double dt = now - prev; prev = now;
                int tps = _targetTps;

                if (tps == UnlimitedTps) { _world.Step(); tick++; }   // flat out, CPU-bound
                else if (tps > 0)
                {
                    // Accumulate the time we owe and spend it in whole ticks. Cap the
                    // backlog so a hitch (or a tab regaining focus) can't trigger a
                    // catch-up spiral of thousands of steps.
                    tickAcc += dt * tps;
                    if (tickAcc > tps) tickAcc = tps;
                    while (tickAcc >= 1.0) { _world.Step(); tick++; tickAcc -= 1.0; }
                }
                else tickAcc = 0;   // paused: don't bank time while stopped

                // Refresh the published Frame on a fixed cadence, not per tick — so an
                // unlimited run doesn't rebuild the snapshot thousands of times a second.
                if (now >= nextPublish) { _latest = Build(tick); nextPublish = now + 1.0 / PublishHz; }

                if (tps != UnlimitedTps) Thread.Sleep(1);   // yield unless running flat out
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
