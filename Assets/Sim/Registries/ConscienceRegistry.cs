using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Conscience (Atoms what_is_conscience): aversive charges over the agent's
    /// OWN actions — "I shouldn't do X" — keyed by verb (ActivityKind). Read at
    /// the marketplace join (OddSystem.V) to penalise a shamed action, the same
    /// way a drive's valence biases V (the superego as a sign-opposed valence
    /// source, not a separate judge).
    ///
    /// S4 proto-subset: a seeded, personality-scaled begging-shame charge. The
    /// learned installer (altruistic punishment → consolidation into a node), the
    /// taboo hard-cull + sacred upward edge, the guilt pole, and crime-as-tag are
    /// DEFERRED. See docs/cognitive_substrate_S4_conscience.md.
    public sealed class ConscienceData
    {
        public Dictionary<int, double> Charge = new Dictionary<int, double>();   // (int)ActivityKind → aversive charge [0..1]
    }

    /// Per-agent conscience. Writers: the loaders (seed) — and a future installer
    /// system (S4.3). Read by OddSystem.V.
    public sealed class ConscienceRegistry
    {
        readonly ConcurrentDictionary<EntityId, ConscienceData> _d = new ConcurrentDictionary<EntityId, ConscienceData>();

        public void Set(EntityId id, ConscienceData data) => _d[id] = data;
        public bool TryGet(EntityId id, out ConscienceData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, ConscienceData>> All => _d;

        /// The aversive charge on a verb for this agent (0 = no qualm).
        public double ChargeFor(EntityId id, ActivityKind verb)
            => _d.TryGetValue(id, out var data) && data.Charge.TryGetValue((int)verb, out var c) ? c : 0;
    }
}
