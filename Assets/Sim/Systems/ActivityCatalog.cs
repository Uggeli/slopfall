namespace DaggerfallWorkshop.Sim
{
    /// Static authored data for town activities: durations, promised V deltas,
    /// Object-Zero base utilities. The ODD testbed keeps the same shape in
    /// src/Data/Actions.cs (actions promise deltas; magnitudes live in tables).
    /// Atoms vocabulary: every axis here is a deplete-model deficiency pole;
    /// growth drives (curiosity, play) and prepotency gating arrive later.
    public static class ActivityCatalog
    {
        public sealed class Spec
        {
            public ActivityKind Kind;
            public double DurationMinutes;
            /// Promised total V change over a full activity (negative reduces
            /// the deficit). Indexed by NeedAxis.
            public double[] Delta = new double[NeedAxis.Count];
            /// Object-Zero liveness floor: a tiny always-positive utility so
            /// the agent never has literally nothing worth doing.
            public double BaseUtility;
        }

        public static readonly Spec Idle = new Spec
        {
            Kind = ActivityKind.Idle,
            DurationMinutes = 20,
            BaseUtility = 0.002,
        };

        public static readonly Spec Wander = new Spec
        {
            Kind = ActivityKind.Wander,
            DurationMinutes = 30,
            Delta = Deltas(socialDef: -0.05, energyDef: +0.03),
            BaseUtility = 0.004,
        };

        public static readonly Spec Sleep = new Spec
        {
            Kind = ActivityKind.Sleep,
            DurationMinutes = 480,
            Delta = Deltas(energyDef: -1.0),
        };

        public static readonly Spec Work = new Spec
        {
            Kind = ActivityKind.Work,
            DurationMinutes = 180,
            Delta = Deltas(coinDef: -0.3, energyDef: +0.1),
        };

        public static readonly Spec EatHome = new Spec
        {
            Kind = ActivityKind.EatHome,
            DurationMinutes = 30,
            Delta = Deltas(hunger: -0.5),
        };

        public static readonly Spec EatTavern = new Spec
        {
            Kind = ActivityKind.EatTavern,
            DurationMinutes = 45,
            Delta = Deltas(hunger: -0.8, coinDef: +0.08, socialDef: -0.1),
        };

        public static readonly Spec Socialize = new Spec
        {
            Kind = ActivityKind.Socialize,
            DurationMinutes = 90,
            Delta = Deltas(socialDef: -0.6, coinDef: +0.04, hunger: +0.05),
        };

        /// Growth drive (Atoms B-need): no setpoint, engaging is the reward.
        /// BaseUtility carries the whole score — the tiny SocialDef delta just
        /// reflects being out among people. Prepotency-gated in OddSystem.
        public static readonly Spec Visit = new Spec
        {
            Kind = ActivityKind.Visit,
            DurationMinutes = 45,
            Delta = Deltas(socialDef: -0.1, energyDef: +0.02),
            BaseUtility = 0.05,
        };

        /// Per-axis scoring weights, ported from ODD's WeightsRegistry idea as
        /// global defaults; per-agent weights become personality later.
        public static readonly double[] Weights = { 1.2, 1.0, 0.6, 0.5 };

        /// Need drift per game HOUR — the poles ticking up (Atoms: Metabolism).
        /// CoinDef has no drift: it derives from real money (CoinRegistry);
        /// the cost of living is an actual coin sink in EconomySystem. The
        /// CoinDef deltas in the specs above remain as scoring PROMISES whose
        /// real rates EconomySystem implements as transfers.
        public static readonly double[] DriftPerHour = { 0.04, 0.05, 0.03, 0.0 };

        public const double VMax = 1.5;

        static double[] Deltas(double hunger = 0, double energyDef = 0, double socialDef = 0, double coinDef = 0)
        {
            var d = new double[NeedAxis.Count];
            d[NeedAxis.Hunger] = hunger;
            d[NeedAxis.EnergyDef] = energyDef;
            d[NeedAxis.SocialDef] = socialDef;
            d[NeedAxis.CoinDef] = coinDef;
            return d;
        }
    }

    /// The scoring core, ported intact from ODD's Score.Compute:
    ///   gap   = ||V − setpoints||_W − ||V + Δ − setpoints||_W   (weighted L2)
    ///   score = max(0, αᵉ · gap) · timeGate + baseUtility
    /// Setpoints are uniformly 0 because every axis is a deficit. The trait
    /// (αᵗ · trait·Sig) and risk-variance terms are deferred until civilians
    /// have personalities; the time gate stands in for ODD's outer modulators.
    public static class OddScore
    {
        public const double AlphaE = 1.0;

        public static double Compute(double[] v, double[] delta, double[] weights, double timeGate, double baseUtility)
        {
            double devNorm2 = 0, nextNorm2 = 0;
            for (int i = 0; i < v.Length; i++)
            {
                double dev = v[i];
                double next = v[i] + delta[i];
                if (next < 0) next = 0;     // an activity can't push a deficit below satisfied
                devNorm2 += weights[i] * dev * dev;
                nextNorm2 += weights[i] * next * next;
            }

            double gap = System.Math.Sqrt(devNorm2) - System.Math.Sqrt(nextNorm2);
            double score = AlphaE * gap;
            if (score < 0) score = 0;
            return score * timeGate + baseUtility;
        }
    }
}
