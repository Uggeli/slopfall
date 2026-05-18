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

        public SimulationTime(double tickIntervalSeconds)
        {
            TickIntervalSeconds = tickIntervalSeconds;
        }
    }
}
