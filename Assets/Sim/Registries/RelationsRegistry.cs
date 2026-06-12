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

    /// Per-entity directed opinions about other entities. Sole writer:
    /// SocialSystem (whole-row replacement). This is the substrate NPC
    /// opinions, gossip, and emergent quest seeds grow from.
    public sealed class RelationsRegistry
    {
        readonly ConcurrentDictionary<EntityId, RelationsData> _d = new ConcurrentDictionary<EntityId, RelationsData>();

        public void Set(EntityId id, RelationsData data) => _d[id] = data;
        public bool TryGet(EntityId id, out RelationsData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, RelationsData>> All => _d;
    }
}
