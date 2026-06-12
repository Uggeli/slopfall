namespace DaggerfallWorkshop.Sim
{
    /// Ticks the need poles: time drift upward (Atoms: Metabolism ticking the
    /// poles) plus the current activity's promised delta spread evenly over
    /// its duration while the entity is Doing. Sole writer of NeedsRegistry
    /// after TownLoader seeds it.
    public sealed class NeedsSystem : ISystem
    {
        SimulationContext _ctx;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

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
                    doing = SpecFor(behavior.Activity);

                bool sleeping = doing != null && doing.Kind == ActivityKind.Sleep;

                for (int axis = 0; axis < NeedAxis.Count; axis++)
                {
                    double value = v[axis];

                    // Metabolism slows in sleep: tiredness and loneliness
                    // don't accrue, hunger at half rate — otherwise a night's
                    // rest can't keep up with a day's drain, and sleepers wake
                    // for midnight snacks every hour.
                    double drift = ActivityCatalog.DriftPerHour[axis];
                    if (sleeping)
                    {
                        if (axis == NeedAxis.EnergyDef || axis == NeedAxis.SocialDef) drift = 0;
                        else if (axis == NeedAxis.Hunger) drift *= 0.5;
                    }
                    value += drift * gameHours;

                    if (doing != null && doing.DurationMinutes > 0)
                        value += doing.Delta[axis] / doing.DurationMinutes * gameMinutes;

                    if (value < 0) value = 0;
                    if (value > ActivityCatalog.VMax) value = ActivityCatalog.VMax;
                    next.V[axis] = value;
                }

                _ctx.Needs.Set(kv.Key, next);
            }
        }

        internal static ActivityCatalog.Spec SpecFor(ActivityKind kind)
        {
            switch (kind)
            {
                case ActivityKind.Idle:      return ActivityCatalog.Idle;
                case ActivityKind.Wander:    return ActivityCatalog.Wander;
                case ActivityKind.Sleep:     return ActivityCatalog.Sleep;
                case ActivityKind.Work:      return ActivityCatalog.Work;
                case ActivityKind.EatHome:   return ActivityCatalog.EatHome;
                case ActivityKind.EatTavern: return ActivityCatalog.EatTavern;
                case ActivityKind.Socialize: return ActivityCatalog.Socialize;
                default:                     return null;
            }
        }
    }
}
