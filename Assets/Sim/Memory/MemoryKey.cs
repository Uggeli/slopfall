using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Store-specific key for a memory record, packed into a long by the caller (position for
    /// PLACES, signature for THINGS, an event tuple for EVENTS). Ordered and equatable so a
    /// store can stay sorted for deterministic iteration and binary-search lookup. No None
    /// sentinel — 0 is a valid key (e.g. tile position (0,0)).
    /// </summary>
    public readonly struct MemoryKey : IEquatable<MemoryKey>, IComparable<MemoryKey>
    {
        public readonly long Value;

        public MemoryKey(long value) { Value = value; }

        public bool Equals(MemoryKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is MemoryKey o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(MemoryKey other) => Value.CompareTo(other.Value);
        public override string ToString() => "MemoryKey(" + Value + ")";

        public static bool operator ==(MemoryKey a, MemoryKey b) => a.Value == b.Value;
        public static bool operator !=(MemoryKey a, MemoryKey b) => a.Value != b.Value;
    }
}
