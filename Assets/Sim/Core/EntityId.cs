using System;

namespace DaggerfallWorkshop.Sim
{
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public static readonly EntityId None = new EntityId(0);

        public readonly int Value;

        public EntityId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "EntityId.None" : "EntityId(" + Value + ")";

        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
    }
}
