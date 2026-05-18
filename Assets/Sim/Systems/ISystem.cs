namespace DaggerfallWorkshop.Sim
{
    /// Every sim subsystem implements this. TickLoop calls them in registration order:
    ///   ProcessEvents (serial) → Update (serial for Phase 0, parallel later)
    public interface ISystem
    {
        void Init(SimulationContext ctx);

        /// Drain whatever this system listens to into its registries.
        /// Single-writer rule: exactly one system writes each registry field.
        void ProcessEvents();

        /// Advance state. Read freely; write only to registries you own. Emit events
        /// for cross-system effects (they fire next tick via EventBus).
        void Update(long tick);
    }
}
