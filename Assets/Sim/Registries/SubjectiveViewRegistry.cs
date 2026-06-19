using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One interpreted read of a perceived entity — Atoms row 2, the output of
    /// interpret(): what the perceived thing MEANS to the perceiver, not what it
    /// objectively is. S1 fills valence from the dossier and recognition/trust
    /// from familiarity; S2 will bias attention with affect; S3 will source
    /// valence/recognition/trust from the MEANINGS store. See
    /// docs/cognitive_substrate_S1_membrane.md.
    public struct EntityRead
    {
        public EntityId Other;
        public double Valence;       // good/bad TO ME
        public double Recognition;   // 0 stranger .. 1 well-known
        public double Trust;         // how much I credit the read
        public double Attention;     // salience this tick (capacity-limited top-K)
        public double Threat;        // 0 = not a threat .. 1 = a clear danger (the fear drive's target field, V2)
    }

    public sealed class SubjectiveViewData
    {
        /// Attention-ordered, capacity-limited reads of who I currently perceive.
        public List<EntityRead> Entities = new List<EntityRead>();
    }
}
