using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The sleep job (row 7), over one agent's own stores. Per pass: RE-DIFF (drop what the fact
    /// now covers) → MINT (compress clustered novelty into new facts) → DECAY (erode by strength).
    /// SETTLE/split is the spec's deliberately under-pinned step and is deferred. Everything is a
    /// pure mutation of the passed stores, deterministic in key/creation order.
    /// </summary>
    public static partial class Consolidation
    {
        /// <summary>
        /// RE-DIFF: re-diff each recognized record's deltaBag against its category's UPDATED
        /// prediction, dropping atoms the fact now predicts. A record that re-diffs to empty has
        /// fully migrated into the fact and is removed (pure redundancy); INNATE and novel records
        /// are left alone.
        /// </summary>
        public static void ReDiff(MemoryStore store, MeaningsStore meanings)
        {
            List<MemoryRecord> snapshot = Snapshot(store);
            for (int i = 0; i < snapshot.Count; i++)
            {
                MemoryRecord rec = snapshot[i];
                if (rec.IsNovel) continue;
                CategoryNode node;
                if (!meanings.TryGetNode(rec.CategoryRef, out node)) continue;   // dangling ref — leave

                AtomBag newDelta = AtomBag.Diff(rec.DeltaBag, node.Prediction(meanings.Config));
                if (newDelta.Count == 0 && !rec.IsInnate)
                {
                    store.Remove(rec.Key);
                }
                else
                {
                    store.Encode(new MemoryRecord(rec.Key, rec.CategoryRef, newDelta,
                        rec.Strength, rec.WrittenAt, rec.LastRefresh, rec.Flags));
                }
            }
        }

        static List<MemoryRecord> Snapshot(MemoryStore store)
        {
            List<MemoryRecord> list = new List<MemoryRecord>(store.Count);
            for (int i = 0; i < store.Count; i++) list.Add(store[i]);
            return list;
        }
    }
}
