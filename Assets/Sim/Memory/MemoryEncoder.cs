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
    ///
    /// Novelty is maximal surprise, so a novel record always trips the surprise arm and is written
    /// SURPRISE-flagged (decay-resistant) — novel-and-verbatim things resist decay until a later
    /// MINT pass (A5) compresses them into a category.
    /// </summary>
    public static class MemoryEncoder
    {
        public static EncodeResult Perceive(MeaningsStore store, AtomBag signature, AtomBag percept,
                                            Fixed arousal, MemoryKey key, long tick, in EncodeConfig cfg)
        {
            CategoryId cat = store.Recognize(signature);

            Surprise surprise;
            AtomBag deltaBag;
            AtomBag prediction = AtomBag.Empty;
            if (cat.IsNone)
            {
                surprise = Surprise.Maximal;
                deltaBag = percept;                 // novelty -> store verbatim
            }
            else
            {
                CategoryNode node;
                store.TryGetNode(cat, out node);
                prediction = node.Prediction(store.Config);
                surprise = Surprise.Against(percept, prediction);
                deltaBag = AtomBag.Diff(percept, prediction);
                store.Fold(cat, percept);           // ungated StatFold on every recognition
            }

            // The WRITE GATE stays record-level + MEAN-encode (the anti-flood guard): whether to
            // write at all. Per-atom strength below decides only how deeply each stored atom etches.
            bool surpriseFired = surprise.Encode.Raw > cfg.SurpriseThresholdRaw;
            bool arousalFired = arousal.Raw > cfg.ArousalThresholdRaw;
            if (!surpriseFired && !arousalFired)
                return new EncodeResult(false, default(MemoryRecord), surprise, cat);

            // Per-atom etch. A recognized-and-fully-matched percept has an empty delta, but if AROUSAL
            // drove the write (it mattered though nothing diverged), etch the whole episode (flashbulb)
            // so the strength has atoms to live on.
            AtomBag bag = deltaBag.Count > 0 ? deltaBag : percept;
            int novelErr = Fixed.One.Raw;           // novelty: every atom maximally surprising
            var meta = new AtomMeta[bag.Count];
            for (int k = 0; k < bag.Count; k++)
            {
                long err = cat.IsNone
                    ? novelErr
                    : (prediction.TryGet(bag[k].Type, out var pv) ? Abs((long)bag[k].Value.Raw - pv.Raw)
                                                                  : Abs(bag[k].Value.Raw));
                int peak = err > arousal.Raw ? (int)err : arousal.Raw;
                // SURPRISE (decay-resistant) when the atom's OWN divergence drove its etch; if it only
                // rode in on arousal, it is ordinary and fades normally.
                MemoryFlags flags = err > arousal.Raw ? MemoryFlags.Surprise : MemoryFlags.None;
                meta[k] = new AtomMeta(Scale(peak), flags);
            }
            return new EncodeResult(true, new MemoryRecord(key, cat, bag, meta, tick, tick), surprise, cat);
        }

        /// <summary>Map a raw Fixed surprise/arousal (Q8, ~[0,1]) to a strength byte [0,255].</summary>
        static byte Scale(int raw)
        {
            if (raw < 0) raw = 0;
            else if (raw > 255) raw = 255;
            return (byte)raw;
        }

        static long Abs(long x) => x < 0 ? -x : x;
    }
}
