using System;

namespace DaggerfallWorkshop.Sim
{
    /// An opaque handle for who *owns* a treasury (and, later, property). v1 has a
    /// single sentinel — Town — standing in for the local authority that taxes and
    /// pays guards. The whole point of the handle is the seam: when factions land
    /// (E3+), this value simply *becomes* a faction id (KnightlyGuard, Courts, a
    /// temple), a data change rather than a rewrite. Nothing downstream hardcodes a
    /// global-singleton treasury, so multiple owners (crown, guild, temple) drop in
    /// for free.
    public readonly struct OwnerId : IEquatable<OwnerId>
    {
        public static readonly OwnerId None = new OwnerId(0);
        public static readonly OwnerId Town = new OwnerId(1);   // sentinel local authority; later a faction id

        public readonly int Value;

        public OwnerId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(OwnerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is OwnerId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "OwnerId.None" : "OwnerId(" + Value + ")";

        public static bool operator ==(OwnerId a, OwnerId b) => a.Value == b.Value;
        public static bool operator !=(OwnerId a, OwnerId b) => a.Value != b.Value;
    }
}
