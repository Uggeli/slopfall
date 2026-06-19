using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One learned category — the compression-dictionary entry interpret() looks
    /// up (Atoms memory doc: "the MEANINGS store IS the interpretation
    /// substrate"). S3 proto-subset: a node carries a running valence + a
    /// confidence, keyed by a Signature (here: the role — keeper/resident/guard).
    /// The full memory-doc machinery (atom-prediction stats, delta-bags, MINT/
    /// split, eviction, false-memory) is deferred. See
    /// docs/cognitive_substrate_S3_meanings.md.
    public sealed class CategoryNode
    {
        public int Signature;
        public double Valence;       // running mean of outcomes with this kind: appetitive/aversive
        public double Confidence;    // 0..1 — how sure (weights how much it colors a read)
    }

    public sealed class MeaningsData
    {
        public Dictionary<int, CategoryNode> Nodes = new Dictionary<int, CategoryNode>();
    }
}
