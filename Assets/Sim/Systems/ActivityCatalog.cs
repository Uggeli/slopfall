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

            // --- Larder gate (Subsistence): the home-larder analog of the shop-
            //     stock sale gate. EatHome eats provisions from the eater's home
            //     larder (drawn by EconomySystem); with the larder empty the meal
            //     isn't backed, so its hunger relief doesn't accrue. The "shelf" is
            //     the eater's residency (HomeOf), not a TargetBuilding. -->
            public bool LarderGated = false;
            public double LarderUnitsPerMinute = 0;     // provisions eaten per game-minute while Doing

            // --- V modulators (soft; the uniform value function reads these) ---
            public double BaseGate = 1.0;               // flat gate multiplier (duty, etc.)
            public double DayGate = 1.0, NightGate = 1.0;
            public double DistanceScale = 0;            // 0 = no distance penalty; else 1/(1+dist/scale)
            public bool Outdoor = false;                // gate × weather (storms damp)
            public bool Social = false;                 // gate × liveliness × evening × cozy
            public bool Prepotent = false;              // gate × prepotency (leisure/growth)
            public bool RelationSensitive = false;      // gate × the agent's regard for who's present (L3 membrane)
            public double HolidayFactor = 1.0;          // gate × this when a holiday is active
            public int Trait = -1;                      // TraitIndex this activity couples to, or -1
            public double TraitBias = 1.0, TraitScale = 0.0, TraitExp = 1.0;  // factor = bias + scale·trait^exp
            public bool TraitOnBase = false;            // apply the trait factor to base (else to gate)
            public bool Growth = false;                 // base scaled by gate (no setpoint, e.g. Visit)
            public bool FearDriven = false;             // V2b: the fear response — graded down by the desperation edge (escape-affordability)
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

        /// Mining the hills — primary ore production in mountain regions, out at the
        /// diggings (a distinct place). Hard labour (steepest fatigue); otherwise like
        /// the other primary-sector work.
        public static readonly Spec Mine = new Spec
        {
            Kind = ActivityKind.Mine,
            DurationMinutes = 180,
            Delta = Deltas(coinDef: -0.15, energyDef: +0.14),
            OpenHour = 6, CloseHour = 18,
            Trait = TraitIndex.Industry, TraitBias = 0.7, TraitScale = 0.6, TraitExp = 1.0,
        };

        public static readonly Spec EatHome = new Spec
        {
            Kind = ActivityKind.EatHome,
            DurationMinutes = 30,
            Delta = Deltas(hunger: -0.5),
            NightGate = 0.15,               // kitchens mostly cold in the small hours
            // No longer a free lunch (Subsistence): a meal eats provisions from the
            // household larder, and its hunger relief is gated on them — an empty
            // larder means no meal. ~1 provision per 30-min meal (FROZEN placeholder;
            // tuned against the famine soak, not here). EconomySystem draws it.
            LarderGated = true,
            LarderUnitsPerMinute = 0.033,
        };

        public static readonly Spec EatTavern = new Spec
        {
            Kind = ActivityKind.EatTavern,
            DurationMinutes = 45,
            Delta = Deltas(hunger: -0.8, coinDef: +0.08, socialDef: -0.1),
            OpenHour = 6, CloseHour = 23,
            DistanceScale = 150, RelationSensitive = true,
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
            Social = true, Prepotent = true, RelationSensitive = true, HolidayFactor = 1.5,
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
            Outdoor = true, Prepotent = true, RelationSensitive = true, HolidayFactor = 1.3, Growth = true,
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

        /// Stealing (Subsistence slice): take provisions off a shop's shelf without
        /// paying — the destitute tier's survival path when there's no coin to Buy.
        /// Fills the larder like Buy (goodsDef), but free (no coin) — and carries a
        /// conscience charge (seeded per-agent on ActivityKind.Steal), so the marketplace
        /// join penalises it: the honest won't, the desperate/shameless will. The act is
        /// the Take verb (crime-as-tag); the legitimacy lives in the conscience valence,
        /// not a separate verb. Gated on the shelf having provisions (SaleReliefAxis).
        public static readonly Spec Steal = new Spec
        {
            Kind = ActivityKind.Steal,
            DurationMinutes = 20,           // grab and go
            Delta = Deltas(goodsDef: -0.3),
            OpenHour = 8, CloseHour = 20,
            DistanceScale = 150,
            // Takes 2 provisions over the act, pays nothing (SalePrice 0). Gated on
            // the shelf — an empty shop has nothing to steal, no relief.
            SaleUnits = 2, SalePrice = 0, SaleReliefAxis = NeedAxis.GoodsDef,
        };

        /// Flee (V2b): the fear response. Always advertised (an Object-Zero-style
        /// innate floor), but only wins when fear is loud — its fear delta is the
        /// scoring PROMISE (the somatic marker pruning the marketplace toward
        /// running), while the real relief is physical: fleeing carries the agent
        /// away, the threat percept breaks, and the controller resets fear
        /// (ResetOnPercept). OddSystem aims it away from the nearest threat.
        /// FearDriven, so the desperation edge (hunger ⊣ fear) grades it down — a
        /// starving agent can't afford to run (escape-affordability).
        public static readonly Spec Flee = new Spec
        {
            Kind = ActivityKind.Flee,
            DurationMinutes = 10,            // short bursts; re-decide and keep running while afraid
            Delta = Deltas(fear: -1.0),
            DistanceScale = 0,
            Outdoor = false,                // you run regardless of weather
            FearDriven = true,
        };

        /// Attack (V2b): the fight half of fight-or-flight — strike a perceived
        /// threat. The DamageEvent is emitted by the combat layer; the fear delta
        /// is the scoring promise (killing the threat ends the percept). Gated to
        /// the bold/armed in OddSystem, and tagged a crime if the target is innocent.
        public static readonly Spec Attack = new Spec
        {
            Kind = ActivityKind.Attack,
            DurationMinutes = 10,
            Delta = Deltas(fear: -1.0),
            DistanceScale = 80,             // you must close to the threat
            FearDriven = true,
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
        /// (Legacy: superseded by Beg; kept until the journey path is removed.)
        public static readonly Spec SeekHelp = new Spec
        {
            Kind = ActivityKind.SeekHelp,
            DurationMinutes = 5,
        };

        /// Begging (L4): the poor sit at a public venue and ask passers-by for
        /// alms. The marketplace picks it whenever poverty is loud — its value
        /// comes from the coinDef gap, so the comfortable never beg (no bespoke
        /// trigger). The asking is a proximity interaction RequestSystem runs
        /// while the agent is Doing this (it senses passers-by), not a journey.
        /// The coinDef delta is a scoring estimate; real relief = alms received.
        public static readonly Spec Beg = new Spec
        {
            Kind = ActivityKind.Beg,
            DurationMinutes = 120,
            Delta = Deltas(coinDef: -0.2, energyDef: +0.03),
            OpenHour = 8, CloseHour = 20,
            DistanceScale = 150,
        };

        /// Per-axis scoring weights — DERIVED from the canonical DriveCatalog
        /// table (the single source of truth; the drive doc's ScoreField).
        /// Personality scales a copy of this. Was a duplicate literal
        /// {1.2,1.0,0.6,0.5,0.5}; now the table is authoritative.
        public static readonly double[] Weights = BuildAxisArray(d => d.ScoreField);

        /// Need drift per game HOUR — the poles ticking up (Atoms: Metabolism),
        /// derived from DriveCatalog. Coin/Goods are derived reads (their
        /// LevelSource is not Stored), so their stored drift is 0; the CoinDef
        /// deltas in the specs above remain scoring PROMISES whose real rates
        /// EconomySystem implements as transfers. Was {0.04,0.05,0.03,0.0,0.03}.
        public static readonly double[] DriftPerHour = BuildAxisArray(d => d.DriftPerHour);

        /// Project one field of every DriveDef into a by-axis array, so the
        /// scattered scoring/metabolism constants share DriveCatalog's one table.
        static double[] BuildAxisArray(System.Func<DriveDef, double> select)
        {
            var a = new double[NeedAxis.Count];
            for (int i = 0; i < NeedAxis.Count; i++) a[i] = select(DriveCatalog.Defs[i]);
            return a;
        }

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
                case ActivityKind.Mine:      return Mine;
                case ActivityKind.EatHome:   return EatHome;
                case ActivityKind.EatTavern: return EatTavern;
                case ActivityKind.Socialize: return Socialize;
                case ActivityKind.Visit:     return Visit;
                case ActivityKind.Buy:       return Buy;
                case ActivityKind.Steal:     return Steal;
                case ActivityKind.Flee:      return Flee;
                case ActivityKind.Attack:    return Attack;
                case ActivityKind.Chat:      return Chat;
                case ActivityKind.SeekHelp:  return SeekHelp;
                case ActivityKind.Beg:       return Beg;
                default:                     return null;
            }
        }

        static double[] Deltas(double hunger = 0, double energyDef = 0, double socialDef = 0, double coinDef = 0, double goodsDef = 0, double fear = 0)
        {
            var d = new double[NeedAxis.Count];
            d[NeedAxis.Hunger] = hunger;
            d[NeedAxis.EnergyDef] = energyDef;
            d[NeedAxis.SocialDef] = socialDef;
            d[NeedAxis.CoinDef] = coinDef;
            d[NeedAxis.GoodsDef] = goodsDef;
            d[NeedAxis.Fear] = fear;
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
