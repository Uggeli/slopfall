using System.Threading.Tasks;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// The tick loop. One barrier (event flip), then two phases that never overlap:
    ///   WRITE     — registries apply last tick's intents to their own data
    ///   READ+EMIT — systems read the settled data and publish next tick's intents
    /// Each phase runs its members in parallel. Safe without per-registry buffering
    /// because the phases are disjoint (no read during write) and each registry is the
    /// sole writer of its own storage. An intent emitted on tick N is applied at the
    /// top of tick N+1, before systems read again — uniform one-tick latency.
    public sealed class SimEngine
    {
        readonly EventBus _events;
        readonly Registry[] _registries;
        readonly SimSystem[] _systems;

        public long Tick { get; private set; }

        public SimEngine(EventBus events, Registry[] registries, SimSystem[] systems)
        {
            _events = events;
            _registries = registries;
            _systems = systems;
        }

        /// Parallel step — the production loop (nested parallelism: phases ∥, and each
        /// member may Parallel.ForEach its own interior).
        public void Step()
        {
            _events.Tick();
            Parallel.For(0, _registries.Length, i => _registries[i].Update(Tick));
            Parallel.For(0, _systems.Length, i => _systems[i].Update(Tick));
            Tick++;
        }

        /// Apply seed intents (published by the loader/bridge) into the registries
        /// WITHOUT running systems — so tick 0 reads the seeded world. Flip + registry
        /// apply, once.
        public void SeedApply()
        {
            _events.Tick();
            for (int i = 0; i < _registries.Length; i++) _registries[i].Update(0);
        }

        /// Serial step — same order, single-threaded. Used for the serial-correctness
        /// gate and deterministic reference runs (the parallel Step must match it).
        public void StepSerial()
        {
            _events.Tick();
            for (int i = 0; i < _registries.Length; i++) _registries[i].Update(Tick);
            for (int i = 0; i < _systems.Length; i++) _systems[i].Update(Tick);
            Tick++;
        }
    }
}
