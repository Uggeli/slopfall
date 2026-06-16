namespace DaggerfallWorkshop.Sim
{
    /// Deterministic, tick-rate-independent decay toward a baseline — the
    /// mechanical form of the DecayTowardBaseline satisfaction-model (Atoms
    /// what_is_drive.md; see docs/living_world_L1_entropy.md).
    ///
    /// Expressed per game-hour: the same wall of game-time erodes the same
    /// amount regardless of TimeScale or tick cadence, and the result is a pure
    /// function of its inputs, so it never introduces replay drift. Composes
    /// exactly — decaying H1 hours then H2 hours equals decaying (H1+H2) at
    /// once — which is what lets callers apply it on any cadence they like.
    public static class Decay
    {
        /// Pull <paramref name="v"/> toward <paramref name="baseline"/>.
        /// <paramref name="ratePerHour"/> in [0,1] is the fraction of the
        /// remaining gap closed in one game-hour; <paramref name="gameHours"/>
        /// may be fractional. rate &lt;= 0 or hours &lt;= 0 → unchanged;
        /// rate &gt;= 1 → snaps to baseline.
        public static double TowardBaseline(double v, double baseline, double ratePerHour, double gameHours)
        {
            if (ratePerHour <= 0.0 || gameHours <= 0.0) return v;
            if (ratePerHour >= 1.0) return baseline;
            double retained = System.Math.Pow(1.0 - ratePerHour, gameHours);
            return baseline + (v - baseline) * retained;
        }
    }
}
