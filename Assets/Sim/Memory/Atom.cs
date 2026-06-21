using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// One (type, value) pair — the unit stored in an AtomBag. Value is fixed-point so the
    /// whole Memory subsystem stays float-free and replay-exact.
    /// </summary>
    public readonly struct Atom : IEquatable<Atom>
    {
        public readonly AtomTypeId Type;
        public readonly Fixed Value;

        public Atom(AtomTypeId type, Fixed value) { Type = type; Value = value; }

        public bool Equals(Atom other) => Type == other.Type && Value == other.Value;
        public override bool Equals(object obj) => obj is Atom o && Equals(o);
        public override int GetHashCode() => unchecked((Type.GetHashCode() * 397) ^ Value.GetHashCode());
        public override string ToString() => Type + "=" + Value;
    }
}
