namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>The outcome of perceiving one percept: whether it was written, the would-be record,
    /// the surprise (attention spikes even when nothing is stored), and the recognized category
    /// (None = novel).</summary>
    public readonly struct EncodeResult
    {
        public readonly bool Written;
        public readonly MemoryRecord Record;   // valid only when Written
        public readonly Surprise Surprise;
        public readonly CategoryId Category;

        public EncodeResult(bool written, MemoryRecord record, Surprise surprise, CategoryId category)
        {
            Written = written;
            Record = record;
            Surprise = surprise;
            Category = category;
        }
    }

    /// <summary>
    /// The write path's front half (row 2 detect + row 5 encode, fused into a pure decision). Given
    /// a percept it recognizes a category, folds the percept's stats (ungated StatFold), measures
    /// surprise against the category's prediction, and — if surprise or arousal crosses the gate —
    /// builds a MemoryRecord whose deltaBag is only the divergence (the full percept when novel) at
    /// a strength set by max(encode-surprise, arousal). It does NOT insert the record (Phase B routes
    /// it to a store); keeping it a pure function makes the dynamics testable in isolation.
    /// </summary>
    public static class MemoryEncoder
    {
        public static EncodeResult Perceive(MeaningsStore store, AtomBag signature, AtomBag percept,
                                            Fixed arousal, MemoryKey key, long tick, in EncodeConfig cfg)
        {
            CategoryId cat = store.Recognize(signature);

            Surprise surprise;
            AtomBag deltaBag;
            if (cat.IsNone)
            {
                surprise = Surprise.Maximal;
                deltaBag = percept;                 // novelty -> store verbatim
            }
            else
            {
                CategoryNode node;
                store.TryGetNode(cat, out node);
                AtomBag prediction = node.Prediction(store.Config);
                surprise = Surprise.Against(percept, prediction);
                deltaBag = AtomBag.Diff(percept, prediction);
                store.Fold(cat, percept);           // ungated StatFold on every recognition
            }

            bool surpriseFired = surprise.Encode.Raw > cfg.SurpriseThresholdRaw;
            bool arousalFired = arousal.Raw > cfg.ArousalThresholdRaw;
            if (!surpriseFired && !arousalFired)
                return new EncodeResult(false, default(MemoryRecord), surprise, cat);

            int peak = surprise.Encode.Raw > arousal.Raw ? surprise.Encode.Raw : arousal.Raw;
            byte strength = Scale(peak);
            MemoryFlags flags = surpriseFired ? MemoryFlags.Surprise : MemoryFlags.None;
            MemoryRecord record = new MemoryRecord(key, cat, deltaBag, strength, tick, tick, flags);
            return new EncodeResult(true, record, surprise, cat);
        }

        /// <summary>Map a raw Fixed surprise/arousal (Q8, ~[0,1]) to a strength byte [0,255].</summary>
        static byte Scale(int raw)
        {
            if (raw < 0) raw = 0;
            else if (raw > 255) raw = 255;
            return (byte)raw;
        }
    }
}
