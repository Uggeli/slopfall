namespace DaggerfallWorkshop.Sim
{
    /// Sim's own clock. Distinct from UnityEngine.Time — the sim thread
    /// drives this; Unity never reads it. TickLoop mutates Tick at the
    /// end of each step.
    public sealed class SimulationTime
    {
        /// FIXED timestep: the constant SIM time one tick advances (0.1s = 100ms).
        /// Never varies — speed is set by how fast ticks are FIRED in real time
        /// (SimThread), not by changing this. A constant step keeps per-tick
        /// behaviour identical everywhere (live, soak, tests) and avoids the whole
        /// class of dt-dependent bugs.
        public double TickIntervalSeconds { get; }
        public long Tick { get; internal set; }
        public double Elapsed => Tick * TickIntervalSeconds;

        public SimulationTime(double tickIntervalSeconds)
        {
            TickIntervalSeconds = tickIntervalSeconds;
        }
    }
}
