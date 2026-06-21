namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Encode-gate thresholds (raw Fixed units, /256). A percept is written when its MEAN-encode
    /// surprise exceeds SurpriseThresholdRaw OR arousal exceeds ArousalThresholdRaw. Both are p6
    /// tuning knobs, not architecture; the defaults are scaffolding (θ_s ~ 0.3, θ_a ~ 0.6) chosen
    /// so a single trivial new atom (the notched ear, MEAN ~ 1/6) does NOT cross the surprise arm.
    /// </summary>
    public readonly struct EncodeConfig
    {
        public readonly int SurpriseThresholdRaw;   // θ_s
        public readonly int ArousalThresholdRaw;    // θ_a

        public EncodeConfig(int surpriseThresholdRaw, int arousalThresholdRaw)
        {
            SurpriseThresholdRaw = surpriseThresholdRaw;
            ArousalThresholdRaw = arousalThresholdRaw;
        }

        public static readonly EncodeConfig Default = new EncodeConfig(77, 154);   // ~0.3, ~0.6
    }
}
