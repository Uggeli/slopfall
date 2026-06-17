namespace DaggerfallWorkshop.Sim
{
    /// Sim's own clock. Distinct from UnityEngine.Time — the sim thread
    /// drives this; Unity never reads it. TickLoop mutates Tick at the
    /// end of each step.
    public sealed class SimulationTime
    {
        public double TickIntervalSeconds { get; }
        public long Tick { get; internal set; }
        public double Elapsed => Tick * TickIntervalSeconds;

        /// Per-tick game-seconds the live driver wants TimeSystem to advance, set
        /// by SimThread when running real-time so it can keep a small fixed step and
        /// pace ticks faster with timescale. Negative = unset (soak/tests/seed),
        /// in which case TimeSystem uses the classic TickIntervalSeconds * TimeScale.
        public double LiveStepGameSeconds = -1.0;

        public SimulationTime(double tickIntervalSeconds)
        {
            TickIntervalSeconds = tickIntervalSeconds;
        }
    }
}
