using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// One agent's MEANINGS store: the bounded set of CategoryNodes that is BOTH the recognition
    /// substrate and the prediction source. Recognition is nearest-prototype (integer L1 distance
    /// over atom bags) within a configurable match threshold; below it, a percept is novel
    /// (maximal surprise). Nodes are held in ascending CategoryId order for deterministic scans.
    /// Eviction/minting are deferred (A5); AddNode throws when full.
    /// </summary>
    public sealed class MeaningsStore
    {
        readonly List<CategoryNode> _nodes;   // ascending CategoryId
        readonly int _capacity;
        int _nextId;

        public MeaningsStore(int capacity, MeaningsConfig config)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            Config = config;
            _nodes = new List<CategoryNode>(capacity);
            _nextId = 1;
        }

        public MeaningsConfig Config { get; }
        public int Count => _nodes.Count;
        public int Capacity => _capacity;
        public CategoryNode this[int i] => _nodes[i];

        /// <summary>Add a category with the next id. Throws when the store is full.</summary>
        public CategoryId AddNode(AtomBag prototype, Fixed valence, Fixed confidence, bool innate)
        {
            if (_nodes.Count >= _capacity)
                throw new InvalidOperationException("MeaningsStore is full (capacity " + _capacity + ")");
            CategoryId id = new CategoryId(_nextId++);
            _nodes.Add(new CategoryNode(id, prototype, valence, confidence, innate));   // ids ascend -> stays sorted
            return id;
        }

        public bool TryGetNode(CategoryId id, out CategoryNode node)
        {
            // Nodes are in ascending id order — binary search.
            int lo = 0, hi = _nodes.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = _nodes[mid].Id.Value.CompareTo(id.Value);
                if (cmp == 0) { node = _nodes[mid]; return true; }
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Recognition: the nearest prototype within MatchThresholdRaw. Scans in ascending id
        /// order and keeps the strictly-smaller distance, so equal-distance ties resolve to the
        /// lowest CategoryId. Returns CategoryId.None when nothing is within threshold (novelty).
        /// </summary>
        public CategoryId Recognize(AtomBag signature)
        {
            CategoryId best = CategoryId.None;
            long bestDist = long.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                long dist = SignatureDistance(signature, _nodes[i].Prototype);
                if (dist <= Config.MatchThresholdRaw && dist < bestDist)
                {
                    bestDist = dist;
                    best = _nodes[i].Id;
                }
            }
            return best;
        }

        /// <summary>
        /// StatFold + valence/confidence update for one category. Folds the percept into the
        /// node's running stats, nudges valence toward the outcome (with a ±1 raw floor so it
        /// converges), and raises confidence on a confirming outcome / lowers it on a
        /// contradicting one (clamped to [0,1]). INNATE nodes learn too. Returns false if absent.
        /// </summary>
        public bool Reinforce(CategoryId id, AtomBag percept, Fixed outcome)
        {
            CategoryNode node;
            if (!TryGetNode(id, out node)) return false;

            node.Predicted.Fold(percept);

            // Valence: integer running mean toward the outcome; ±1 raw minimum step on a non-zero
            // gap so quantization never stalls convergence short of the target.
            int gap = outcome.Raw - node.Valence.Raw;
            int step = gap >> Config.LearnShift;
            if (step == 0 && gap != 0) step = gap > 0 ? 1 : -1;
            int valenceBefore = node.Valence.Raw;
            node.Valence = new Fixed(valenceBefore + step);

            // Confidence: confirming (same sign as the prior valence, or prior valence within the
            // neutral band) raises; contradicting lowers. Clamp to [0, Fixed.One].
            bool neutral = valenceBefore <= Config.NeutralBandRaw && valenceBefore >= -Config.NeutralBandRaw;
            bool confirming = neutral || ((outcome.Raw >= 0) == (valenceBefore >= 0));
            int conf = node.Confidence.Raw + (confirming ? Config.ConfidenceGainRaw : -Config.ConfidenceGainRaw);
            if (conf < 0) conf = 0;
            else if (conf > Fixed.Scale) conf = Fixed.Scale;
            node.Confidence = new Fixed(conf);

            return true;
        }

        /// <summary>Integer L1 (Manhattan) distance between two atom bags: sum of |Δ| over the
        /// union of atom types, a missing type counting as 0 on that side. Deterministic merge
        /// walk over the sorted bags.</summary>
        internal static long SignatureDistance(AtomBag a, AtomBag b)
        {
            long d = 0;
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                int cmp = a[i].Type.CompareTo(b[j].Type);
                if (cmp < 0) { d += Abs(a[i].Value.Raw); i++; }
                else if (cmp > 0) { d += Abs(b[j].Value.Raw); j++; }
                else { d += Abs(a[i].Value.Raw - b[j].Value.Raw); i++; j++; }
            }
            while (i < a.Count) { d += Abs(a[i].Value.Raw); i++; }
            while (j < b.Count) { d += Abs(b[j].Value.Raw); j++; }
            return d;
        }

        static long Abs(int x) { long v = x; return v < 0 ? -v : v; }
    }
}
