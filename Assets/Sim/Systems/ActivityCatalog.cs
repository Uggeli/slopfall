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
            /// the deficit). Indexed by NeedAxis. (The spec's "prediction".)
            public double[] Delta = new double[NeedAxis.Count];
            /// Object-Zero liveness floor: a tiny always-positive utility so
            /// the agent never has literally nothing worth doing.
            public double BaseUtility;

            // --- Preconditions (hard yes/no; gate Collect in ActionDiscovery) ---
            public int OpenHour = 0, CloseHour = 24;    // active window [open, close)
            public bool KeeperOnly = false;
            public bool HolidayCloses = false;

            // --- Sale (G3): a B2C transaction that draws a real good off the
            //     seller's shelf. SaleUnits>0 means the patron buys SaleUnits of
            //     the building's sale good (GoodsCatalog.SaleGoodFor) over a full
            //     activity, paying SalePrice/unit to the keeper. If SaleReliefAxis
            //     ≥ 0 the activity is GATED on stock: with the shelf empty it isn't
            //     offered, and the relief on that axis doesn't accrue (no goods, no
            //     satisfaction). SaleReliefAxis = −1 = ungated (drink at a tavern:
            //     bought if available, but you can still gather with no ale). -->
            public double SaleUnits = 0;
            public double SalePrice = 0;
            public int SaleReliefAxis = -1;

            // --- V modulators (soft; the uniform value function reads these) ---
            public double BaseGate = 1.0;               // flat gate multiplier (duty, etc.)
            public double DayGate = 1.0, NightGate = 1.0;
            public double DistanceScale = 0;            // 0 = no distance penalty; else 1/(1+dist/scale)
            public bool Outdoor = false;                // gate × weather (storms damp)
            public bool Social = false;                 // gate × liveliness × evening × cozy
            public bool Prepotent = false;              // gate × prepotency (leisure/growth)
            public double HolidayFactor = 1.0;          // gate × this when a holiday is active
            public int Trait = -1;                      // TraitIndex this activity couples to, or -1
            public double TraitBias = 1.0, TraitScale = 0.0, TraitExp = 1.0;  // factor = bias + scale·trait^exp
            public bool TraitOnBase = false;            // apply the trait factor to base (else to gate)
            public bool Growth = false;                 // base scaled by gate (no setpoint, e.g. Visit)
            public double NightBase = 0;                // base added at night (circadian)
            public double WetBase = 0;                  // base added in foul weather (shelter pull)
        }

        /// Hard preconditions — does the world permit this activity at all right
        /// now? Gates Collect (ActionDiscovery), separate from V's soft scoring.
        public static bool PreconditionsMet(Spec s, int hour, bool holidayActive, bool isKeeper)
        {
            if (hour < s.OpenHour || hour >= s.CloseHour) return false;
            if (s.KeeperOnly && !isKeeper) return false;
            if (s.HolidayCloses && holidayActive) return false;
            return true;
        }

        public static readonly Spec Idle = new Spec
        {
            Kind = ActivityKind.Idle,
            DurationMinutes = 20,
            BaseUtility = 0.002,
            WetBase = 0.03,                 // shelter pull: idle wins when it's foul out
        };

        public static readonly Spec Wander = new Spec
        {
            Kind = ActivityKind.Wander,
            DurationMinutes = 30,
            Delta = Deltas(socialDef: -0.05, energyDef: +0.03),
            BaseUtility = 0.004,
            NightGate = 0.3,                // ambling is a daytime thing
            Outdoor = true,
            // Restless souls crave it (steep curve on the base); homebodies never
            // bother. The strong base is what keeps restless owls out at night.
            Trait = TraitIndex.Restlessness, TraitBias = 0.2, TraitScale = 16.0, TraitExp = 2.0, TraitOnBase = true,
        };

        public static readonly Spec Sleep = new Spec
        {
            Kind = ActivityKind.Sleep,
            DurationMinutes = 480,
            Delta = Deltas(energyDef: -1.0),
            DayGate = 0.12, NightGate = 2.0,
            NightBase = 0.06,               // circadian pull that holds a rested sleeper in bed
        };

        public static readonly Spec Work = new Spec
        {
            Kind = ActivityKind.Work,
            DurationMinutes = 180,
            Delta = Deltas(coinDef: -0.4, energyDef: +0.1),
            OpenHour = 8, CloseHour = 18, KeeperOnly = true, HolidayCloses = true,
            BaseGate = 1.3,                 // duty: hold shop through the day even with a full purse
            BaseUtility = 0.05,             // duty pull, strong enough to beat idle-day leisure
            Trait = TraitIndex.Industry, TraitBias = 0.7, TraitScale = 0.6, TraitExp = 1.0, TraitOnBase = true,
        };

        /// Residents' day-work: home-based piecework (washing, crafting,
        /// portering) that earns a modest wage. Purely coin-need-driven (no duty
        /// base) — poor residents do it, then escape to leisure once comfortable.
        /// The income faucet the underclass never had.
        public static readonly Spec Labor = new Spec
        {
            Kind = ActivityKind.Labor,
            DurationMinutes = 120,
            Delta = Deltas(coinDef: -0.15, energyDef: +0.08),
            OpenHour = 8, CloseHour = 18,
            Trait = TraitIndex.Industry, TraitBias = 0.7, TraitScale = 0.6, TraitExp = 1.0,   // on the gate
        };

        /// Farming the settlement's fields — primary food production, out at the
        /// fields (a distinct place from home/shop). Coin-need-driven like labor; a
        /// long day, dawn to dusk. No distance penalty: it's the one workplace its
        /// hands have, so they go however far it is.
        public static readonly Spec Farm = new Spec
        {
            Kind = ActivityKind.Farm,
            DurationMinutes = 180,
            Delta = Deltas(coinDef: -0.15, energyDef: +0.10),
            OpenHour = 6, CloseHour = 19,
            Trait = TraitIndex.Industry, TraitBias = 0.7, TraitScale = 0.6, TraitExp = 1.0,
        };

        /// Fishing the coast — primary food production, out at the shore (a distinct
        /// place). Early start (best catch at dawn); otherwise like farming.
        public static readonly Spec Fish = new Spec
        {
            Kind = ActivityKind.Fish,
            DurationMinutes = 180,
            Delta = Deltas(coinDef: -0.15, energyDef: +0.12),
            OpenHour = 5, CloseHour = 17,
            Trait = TraitIndex.Industry, TraitBias = 0.7, TraitScale = 0.6, TraitExp = 1.0,
        };

        public static readonly Spec EatHome = new Spec
        {
            Kind = ActivityKind.EatHome,
            DurationMinutes = 30,
            Delta = Deltas(hunger: -0.5),
            NightGate = 0.15,               // kitchens mostly cold in the small hours
        };

        public static readonly Spec EatTavern = new Spec
        {
            Kind = ActivityKind.EatTavern,
            DurationMinutes = 45,
            Delta = Deltas(hunger: -0.8, coinDef: +0.08, socialDef: -0.1),
            OpenHour = 6, CloseHour = 23,
            DistanceScale = 150,
            // A meal = provisions transformed and served; gated on the tavern
            // having provisions. SaleUnits×SalePrice (1×0.08) matches the coinDef
            // promise; the price tops raw-provisions retail (0.05) — the transform's
            // value-add.
            SaleUnits = 1, SalePrice = 0.08, SaleReliefAxis = NeedAxis.Hunger,
        };

        public static readonly Spec Socialize = new Spec
        {
            Kind = ActivityKind.Socialize,
            DurationMinutes = 90,
            Delta = Deltas(socialDef: -0.6, coinDef: +0.04, hunger: +0.05),
            OpenHour = 6, CloseHour = 23,
            DistanceScale = 150,
            Social = true, Prepotent = true, HolidayFactor = 1.5,
            Trait = TraitIndex.Sociability, TraitBias = 0.7, TraitScale = 0.6, TraitExp = 1.0,   // on the gate
            // A round of drink — bought if the tavern has any (revenue), but the
            // gathering (and its social relief) isn't gated on it: a dry tavern is
            // still a social hall. So SaleReliefAxis stays −1 (ungated).
            SaleUnits = 1, SalePrice = 0.04,
        };

        /// Growth drive (Atoms B-need): no setpoint, engaging is the reward, so
        /// the base carries the score and is scaled by the gate.
        public static readonly Spec Visit = new Spec
        {
            Kind = ActivityKind.Visit,
            DurationMinutes = 45,
            Delta = Deltas(socialDef: -0.1, energyDef: +0.02),
            BaseUtility = 0.05,
            OpenHour = 7, CloseHour = 21,
            DistanceScale = 200,
            Outdoor = true, Prepotent = true, HolidayFactor = 1.3, Growth = true,
            // At a service institution (temple/guild/bank) a visit is patronage —
            // an offering / dues / fee to the keeper (G5). UNGATED: you may worship
            // or call even with an empty purse (the poor visit free, and receive),
            // so this funds the institution without barring anyone. Free at a
            // library/palace (GoodsCatalog.IsPaidService says where it costs).
            SaleUnits = 1, SalePrice = 0.05,
        };

        /// Shopping: refill household provisions at a store, paying the keeper.
        /// The demand that gives shops real customers (and revenue) — closing the
        /// money loop so keeper income comes from sales, not a minted wage.
        public static readonly Spec Buy = new Spec
        {
            Kind = ActivityKind.Buy,
            DurationMinutes = 30,
            Delta = Deltas(goodsDef: -0.3, coinDef: +0.1),
            OpenHour = 8, CloseHour = 18,
            DistanceScale = 150,
            // Buying the shop's wares at retail; gated on the shelf. The good is
            // whatever this shop sells (provisions at a general store, wares at a
            // craftsman) — GoodsCatalog.SaleGoodFor. 2×0.05 matches coinDef +0.1.
            SaleUnits = 2, SalePrice = 0.05, SaleReliefAxis = NeedAxis.GoodsDef,
        };

        /// Street greeting between friends — entered by interrupt (rung 2),
        /// never chosen by the marketplace.
        public static readonly Spec Chat = new Spec
        {
            Kind = ActivityKind.Chat,
            DurationMinutes = 8,
            Delta = Deltas(socialDef: -0.15),
        };

        /// Walking to a mark to ask for alms — entered via RequestSystem,
        /// never chosen by the marketplace. The "conversation" on arrival.
        public static readonly Spec SeekHelp = new Spec
        {
            Kind = ActivityKind.SeekHelp,
            DurationMinutes = 5,
        };

        /// Per-axis scoring weights, ported from ODD's WeightsRegistry idea as
        /// global defaults; per-agent weights become personality later.
        public static readonly double[] Weights = { 1.2, 1.0, 0.6, 0.5, 0.5 };

        /// Need drift per game HOUR — the poles ticking up (Atoms: Metabolism).
        /// CoinDef has no drift: it derives from real money (CoinRegistry);
        /// the cost of living is an actual coin sink in EconomySystem. The
        /// CoinDef deltas in the specs above remain as scoring PROMISES whose
        /// real rates EconomySystem implements as transfers.
        public static readonly double[] DriftPerHour = { 0.04, 0.05, 0.03, 0.0, 0.03 };

        public const double VMax = 1.5;

        /// Resolve a verb to its authored spec. The ad-gatherer uses this to
        /// turn an advertised ActivityKind into its duration + served-Δ.
        public static Spec SpecFor(ActivityKind kind)
        {
            switch (kind)
            {
                case ActivityKind.Idle:      return Idle;
                case ActivityKind.Wander:    return Wander;
                case ActivityKind.Sleep:     return Sleep;
                case ActivityKind.Work:      return Work;
                case ActivityKind.Labor:     return Labor;
                case ActivityKind.Farm:      return Farm;
                case ActivityKind.Fish:      return Fish;
                case ActivityKind.EatHome:   return EatHome;
                case ActivityKind.EatTavern: return EatTavern;
                case ActivityKind.Socialize: return Socialize;
                case ActivityKind.Visit:     return Visit;
                case ActivityKind.Buy:       return Buy;
                case ActivityKind.Chat:      return Chat;
                case ActivityKind.SeekHelp:  return SeekHelp;
                default:                     return null;
            }
        }

        static double[] Deltas(double hunger = 0, double energyDef = 0, double socialDef = 0, double coinDef = 0, double goodsDef = 0)
        {
            var d = new double[NeedAxis.Count];
            d[NeedAxis.Hunger] = hunger;
            d[NeedAxis.EnergyDef] = energyDef;
            d[NeedAxis.SocialDef] = socialDef;
            d[NeedAxis.CoinDef] = coinDef;
            d[NeedAxis.GoodsDef] = goodsDef;
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
