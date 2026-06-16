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
    }

    public sealed class SubjectiveViewData
    {
        /// Attention-ordered, capacity-limited reads of who I currently perceive.
        public List<EntityRead> Entities = new List<EntityRead>();
    }

    /// Per-agent subjective view — the membrane's output and the single
    /// interpretation surface the decider and the interrupt layer both read.
    /// Sole writer: SubjectiveSystem (rebuilt each sense-tick). The decider also
    /// calls SubjectiveSystem.Interpret directly for entities it isn't currently
    /// sensing (a place's remembered occupants).
    public sealed class SubjectiveViewRegistry
    {
        readonly ConcurrentDictionary<EntityId, SubjectiveViewData> _d = new ConcurrentDictionary<EntityId, SubjectiveViewData>();

        public void Set(EntityId id, SubjectiveViewData data) => _d[id] = data;
        public bool TryGet(EntityId id, out SubjectiveViewData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, SubjectiveViewData>> All => _d;
    }
}
