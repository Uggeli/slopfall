using System.Collections.Generic;
using System.Diagnostics;

namespace DaggerfallWorkshop.Sim
{
    /// Sim tick orchestrator. Owns the system list and runs one full tick per Step().
    /// Step() is called from SimThread.Run() at the configured rate.
    public sealed class TickLoop
    {
        readonly SimulationContext _ctx;
        readonly List<ISystem> _systems = new List<ISystem>();
        readonly List<long> _systemTicks = new List<long>();   // cumulative Stopwatch ticks per system (profiling)

        public long LastTickElapsedMs { get; private set; }
        public IReadOnlyList<ISystem> Systems => _systems;

        public TickLoop(SimulationContext ctx)
        {
            _ctx = ctx;
        }

        public void Register(ISystem system)
        {
            system.Init(_ctx);
            _systems.Add(system);
            _systemTicks.Add(0);
        }

        /// Per-system share of total time so far (ProcessEvents + Update), sorted
        /// heaviest-first — so we parallelize the system that actually dominates,
        /// not a guess. Cheap GetTimestamp deltas; only meaningful after a run.
        public string Profile()
        {
            long total = 0;
            for (int i = 0; i < _systemTicks.Count; i++) total += _systemTicks[i];
            if (total <= 0) return "(no timing collected)";
            double toMs = 1000.0 / Stopwatch.Frequency;

            var idx = new List<int>();
            for (int i = 0; i < _systems.Count; i++) idx.Add(i);
            idx.Sort((a, b) => _systemTicks[b].CompareTo(_systemTicks[a]));

            var sb = new System.Text.StringBuilder();
            sb.Append("per-system time (total ").Append((total * toMs).ToString("F0")).Append(" ms):\n");
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                if (_systemTicks[i] <= 0) continue;
                sb.Append("  ").Append(_systems[i].GetType().Name.PadRight(24)).Append(' ')
                  .Append((_systemTicks[i] * toMs).ToString("F0").PadLeft(7)).Append(" ms  ")
                  .Append((100.0 * _systemTicks[i] / total).ToString("F1").PadLeft(5)).Append("%\n");
            }
            return sb.ToString();
        }

        public void Step()
        {
            var sw = Stopwatch.StartNew();

            // 1. Drain cross-thread input intents into this tick's event queue.
            while (_ctx.Inputs.TryDequeue(out var inputEvt))
                _ctx.Events.EmitImmediate(inputEvt);

            // 2. Fire last tick's emitted events + this tick's input events to handlers.
            _ctx.Events.Drain();

            // 3. ProcessEvents — each system applies the events it cares about to its registries.
            for (int i = 0; i < _systems.Count; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                _systems[i].ProcessEvents();
                _systemTicks[i] += Stopwatch.GetTimestamp() - t0;
            }

            // 4. Update — each system advances. Serial for Phase 0; the independent
            //    per-entity systems can go Parallel.For (own-row writes only), the
            //    contended ones (Economy/Social) shard by settlement — see Profile().
            var tick = _ctx.Time.Tick;
            for (int i = 0; i < _systems.Count; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                _systems[i].Update(tick);
                _systemTicks[i] += Stopwatch.GetTimestamp() - t0;
            }

            // 5. Flush — emits made during Update become next tick's queue.
            _ctx.Events.Flush();

            // 6. Advance tick counter.
            _ctx.Time.Tick++;

            sw.Stop();
            LastTickElapsedMs = sw.ElapsedMilliseconds;
        }
    }
}
