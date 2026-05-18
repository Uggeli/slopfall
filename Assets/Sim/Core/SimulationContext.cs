namespace DaggerfallWorkshop.Sim
{
    /// Container for all sim-thread-owned services + registries.
    /// Phase 0: just clock, RNG, event bus, and the cross-thread input queue.
    /// Phase 1+ will add registry references here as they land.
    public sealed class SimulationContext
    {
        public EventBus Events { get; }
        public SimulationTime Time { get; }
        public SimRandom Random { get; }
        public InputBus Inputs { get; }

        public SimulationContext(EventBus events, SimulationTime time, SimRandom random, InputBus inputs)
        {
            Events = events;
            Time = time;
            Random = random;
            Inputs = inputs;
        }
    }
}
