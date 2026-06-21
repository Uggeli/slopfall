namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Recall is reconstruction, never replay: the full picture is rebuilt by re-applying the
    /// stored delta to the CURRENT category prediction. Because it reads the current node, a
    /// category that has drifted since encoding yields recalled "specifics" that are the new
    /// prediction wearing the old episode's costume — confident false memory is this one line, not
    /// a separate mechanism. Recall is also a write: it refreshes the record (reconsolidation).
    /// </summary>
    public static class MemoryRecall
    {
        /// <summary>predicted ⊕ deltaBag (delta wins). Novel or dangling-ref records have no
        /// prediction to merge against, so they reconstruct to their verbatim delta.</summary>
        public static AtomBag Reconstruct(in MemoryRecord record, MeaningsStore meanings)
        {
            if (record.IsNovel) return record.DeltaBag;
            CategoryNode node;
            if (!meanings.TryGetNode(record.CategoryRef, out node)) return record.DeltaBag;
            return AtomBag.Merge(node.Prediction(meanings.Config), record.DeltaBag);
        }

        /// <summary>Reconstruct the record at <paramref name="key"/> and refresh it (reconsolidation:
        /// recall renders the trace labile and re-stores it stronger). False if the key is absent.</summary>
        public static bool Recall(MemoryStore store, MeaningsStore meanings, MemoryKey key,
                                  int refreshDelta, long atTick, out AtomBag reconstructed)
        {
            MemoryRecord rec;
            if (!store.TryGet(key, out rec)) { reconstructed = AtomBag.Empty; return false; }
            reconstructed = Reconstruct(rec, meanings);
            store.Refresh(key, refreshDelta, atTick);
            return true;
        }
    }
}
