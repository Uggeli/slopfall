using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Per-AtomType running statistics for one category — the mechanical form of "the semantic
    /// fact is the intersection of the episodes." Fold() adds a percept's atoms (integer
    /// count/sum/sumsq, commutative). Prediction() emits only the LOW-SPREAD atom types: a fox
    /// is always 'fox' and 'chase' (low variance -> predicted), but each fox sits in a different
    /// spot on a different day (high variance -> never predicted). The intersection is
    /// variance-gated running statistics, not set-intersection.
    /// </summary>
    public sealed class PredictedStats
    {
        readonly Dictionary<int, RunningStat> _byType = new Dictionary<int, RunningStat>();

        public int TypeCount => _byType.Count;

        /// <summary>Add a percept's atoms to the running stats. Order-independent (adds commute).</summary>
        public void Fold(AtomBag percept)
        {
            for (int i = 0; i < percept.Count; i++)
            {
                Atom a = percept[i];
                RunningStat stat;
                _byType.TryGetValue(a.Type.Value, out stat);   // default(RunningStat) if absent
                stat.Add(a.Value);
                _byType[a.Type.Value] = stat;
            }
        }

        public bool TryGetStat(AtomTypeId type, out RunningStat stat)
            => _byType.TryGetValue(type.Value, out stat);

        /// <summary>
        /// The variance-gated intersection: an AtomBag of (type, mean) for every atom type with
        /// at least minCount samples AND spread (VarianceRaw, Q16) at or below varianceThresholdRaw.
        /// Built in AtomTypeId order (deterministic).
        /// </summary>
        public AtomBag Prediction(long varianceThresholdRaw, int minCount)
        {
            // Collect qualifying type ids, then sort for deterministic output.
            List<int> types = new List<int>();
            foreach (KeyValuePair<int, RunningStat> kv in _byType)
            {
                RunningStat s = kv.Value;
                if (s.Count >= minCount && s.VarianceRaw() <= varianceThresholdRaw)
                    types.Add(kv.Key);
            }
            if (types.Count == 0) return AtomBag.Empty;
            types.Sort();

            List<Atom> atoms = new List<Atom>(types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                RunningStat s = _byType[types[i]];
                atoms.Add(new Atom(new AtomTypeId(types[i]), s.Mean()));
            }
            return AtomBag.Create(atoms);
        }
    }
}
