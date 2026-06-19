using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One directed opinion: how `owner` sees `other`. Atoms vocabulary: the
    /// target-field half of a directed social drive — per-entity valence reads,
    /// no level of their own.
    public sealed class RelationData
    {
        /// 0..1 — how well the owner knows this person. Grows with shared time.
        public double Familiarity;
        /// -1..1 — how the owner feels about them. Signed by pair affinity;
        /// personalities will replace the affinity hash later.
        public double Regard;
        /// Friendship announced once when Regard and Familiarity cross the bar.
        public bool FriendAnnounced;
    }

    public sealed class RelationsData
    {
        public Dictionary<EntityId, RelationData> Of = new Dictionary<EntityId, RelationData>();
    }
}
