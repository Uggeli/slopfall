using System.Collections.Generic;
using System.Threading;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of IdentityRegistry. Reuses IdentityData / EntityKind from the
    // DaggerfallWorkshop.Sim namespace. The identity ROW is applied from intents in
    // Update(); the EntityId allocator and the player-id pointer remain direct
    // (synchronous) operations, exactly as the old registry exposed them — they are
    // allocation/lookup concerns, not tick-buffered data the registry "owns".

    /// Intent: "set entity's identity row." Writers (TownLoader, SimMirror,
    /// CreatureSystem, ProgressionSystem) always build a whole IdentityData, so the
    /// intent carries the whole row.
    public struct IdentitySetIntent : IEvent
    {
        public EntityId Id;
        public IdentityData Data;
    }

    /// Who exists. EntityId allocator + lookup.
    public sealed class IdentityRegistry : Registry
    {
        readonly Dictionary<EntityId, IdentityData> _d = new Dictionary<EntityId, IdentityData>();
        int _nextId;
        int _playerIdValue;

        public IdentityRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (var i in Events.GetEvents<IdentitySetIntent>())
                _d[i.Id] = i.Data;

            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }

        /// EntityId allocator — kept as the Interlocked counter (callers allocate an id
        /// synchronously, then emit an IdentitySetIntent for the row).
        public EntityId Allocate() => new EntityId(Interlocked.Increment(ref _nextId));

        /// Load-time direct write (load runs before ticking, single-threaded). Used by
        /// the ARENA2 loaders, which both write and read back this store mid-load.
        public void Seed(EntityId id, IdentityData data) => _d[id] = data;

        /// Load-time: advance the allocator past the highest seeded id so post-load
        /// spawns (creatures, immigrants) don't collide with loaded entities.
        public void SeedNextId(int maxId) { if (maxId > _nextId) _nextId = maxId; }

        public bool TryGet(EntityId id, out IdentityData data) => _d.TryGetValue(id, out data);

        public int Count => _d.Count;
        public ICollection<EntityId> Ids => _d.Keys;
        public IEnumerable<KeyValuePair<EntityId, IdentityData>> All => _d;

        /// Cached lookup for "which EntityId is the player." EntityId.None if no
        /// Player-kind entity is currently registered.
        public EntityId PlayerId => new EntityId(Volatile.Read(ref _playerIdValue));

        public void SetPlayer(EntityId id) => Interlocked.Exchange(ref _playerIdValue, id.Value);

        /// Clear only if the stored player matches `id` (so a stale despawn doesn't
        /// clobber a newer player registration).
        public void ClearPlayer(EntityId id) => Interlocked.CompareExchange(ref _playerIdValue, 0, id.Value);
    }
}
