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
}
