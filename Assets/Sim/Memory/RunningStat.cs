namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Variance-gated running statistics for one atom type — the mechanical form of "the
    /// semantic fact is the intersection of the episodes." Integer accumulators only (no float
    /// sum), so it is replay-exact and machine-portable (spec: Determinism). Sum is in raw Q8
    /// units; SumSq in raw Q16 units (value.Raw squared). VarianceRaw is the spread² the A3
    /// variance gate compares — no sqrt needed at this layer.
    ///
    /// Sum/SumSq assume bounded atom magnitudes (atom values sit near the [0,1] fixed-point
    /// range and consolidation caps episode counts per the spec), so the long accumulators
    /// never overflow in practice. The integer-truncated mean makes VarianceRaw approximate,
    /// but it is provably non-negative (Cauchy-Schwarz on the integer floors) — the clamp in
    /// VarianceRaw is defensive, not a reachable path.
    /// </summary>
    public struct RunningStat
    {
        public int Count;
        public long Sum;     // Σ value.Raw    (Q8)
        public long SumSq;   // Σ value.Raw^2  (Q16)

        public void Add(Fixed value)
        {
            Count++;
            Sum += value.Raw;
            SumSq += (long)value.Raw * value.Raw;
        }

        /// <summary>Mean as a Fixed (Q8). Zero when empty. Integer division truncates toward zero.</summary>
        public Fixed Mean()
        {
            if (Count == 0) return Fixed.Zero;
            return new Fixed((int)(Sum / Count));
        }

        /// <summary>Population variance in raw Q16 units (spread² without the sqrt). Zero when
        /// empty or for a single sample; never negative.</summary>
        public long VarianceRaw()
        {
            if (Count == 0) return 0;
            long meanRaw = Sum / Count;        // Q8
            long meanSq = meanRaw * meanRaw;   // Q16
            long v = SumSq / Count - meanSq;   // Q16
            return v < 0 ? 0 : v;
        }
    }
}
