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

    /// Per-agent semantic store. Sole writer: MeaningsSystem (folds interactions
    /// into categories, decays them). Read by SubjectiveSystem.Interpret to judge
    /// a STRANGER by their kind when no individual dossier read exists.
    public sealed class MeaningsRegistry
    {
        readonly ConcurrentDictionary<EntityId, MeaningsData> _d = new ConcurrentDictionary<EntityId, MeaningsData>();

        public void Set(EntityId id, MeaningsData data) => _d[id] = data;
        public bool TryGet(EntityId id, out MeaningsData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, MeaningsData>> All => _d;
    }
}
