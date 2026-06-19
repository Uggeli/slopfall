namespace DaggerfallWorkshop.Sim.Engine
{
    /// Owns a slice of sim data and is its SOLE writer. Update() pulls last tick's
    /// intent events from the bus and applies them to its OWN storage, in place.
    /// It reads only its own data + events — never another registry — which is what
    /// lets every registry's Update() run in parallel with no contention. Exposes a
    /// read-only view of its data for systems to consume in the read phase.
    public abstract class Registry
    {
        protected readonly EventBus Events;
        protected Registry(EventBus events) => Events = events;

        /// WRITE phase: apply last tick's intents to own data. `tick` is provided for
        /// the rare registry that needs it (most ignore it).
        public abstract void Update(long tick);
    }
}
