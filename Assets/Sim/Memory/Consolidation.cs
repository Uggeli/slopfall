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

        /// <summary>
        /// MINT: cluster the store's novel records by deltaBag proximity; a cluster with enough
        /// support mints a new category from the members' variance-gated intersection (their shared
        /// stable atoms) and re-keys each member to it, re-diffing away the now-predicted shared
        /// atoms. Novelty stops being verbatim. Clusters too small, or with no stable intersection,
        /// are left novel.
        /// </summary>
        public static void Mint(MemoryStore store, MeaningsStore meanings, in ConsolidationConfig cfg)
        {
            List<MemoryRecord> novel = new List<MemoryRecord>();
            for (int i = 0; i < store.Count; i++)
                if (store[i].IsNovel) novel.Add(store[i]);
            if (novel.Count == 0) return;

            // Greedy clustering: each record joins the first cluster whose seed is within threshold.
            List<List<MemoryRecord>> clusters = new List<List<MemoryRecord>>();
            for (int i = 0; i < novel.Count; i++)
            {
                bool placed = false;
                for (int c = 0; c < clusters.Count; c++)
                {
                    if (MeaningsStore.SignatureDistance(novel[i].DeltaBag, clusters[c][0].DeltaBag) <= cfg.ClusterThresholdRaw)
                    {
                        clusters[c].Add(novel[i]); placed = true; break;
                    }
                }
                if (!placed) { List<MemoryRecord> nc = new List<MemoryRecord>(); nc.Add(novel[i]); clusters.Add(nc); }
            }

            for (int c = 0; c < clusters.Count; c++)
            {
                List<MemoryRecord> members = clusters[c];
                if (members.Count < cfg.MinClusterSupport) continue;

                // Prototype = the cluster's variance-gated intersection (its shared stable atoms).
                PredictedStats stats = new PredictedStats();
                for (int k = 0; k < members.Count; k++) stats.Fold(members[k].DeltaBag);
                AtomBag prototype = stats.Prediction(meanings.Config.VarianceThresholdRaw, meanings.Config.MinPredictCount);
                if (prototype.Count == 0) continue;                 // no stable shared core — leave novel
                if (meanings.Count >= meanings.Capacity) continue;  // no room to mint

                CategoryId catId = meanings.AddNode(prototype, Fixed.Zero, new Fixed(cfg.MintConfidenceRaw), false);
                for (int k = 0; k < members.Count; k++) meanings.Fold(catId, members[k].DeltaBag);

                for (int k = 0; k < members.Count; k++)
                {
                    MemoryRecord m = members[k];
                    AtomBag newDelta = AtomBag.Diff(m.DeltaBag, prototype);
                    store.Encode(new MemoryRecord(m.Key, catId, newDelta, m.Strength, m.WrittenAt, m.LastRefresh, m.Flags));
                }
            }
        }

        /// <summary>
        /// One full consolidation pass over an agent's record store: RE-DIFF (shed what the facts
        /// now cover) → MINT (compress clustered novelty into new facts) → DECAY (erode by strength,
        /// surprising records resisting). The composition is why confirming episodes dissolve
        /// fastest (their bags re-diff empty, then decay) while a surprising memory stays vivid.
        /// </summary>
        public static void Pass(MemoryStore store, MeaningsStore meanings, in ConsolidationConfig cfg)
        {
            ReDiff(store, meanings);
            Mint(store, meanings, cfg);
            store.Decay(cfg.DecayNormalRate, cfg.DecaySurpriseRate);
        }

        static List<MemoryRecord> Snapshot(MemoryStore store)
        {
            List<MemoryRecord> list = new List<MemoryRecord>(store.Count);
            for (int i = 0; i < store.Count; i++) list.Add(store[i]);
            return list;
        }
    }
}
