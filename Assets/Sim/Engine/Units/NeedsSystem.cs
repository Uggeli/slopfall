namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.NeedsSystem. Ticks the need poles:
    // time drift upward (Atoms: Metabolism ticking the poles) plus the current
    // activity's promised delta spread evenly over its duration while the entity is
    // Doing. Holds NO tick state: reads the Engine registries read-only, folds the
    // social-discomfort events (DislikeNearbyEvent) in, computes the whole new
    // NeedsData per entity, and Publishes a NeedsSetIntent (NeedsRegistry is the sole
    // applier). The drift + activity-effect + fear-controller logic is preserved
    // EXACTLY from the legacy system — every constant, every gate.
    //
    // Catalogs/helpers (DriveCatalog, ActivityCatalog, NeedAxis, LevelSource,
    // GoodsCatalog, TraitIndex, PersonalityData, NeedsData, ...) resolve unqualified
    // from the enclosing DaggerfallWorkshop.Sim namespace.
    public sealed class NeedsSystem : SimSystem
    {
        /// Social discomfort from sharing the street with someone resented —
        /// the felt side of a directed emotion (Atoms: the residual).
        const double DislikeDiscomfort = 0.03;

        /// A "comfortably stocked" household larder, in provisions: the GoodsDef
        /// derived read saturates to 0 at/above this, 1.0 at empty. FROZEN
        /// placeholder (tuned against the famine soak, not here).
        const double LarderTarget = 5.0;

        // --- Fear controller (V2b). FROZEN placeholders, tuned with the rest. ---
        const double VigilanceFloorMax = 0.3;    // a maximally timid soul rests this vigilant
        const double ThreatSightRadius = 12.0;   // matches SenseSystem.SightRadius (clarity = 1 − d/R)
        const double CompletionGain = 0.8;       // how hard the ambiguous (1−c) remainder is filled in
        const double SpiralGain = 0.6;           // arousal feeds completion → the positive-feedback (panic) loop
        const double FearRiseRatePerMin = 0.5;   // fear floods fast
        const double FearDecayRatePerMin = 0.1;  // and ebbs slower, toward the floor (ResetOnPercept)

        readonly NeedsRegistry _needs;
        readonly BehaviorRegistry _behavior;
        readonly PersonalityRegistry _personality;
        readonly CoinRegistry _coin;
        readonly LarderRegistry _larder;
        readonly ResidencyRegistry _residency;
        readonly OccupancyRegistry _occupancy;
        readonly BuildingRegistry _buildings;
        readonly StockRegistry _stock;
        readonly SubjectiveViewRegistry _subjective;
        readonly PositionRegistry _position;
        readonly WorldClockRegistry _worldClock;

        public NeedsSystem(
            EventBus events,
            NeedsRegistry needs,
            BehaviorRegistry behavior,
            PersonalityRegistry personality,
            CoinRegistry coin,
            LarderRegistry larder,
            ResidencyRegistry residency,
            OccupancyRegistry occupancy,
            BuildingRegistry buildings,
            StockRegistry stock,
            SubjectiveViewRegistry subjective,
            PositionRegistry position,
            WorldClockRegistry worldClock) : base(events)
        {
            _needs = needs;
            _behavior = behavior;
            _personality = personality;
            _coin = coin;
            _larder = larder;
            _residency = residency;
            _occupancy = occupancy;
            _buildings = buildings;
            _stock = stock;
            _subjective = subjective;
            _position = position;
            _worldClock = worldClock;
        }

        public override void Update(long tick)
        {
            var clock = _worldClock.Current;
            if (clock.Year == 0) return;    // clock not seeded yet

            double gameMinutes = clock.DeltaGameSeconds / 60.0;
            double gameHours = gameMinutes / 60.0;

            // Social discomfort from DislikeNearbyEvent — the felt side of a directed
            // emotion. The legacy ProcessEvents() folded these into SocialDef as a
            // separate write; here they fold into the same recomputed NeedsData so the
            // single NeedsSetIntent per entity carries everything (NeedsRegistry is the
            // sole applier — one whole-value set per id wins). Accumulate the count of
            // dislike-percepts per entity this tick so multiple stack, as the legacy
            // per-event Set loop did.
            var discomforts = new System.Collections.Generic.Dictionary<EntityId, int>();
            foreach (var d in Events.GetEvents<DislikeNearbyEvent>())
            {
                discomforts.TryGetValue(d.Who, out var n);
                discomforts[d.Who] = n + 1;
            }

            foreach (var kv in _needs.All)
            {
                var v = kv.Value.V;
                var next = new NeedsData();

                ActivityCatalog.Spec doing = null;
                BehaviorData behavior = null;
                if (_behavior.TryGet(kv.Key, out behavior)
                    && behavior.Phase == ActivityPhase.Doing)
                    doing = ActivityCatalog.SpecFor(behavior.Activity);

                bool sleeping = doing != null && doing.Kind == ActivityKind.Sleep;
                _personality.TryGet(kv.Key, out var person);

                int home = HomeOf(kv.Key);

                for (int axis = 0; axis < NeedAxis.Count; axis++)
                {
                    // Derived levels are READ from a conserved/external quantity each
                    // tick, not drifted (drive doc: only Stored poles are ticked by
                    // Metabolism — nothing kept that can desync from its source).
                    var level = DriveCatalog.Defs[axis].Level;
                    if (level == LevelSource.DerivedCoin)
                    {
                        // Poverty pressure tracks the actual purse (CoinRegistry).
                        double deficit = 1.0 - _coin.Get(kv.Key);
                        next.V[axis] = deficit < 0 ? 0 : deficit;
                        continue;
                    }
                    if (level == LevelSource.DerivedLarder)
                    {
                        // "Provisions running low" reads the household larder directly
                        // (single source of truth) — an empty pantry is a loud restock
                        // pull, a full one is silent. The homeless have no pantry to
                        // stock, so no goods pressure (they eat out / beg / steal).
                        double pantry = home < 0 ? LarderTarget : _larder.Get(home);
                        double deficit = 1.0 - System.Math.Min(1.0, pantry / LarderTarget);
                        next.V[axis] = deficit < 0 ? 0 : deficit;
                        continue;
                    }
                    if (level == LevelSource.DerivedThreat)
                    {
                        // The fear controller (V2b): the Max-projected, completion-
                        // biased threat field over the agent's last view, smoothed
                        // with a personality vigilance floor. The one stateful drive.
                        next.V[axis] = FearLevel(kv.Key, v[axis], person, gameMinutes);
                        continue;
                    }

                    double value = v[axis];

                    // Metabolism slows in sleep: tiredness and loneliness
                    // don't accrue, hunger at half rate — otherwise a night's
                    // rest can't keep up with a day's drain, and sleepers wake
                    // for midnight snacks every hour.
                    double drift = ActivityCatalog.DriftPerHour[axis];
                    if (person != null) drift *= person.DriftScale[axis];
                    if (sleeping)
                    {
                        if (axis == NeedAxis.EnergyDef || axis == NeedAxis.SocialDef) drift = 0;
                        else if (axis == NeedAxis.Hunger) drift *= 0.5;
                    }
                    value += drift * gameHours;

                    if (doing != null && doing.DurationMinutes > 0)
                    {
                        double delta = doing.Delta[axis] / doing.DurationMinutes * gameMinutes;

                        // Social relief must be earned by actual company:
                        // drinking alone in an empty tavern barely helps.
                        if (axis == NeedAxis.SocialDef && delta < 0 && IsCompanyActivity(doing.Kind))
                            delta *= CompanyFactor(_occupancy.CompanyOf(kv.Key));

                        // A purchase's relief is contingent on real goods: an empty
                        // shelf satisfies nothing (G3). EconomySystem drew the stock
                        // just now, so this reads the same gate the sale obeyed.
                        if (axis == doing.SaleReliefAxis && delta < 0
                            && !SellerHasStock(behavior.TargetBuilding, doing))
                            delta = 0;

                        // A home meal's relief is contingent on real provisions: the
                        // household larder is the eater's "shelf" (HomeOf), drawn by
                        // EconomySystem this tick. Empty larder → no meal (Subsistence).
                        if (doing.LarderGated && axis == NeedAxis.Hunger && delta < 0
                            && _larder.Get(home) <= 0)
                            delta = 0;

                        value += delta;
                    }

                    // Social discomfort fold (legacy ProcessEvents): each
                    // DislikeNearbyEvent this tick nudges SocialDef up by
                    // DislikeDiscomfort, clamped to VMax — the felt residual of
                    // sharing the street with someone resented.
                    if (axis == NeedAxis.SocialDef && discomforts.TryGetValue(kv.Key, out var dislikes))
                        value = System.Math.Min(ActivityCatalog.VMax, value + dislikes * DislikeDiscomfort);

                    if (value < 0) value = 0;
                    if (value > ActivityCatalog.VMax) value = ActivityCatalog.VMax;
                    next.V[axis] = value;
                }

                Events.Publish(new NeedsSetIntent { Id = kv.Key, Data = next });
            }
        }

        static bool IsCompanyActivity(ActivityKind kind) =>
            kind == ActivityKind.Socialize || kind == ActivityKind.EatTavern || kind == ActivityKind.Visit;

        static double CompanyFactor(int company) =>
            company <= 0 ? 0.2 : (company == 1 ? 0.6 : 1.0);

        /// The home building whose larder this agent eats from — its residency
        /// building (a home's residents share one larder). Mirrors EconomySystem's
        /// HomeOf, the larder's sole writer. −1 if none.
        int HomeOf(EntityId id)
            => _residency.TryGet(id, out var r) && r != null ? r.BuildingIndex : -1;

        /// The vigilance floor — trait anxiety read as a baseline arousal (drive
        /// doc). A bold soul (HarmAvoidance 0) rests at 0; a timid one rests
        /// vigilant, so an ambiguous percept can cross into felt threat FROM REST
        /// (the ignition bifurcation). Absent personality → centered.
        static double VigilanceFloor(PersonalityData person)
            => VigilanceFloorMax * (person != null ? person.Trait(TraitIndex.HarmAvoidance) : 0.5);

        /// Complete an ambiguous threat cue (drive doc, emotion-as-controller):
        /// clarity c, plus the floor-and-arousal-driven fill of the (1−c) remainder.
        /// Bold (low floor/arousal) leaves a faint cue faint; timid completes it
        /// toward threat; rising arousal completes harder still — the analytic
        /// ignition c + (1−c)·gain·(floor + spiral·arousal). Pure, for the
        /// ignition-bifurcation test.
        public static double CompleteThreat(double clarity, double floor, double arousal)
        {
            double completion = CompletionGain * (floor + SpiralGain * arousal);
            double felt = clarity + (1.0 - clarity) * completion;
            return felt < 0 ? 0 : (felt > 1.0 ? 1.0 : felt);
        }

        /// The fear controller: Max over the perceived-threat field (each cue
        /// completed by CompleteThreat, clarity from distance), floored at the
        /// vigilance baseline, then smoothed toward that target — fast to flood,
        /// slow to ebb (ResetOnPercept: with no threat in view the target is the
        /// floor). prevFear is the carried arousal that drives the spiral. Reads
        /// LAST tick's view (NeedsSystem precedes SubjectiveSystem — staleness is
        /// Atoms-acceptable).
        double FearLevel(EntityId self, double prevFear, PersonalityData person, double gameMinutes)
        {
            double floor = VigilanceFloor(person);
            double target = floor;

            if (_subjective.TryGet(self, out var view) && view != null
                && _position.TryGet(self, out var sp) && sp != null)
            {
                var reads = view.Entities;
                for (int i = 0; i < reads.Count; i++)
                {
                    if (reads[i].Threat <= 0) continue;
                    double clarity = 1.0;
                    if (_position.TryGet(reads[i].Other, out var tp) && tp != null)
                    {
                        double dx = tp.X - sp.X, dz = tp.Z - sp.Z;
                        double d = System.Math.Sqrt(dx * dx + dz * dz);
                        clarity = 1.0 - d / ThreatSightRadius;
                        if (clarity < 0) clarity = 0; else if (clarity > 1) clarity = 1;
                    }
                    double felt = CompleteThreat(clarity, floor, prevFear) * reads[i].Threat;
                    if (felt > target) target = felt;   // Max projection: the worst threat dominates
                }
            }

            double ratePerMin = target >= prevFear ? FearRiseRatePerMin : FearDecayRatePerMin;
            double rate = ratePerMin * gameMinutes;
            if (rate > 1.0) rate = 1.0; else if (rate < 0) rate = 0;
            double next = prevFear + rate * (target - prevFear);
            if (next < 0) next = 0; else if (next > ActivityCatalog.VMax) next = ActivityCatalog.VMax;
            return next;
        }

        /// Does the seller still have the good this sale draws? Mirrors the gate
        /// EconomySystem.PaySale obeyed (which ran earlier this tick), so a relief
        /// only lands when a real good backed it.
        bool SellerHasStock(int building, ActivityCatalog.Spec spec)
        {
            if (building < 0 || !_buildings.TryGet(building, out var b) || b == null) return false;
            if (!GoodsCatalog.SaleGoodFor(b.Kind, spec.Kind, out var good)) return false;
            return _stock.Get(building, good) > 0;
        }
    }
}
