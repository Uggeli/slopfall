using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Deterministic fixed-point scalar: int32 backing, 8 fractional bits (Scale = 256).
    /// The 8 fractional bits match the memory spec's "8.8" mean storage; the int32 backing
    /// gives integer-part headroom. All runtime arithmetic is integer — no float ever enters
    /// the Memory subsystem (spec: Determinism). FromDouble/ToDouble exist only for authoring
    /// and tests at the boundary.
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>
    {
        public const int FractionalBits = 8;
        public const int Scale = 1 << FractionalBits;   // 256

        public static readonly Fixed Zero = new Fixed(0);
        public static readonly Fixed One = new Fixed(Scale);

        public readonly int Raw;

        public Fixed(int raw) { Raw = raw; }

        public static Fixed FromInt(int whole) => new Fixed(whole * Scale);

        /// <summary>Round-to-nearest, ties away from zero — identical on every machine.</summary>
        public static Fixed FromDouble(double v)
        {
            double scaled = v * Scale;
            int r = (int)(scaled >= 0 ? scaled + 0.5 : scaled - 0.5);
            return new Fixed(r);
        }

        public double ToDouble() => (double)Raw / Scale;

        public static Fixed operator +(Fixed a, Fixed b) => new Fixed(a.Raw + b.Raw);
        public static Fixed operator -(Fixed a, Fixed b) => new Fixed(a.Raw - b.Raw);
        public static Fixed operator -(Fixed a) => new Fixed(-a.Raw);

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fixed o && Equals(o);
        public override int GetHashCode() => Raw;
        public override string ToString() => ToDouble().ToString("0.###");

        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    }
}
