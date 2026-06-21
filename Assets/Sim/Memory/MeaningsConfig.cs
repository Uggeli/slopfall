namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Tunable knobs for the MEANINGS dynamics. All are p6 dynamics-tuning parameters, not
    /// architecture (roadmap "open knobs"); the defaults are scaffolding order-of-magnitude
    /// values. Raw units: distances/variance/valence are Fixed raw (Q8) or VarianceRaw (Q16) as
    /// noted on each field.
    /// </summary>
    public readonly struct MeaningsConfig
    {
        /// <summary>Recognition: a signature matches a prototype when their L1 distance (sum of
        /// |Δ| over atom raw values, Q8) is &lt;= this. Larger = looser recognition.</summary>
        public readonly long MatchThresholdRaw;

        /// <summary>Prediction: an atom type predicts only when its VarianceRaw (Q16) is &lt;= this
        /// (the variance gate). Smaller = stricter "stable feature" requirement.</summary>
        public readonly long VarianceThresholdRaw;

        /// <summary>Prediction: minimum samples before an atom type can predict.</summary>
        public readonly int MinPredictCount;

        /// <summary>Valence running mean rate: valence moves by (outcome - valence) >> LearnShift
        /// each reinforce. Larger = slower learning.</summary>
        public readonly int LearnShift;

        /// <summary>Confidence step per reinforce (Fixed raw, /256): up on confirming, down on
        /// contradicting.</summary>
        public readonly int ConfidenceGainRaw;

        /// <summary>Valence band (Fixed raw) around zero treated as "still learning the sign", so
        /// a fresh node's first outcomes count as confirming regardless of sign.</summary>
        public readonly int NeutralBandRaw;

        public MeaningsConfig(long matchThresholdRaw, long varianceThresholdRaw, int minPredictCount,
                              int learnShift, int confidenceGainRaw, int neutralBandRaw)
        {
            MatchThresholdRaw = matchThresholdRaw;
            VarianceThresholdRaw = varianceThresholdRaw;
            MinPredictCount = minPredictCount;
            LearnShift = learnShift;
            ConfidenceGainRaw = confidenceGainRaw;
            NeutralBandRaw = neutralBandRaw;
        }

        // Scaffolding defaults (spec "order-of-magnitude"): match within ~0.5 total L1 deviation;
        // predict at std <= ~0.1 (variance 0.01 -> Q16 ~655); 3-sample floor; learn rate 1/16;
        // confidence step ~0.05 (13/256); neutral band ~0.06 (16/256).
        public static readonly MeaningsConfig Default =
            new MeaningsConfig(128, 655, 3, 4, 13, 16);
    }
}
