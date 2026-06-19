namespace DaggerfallWorkshop.Sim.Engine
{
    /// Pure logic. Reads registries READ-ONLY (so its Update() parallelizes freely,
    /// even with a Parallel.ForEach over entities) and Publishes intent events. Holds
    /// NO data — only references to the bus and the registries it reads. Statelessness
    /// is structural: no fields to spool, cache, or carry across ticks.
    ///
    /// (Named SimSystem, not System, to avoid clashing with the `System` namespace
    /// the rest of the codebase uses heavily.)
    public abstract class SimSystem
    {
        protected readonly EventBus Events;
        protected SimSystem(EventBus events) => Events = events;

        /// READ + EMIT phase: read settled tick data, publish next tick's intents.
        /// `tick` is the current tick index — for cadence (tick % N), cooldown
        /// comparisons, and stateless hashed RNG keyed by (tick, id).
        public abstract void Update(long tick);
    }
}
