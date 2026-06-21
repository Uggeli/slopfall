using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Immutable map from AtomTypeId to Fixed value, stored as an array sorted ascending by
    /// AtomTypeId for deterministic iteration and binary-search lookup. The shared substrate
    /// behind percepts, predictions, and memory delta-bags.
    /// </summary>
    public sealed class AtomBag
    {
        public static readonly AtomBag Empty = new AtomBag(Array.Empty<Atom>());

        readonly Atom[] _atoms;   // sorted ascending by Type.Value, unique types

        AtomBag(Atom[] sortedUnique) { _atoms = sortedUnique; }

        public int Count => _atoms.Length;
        public Atom this[int i] => _atoms[i];
        public IReadOnlyList<Atom> Atoms => _atoms;

        /// <summary>Build a bag from atoms in any order. Throws on a duplicate AtomTypeId
        /// (a bag is a map: each type appears at most once).</summary>
        public static AtomBag Create(IEnumerable<Atom> atoms)
        {
            var list = new List<Atom>(atoms);
            list.Sort((a, b) => a.Type.CompareTo(b.Type));
            for (int i = 1; i < list.Count; i++)
                if (list[i].Type == list[i - 1].Type)
                    throw new ArgumentException("Duplicate AtomTypeId in bag: " + list[i].Type);
            return list.Count == 0 ? Empty : new AtomBag(list.ToArray());
        }

        /// <summary>Binary-search lookup by type. O(log n).</summary>
        public bool TryGet(AtomTypeId type, out Fixed value)
        {
            int lo = 0, hi = _atoms.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = _atoms[mid].Type.CompareTo(type);
                if (cmp == 0) { value = _atoms[mid].Value; return true; }
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            value = Fixed.Zero;
            return false;
        }

        public bool Contains(AtomTypeId type) => TryGet(type, out _);

        /// <summary>
        /// Reconstruct: predicted ⊕ delta. Sorted union of both bags; where both contain a
        /// type, the delta's value wins (recall = category prediction overlaid with the
        /// episode's stored divergences). Inputs are sorted, so the merge output is too.
        /// </summary>
        public static AtomBag Merge(AtomBag predicted, AtomBag delta)
        {
            var p = predicted._atoms;
            var d = delta._atoms;
            var result = new List<Atom>(p.Length + d.Length);
            int i = 0, j = 0;
            while (i < p.Length && j < d.Length)
            {
                int cmp = p[i].Type.CompareTo(d[j].Type);
                if (cmp < 0) result.Add(p[i++]);
                else if (cmp > 0) result.Add(d[j++]);
                else { result.Add(d[j]); i++; j++; }   // both speak -> delta wins
            }
            while (i < p.Length) result.Add(p[i++]);
            while (j < d.Length) result.Add(d[j++]);
            return result.Count == 0 ? Empty : new AtomBag(result.ToArray());
        }

        /// <summary>
        /// The delta of a percept against a prediction: every percept atom whose type is
        /// absent from the prediction, or whose value differs from the prediction's. Atoms
        /// present only in the prediction are excluded — the diff is the percept's divergence,
        /// the minimal content a memory record must store. Percept is sorted, so output is too.
        /// </summary>
        public static AtomBag Diff(AtomBag percept, AtomBag prediction)
        {
            var result = new List<Atom>(percept._atoms.Length);
            foreach (var a in percept._atoms)
            {
                if (!prediction.TryGet(a.Type, out var pv) || pv != a.Value)
                    result.Add(a);
            }
            return result.Count == 0 ? Empty : new AtomBag(result.ToArray());
        }
    }
}
