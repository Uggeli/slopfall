using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Stable identifier for a kind of atom — the key in an AtomBag's type→value map.
    /// Ordered so bags can be kept sorted for deterministic, binary-searchable storage.
    /// </summary>
    public readonly struct AtomTypeId : IEquatable<AtomTypeId>, IComparable<AtomTypeId>
    {
        public static readonly AtomTypeId None = new AtomTypeId(0);

        public readonly int Value;

        public AtomTypeId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(AtomTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AtomTypeId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(AtomTypeId other) => Value.CompareTo(other.Value);
        public override string ToString() => IsNone ? "AtomTypeId.None" : "AtomTypeId(" + Value + ")";

        public static bool operator ==(AtomTypeId a, AtomTypeId b) => a.Value == b.Value;
        public static bool operator !=(AtomTypeId a, AtomTypeId b) => a.Value != b.Value;
    }
}
