namespace DaggerfallWorkshop.Sim
{
    /// Ticks the need poles: time drift upward (Atoms: Metabolism ticking the
    /// poles) plus the current activity's promised delta spread evenly over
    /// its duration while the entity is Doing. Sole writer of NeedsRegistry
    /// after TownLoader seeds it.
    public sealed class NeedsSystem : ISystem
    {
        /// Social discomfort from sharing the street with someone resented —
        /// the felt side of a directed emotion (Atoms: the residual).
        const double DislikeDiscomfort = 0.03;

        /// A "comfortably stocked" household larder, in provisions: the GoodsDef
        /// derived read saturates to 0 at/above this, 1.0 at empty. FROZEN
        /// placeholder (tuned against the famine soak, not here).
        const double LarderTarget = 5.0;

        SimulationContext _ctx;
        readonly System.Collections.Generic.List<EntityId> _discomforts = new System.Collections.Generic.List<EntityId>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<DislikeNearbyEvent>(e => _discomforts.Add(e.Who));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _discomforts.Count; i++)
            {
                if (!_ctx.Needs.TryGet(_discomforts[i], out var needs)) continue;
                var next = new NeedsData();
                System.Array.Copy(needs.V, next.V, NeedAxis.Count);
                next.V[NeedAxis.SocialDef] = System.Math.Min(ActivityCatalog.VMax,
                    next.V[NeedAxis.SocialDef] + DislikeDiscomfort);
                _ctx.Needs.Set(_discomforts[i], next);
            }
            _discomforts.Clear();
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;    // clock not seeded yet

            double gameMinutes = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            double gameHours = gameMinutes / 60.0;

            foreach (var kv in _ctx.Needs.All)
            {
                var v = kv.Value.V;
                var next = new NeedsData();

                ActivityCatalog.Spec doing = null;
                if (_ctx.Behavior.TryGet(kv.Key, out var behavior)
                    && behavior.Phase == ActivityPhase.Doing)
                    doing = ActivityCatalog.SpecFor(behavior.Activity);

                bool sleeping = doing != null && doing.Kind == ActivityKind.Sleep;
                _ctx.Personality.TryGet(kv.Key, out var person);

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
                        double deficit = 1.0 - _ctx.Coin.Get(kv.Key);
                        next.V[axis] = deficit < 0 ? 0 : deficit;
                        continue;
                    }
                    if (level == LevelSource.DerivedLarder)
                    {
                        // "Provisions running low" reads the household larder directly
                        // (single source of truth) — an empty pantry is a loud restock
                        // pull, a full one is silent. The homeless have no pantry to
                        // stock, so no goods pressure (they eat out / beg / steal).
                        double pantry = home < 0 ? LarderTarget : _ctx.Larder.Get(home);
                        double deficit = 1.0 - System.Math.Min(1.0, pantry / LarderTarget);
                        next.V[axis] = deficit < 0 ? 0 : deficit;
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
                            delta *= CompanyFactor(_ctx.Occupancy.CompanyOf(kv.Key));

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
                            && _ctx.Larder.Get(home) <= 0)
                            delta = 0;

                        value += delta;
                    }

                    if (value < 0) value = 0;
                    if (value > ActivityCatalog.VMax) value = ActivityCatalog.VMax;
                    next.V[axis] = value;
                }

                _ctx.Needs.Set(kv.Key, next);
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
            => _ctx.Residency.TryGet(id, out var r) && r != null ? r.BuildingIndex : -1;

        /// Does the seller still have the good this sale draws? Mirrors the gate
        /// EconomySystem.PaySale obeyed (which ran earlier this tick), so a relief
        /// only lands when a real good backed it.
        bool SellerHasStock(int building, ActivityCatalog.Spec spec)
        {
            if (building < 0 || !_ctx.Buildings.TryGet(building, out var b) || b == null) return false;
            if (!GoodsCatalog.SaleGoodFor(b.Kind, spec.Kind, out var good)) return false;
            return _ctx.Stock.Get(building, good) > 0;
        }
    }
}
