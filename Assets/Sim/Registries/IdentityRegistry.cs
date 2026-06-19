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
}
