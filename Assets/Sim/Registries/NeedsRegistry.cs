using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// V-vector axes, ported from ODD's StateAxis convention: every axis is a
    /// DEFICIT that drifts up over time and gets pushed back toward its
    /// setpoint (0) by activities. Uniform direction keeps the gap math
    /// branch-free. Fear is the first DIRECTED drive (V2b): its scalar deficit is
    /// the Max-projection of a target field over perceived threats, computed by
    /// NeedsSystem's fear controller rather than drifted.
    public static class NeedAxis
    {
        public const int Hunger    = 0;
        public const int EnergyDef = 1;   // tiredness
        public const int SocialDef = 2;   // loneliness
        public const int CoinDef   = 3;   // poverty pressure
        public const int GoodsDef  = 4;   // household provisions running low → drives shopping
        public const int Fear      = 5;   // safety: a directed drive over perceived threats (V2b)
        public const int Attire    = 6;   // clothing wearing out → drives textile work (in-kind) and clothes-buying; the demand that gives the textile sector a livelihood
        public const int Count     = 7;
    }

    public sealed class NeedsData
    {
        /// Deficit per axis, 0 = satisfied. Soft range [0..1]; drift clamps at
        /// 1.5 so an extreme value can still express urgency in scoring.
        public double[] V = new double[NeedAxis.Count];
    }
}
