using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    public enum EntityKind
    {
        Unknown,
        Player,
        EnemyClass,
        EnemyMonster,
        CivilianNPC,
        StaticNPC,
    }

    /// One row of identity. Held by reference so updates are atomic (whole-row swap).
    public sealed class IdentityData
    {
        public string Name;
        public EntityKind Kind;
        public int Race;          // PlayerEntity.Race cast to int; -1 for non-player
        public int Gender;        // Genders enum value
        public int CareerIndex;   // EnemyEntity.CareerIndex; -1 for non-enemy
        public int Level;
        public int FactionId;
        public int Team;          // MobileTeams enum value
    }

    /// Who exists. EntityId allocator + lookup.
    /// Writer in Phase 1: SimMirror (main thread). Reads safe from any thread.
    public sealed class IdentityRegistry
    {
        readonly ConcurrentDictionary<EntityId, IdentityData> _d = new ConcurrentDictionary<EntityId, IdentityData>();
        int _nextId;
        int _playerIdValue;

        public EntityId Allocate() => new EntityId(Interlocked.Increment(ref _nextId));

        public void Set(EntityId id, IdentityData data) => _d[id] = data;
        public bool TryGet(EntityId id, out IdentityData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public ICollection<EntityId> Ids => _d.Keys;
        public IEnumerable<KeyValuePair<EntityId, IdentityData>> All => _d;

        /// Cached lookup for "which EntityId is the player." EntityId.None if no
        /// Player-kind entity is currently registered. Single-writer = whichever
        /// SimMirror registered the Player.
        public EntityId PlayerId => new EntityId(Volatile.Read(ref _playerIdValue));

        public void SetPlayer(EntityId id) => Interlocked.Exchange(ref _playerIdValue, id.Value);

        /// Clear only if the stored player matches `id` (so a stale despawn doesn't
        /// clobber a newer player registration).
        public void ClearPlayer(EntityId id) => Interlocked.CompareExchange(ref _playerIdValue, 0, id.Value);
    }
}
