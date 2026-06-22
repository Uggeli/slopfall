namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Knobs for the sleep consolidation pass. Decay rates feed MemoryStore.Decay (surprise resists);
    /// the cluster threshold + min support govern MINT; mint confidence seeds a freshly minted node.
    /// All are p6 tuning knobs, not architecture; defaults are scaffolding order-of-magnitude values.
    /// </summary>
    public readonly struct ConsolidationConfig
    {
        public readonly int DecayNormalRate;       // strength lost per pass by ordinary records
        public readonly int DecaySurpriseRate;     // (smaller) loss for SURPRISE-flagged records
        public readonly long ClusterThresholdRaw;  // MINT: max L1 distance to join a cluster (Q8 sum)
        public readonly int MinClusterSupport;     // MINT: members needed to mint a node
        public readonly int MintConfidenceRaw;     // confidence (Fixed raw) of a freshly minted node

        public ConsolidationConfig(int decayNormalRate, int decaySurpriseRate, long clusterThresholdRaw,
                                   int minClusterSupport, int mintConfidenceRaw)
        {
            DecayNormalRate = decayNormalRate;
            DecaySurpriseRate = decaySurpriseRate;
            ClusterThresholdRaw = clusterThresholdRaw;
            MinClusterSupport = minClusterSupport;
            MintConfidenceRaw = mintConfidenceRaw;
        }

        // Scaffolding: decay 20/pass (5 for surprising); cluster within ~0.25 L1; 3-member support;
        // minted nodes start at confidence 0 — confidence tracks LEARNED FEELING (raised only by
        // Reinforce), so recognizing a kind alone never leans the Interpret valence-blend.
        public static readonly ConsolidationConfig Default = new ConsolidationConfig(20, 5, 64, 3, 0);
    }
}
