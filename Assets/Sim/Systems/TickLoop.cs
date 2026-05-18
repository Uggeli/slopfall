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
                _systems[i].ProcessEvents();

            // 4. Update — each system advances. Serial for Phase 0; Parallel.For later
            //    once we have enough systems and verified write-discipline.
            var tick = _ctx.Time.Tick;
            for (int i = 0; i < _systems.Count; i++)
                _systems[i].Update(tick);

            // 5. Flush — emits made during Update become next tick's queue.
            _ctx.Events.Flush();

            // 6. Advance tick counter.
            _ctx.Time.Tick++;

            sw.Stop();
            LastTickElapsedMs = sw.ElapsedMilliseconds;
        }
    }
}
