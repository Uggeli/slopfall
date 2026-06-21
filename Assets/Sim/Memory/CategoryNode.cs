namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// A category in the MEANINGS store: a fact and an expectation in one node. Prototype is the
    /// recognition centroid (fixed in A3). Predicted is the variance-gated running statistics
    /// surprise is measured against. Valence is the appetitive(+)/aversive(-) charge; Confidence
    /// weights how much it colors a read and rises/falls with confirming/contradicting evidence.
    /// INNATE nodes are the species seed (decay/evict-immune).
    /// </summary>
    public sealed class CategoryNode
    {
        public readonly CategoryId Id;
        public AtomBag Prototype;
        public readonly PredictedStats Predicted;
        public Fixed Valence;
        public Fixed Confidence;
        public readonly bool Innate;

        public CategoryNode(CategoryId id, AtomBag prototype, Fixed valence, Fixed confidence, bool innate)
        {
            Id = id;
            Prototype = prototype;
            Predicted = new PredictedStats();
            Valence = valence;
            Confidence = confidence;
            Innate = innate;
        }

        /// <summary>The current prediction (variance-gated intersection) under the given knobs.</summary>
        public AtomBag Prediction(in MeaningsConfig cfg)
            => Predicted.Prediction(cfg.VarianceThresholdRaw, cfg.MinPredictCount);
    }
}
