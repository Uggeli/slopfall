using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim
{
    /// Owns the world clock. Advances DaggerfallDateTime each tick by
    /// TickIntervalSeconds * TimeScale, writes WorldClockRegistry, and emits
    /// sim-native transition events (hour / day / month / year / dawn / dusk / etc.).
    ///
    /// Transition detection walks total-seconds boundaries, so a tick that
    /// spans several hours (or days) fires every crossed marker exactly once —
    /// nothing is skipped at high timescales.
    ///
    /// Bootstrap: WorldClockMirror posts a SeedClockInput once DFU's WorldTime
    /// is ready. Until that arrives, this system is a no-op. After seeding,
    /// the sim is the source of truth; WorldClockMirror reverses direction and
    /// pushes the delta into DFU.WorldTime.RaiseTimeInSeconds so DFU's static
    /// OnDawn/OnDusk/etc subscribers keep firing.
    public sealed class TimeSystem : ISystem
    {
        SimulationContext _ctx;
        readonly DaggerfallDateTime _clock = new DaggerfallDateTime();
        readonly DaggerfallDateTime _scratch = new DaggerfallDateTime();
        float _timeScale = 12f;
        double _deltaGameSeconds;   // game-seconds advanced this tick (published to registry)
        bool _seeded;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<SeedClockInput>(OnSeed);
            ctx.Events.Subscribe<SetTimeScaleInput>(e =>
            {
                if (e.TimeScale >= 0f) _timeScale = e.TimeScale;
                if (_seeded) WriteRegistry();
            });
        }

        void OnSeed(SeedClockInput seed)
        {
            _clock.Year = seed.Year;
            _clock.Month = seed.Month;
            _clock.Day = seed.Day;
            _clock.Hour = seed.Hour;
            _clock.Minute = seed.Minute;
            _clock.Second = seed.Second;
            if (seed.TimeScale > 0f) _timeScale = seed.TimeScale;
            _seeded = true;
            _deltaGameSeconds = _ctx.Time.TickIntervalSeconds * _timeScale;   // sane pre-first-tick value

            WriteRegistry();
        }

        public void ProcessEvents() { /* SeedClockInput consumed via Subscribe */ }

        public void Update(long tick)
        {
            if (!_seeded) return;

            ulong before = _clock.ToSeconds();
            // Per-tick game-time step. The live driver (SimThread) may override it
            // with a small fixed step (running ticks faster for speed); soak/tests
            // leave the override unset and get the classic interval*scale step.
            double over = _ctx.Time.LiveStepGameSeconds;
            _deltaGameSeconds = over >= 0.0 ? over : _ctx.Time.TickIntervalSeconds * _timeScale;
            if (_deltaGameSeconds > 0.0) _clock.RaiseTime((float)_deltaGameSeconds);
            ulong after = _clock.ToSeconds();

            WriteRegistry();

            _ctx.Events.Emit(new TimeTickedEvent { Tick = tick, SimSeconds = _ctx.Time.Elapsed });

            EmitCrossedHours(before, after);
            EmitCrossedDays(before, after);

            ulong monthsBefore = before / (ulong)DaggerfallDateTime.SecondsPerMonth;
            ulong monthsAfter = after / (ulong)DaggerfallDateTime.SecondsPerMonth;
            for (ulong m = monthsBefore + 1; m <= monthsAfter; m++)
            {
                _scratch.FromSeconds(m * (ulong)DaggerfallDateTime.SecondsPerMonth);
                _ctx.Events.Emit(new NewMonthSimEvent { Month = _scratch.Month, Year = _scratch.Year });
            }

            ulong yearsBefore = before / (ulong)DaggerfallDateTime.SecondsPerYear;
            ulong yearsAfter = after / (ulong)DaggerfallDateTime.SecondsPerYear;
            for (ulong y = yearsBefore + 1; y <= yearsAfter; y++)
                _ctx.Events.Emit(new NewYearSimEvent { Year = (int)y });
        }

        void EmitCrossedHours(ulong before, ulong after)
        {
            ulong hoursBefore = before / (ulong)DaggerfallDateTime.SecondsPerHour;
            ulong hoursAfter = after / (ulong)DaggerfallDateTime.SecondsPerHour;

            for (ulong h = hoursBefore + 1; h <= hoursAfter; h++)
            {
                _scratch.FromSeconds(h * (ulong)DaggerfallDateTime.SecondsPerHour);
                int hour = _scratch.Hour;

                if (hour == DaggerfallDateTime.DawnHour)
                    _ctx.Events.Emit(new DawnSimEvent());
                if (hour == DaggerfallDateTime.DuskHour)
                    _ctx.Events.Emit(new DuskSimEvent());
                if (hour == DaggerfallDateTime.MiddayHour)
                    _ctx.Events.Emit(new MiddaySimEvent());
                if (hour == DaggerfallDateTime.MidnightHour)
                    _ctx.Events.Emit(new MidnightSimEvent());
                if (hour == DaggerfallDateTime.LightsOnHour)
                    _ctx.Events.Emit(new CityLightsOnSimEvent());
                if (hour == DaggerfallDateTime.LightsOffHour)
                    _ctx.Events.Emit(new CityLightsOffSimEvent());

                _ctx.Events.Emit(new NewHourSimEvent { Hour = hour, Day = _scratch.Day, Month = _scratch.Month, Year = _scratch.Year });
            }
        }

        void EmitCrossedDays(ulong before, ulong after)
        {
            ulong daysBefore = before / (ulong)DaggerfallDateTime.SecondsPerDay;
            ulong daysAfter = after / (ulong)DaggerfallDateTime.SecondsPerDay;

            for (ulong d = daysBefore + 1; d <= daysAfter; d++)
            {
                _scratch.FromSeconds(d * (ulong)DaggerfallDateTime.SecondsPerDay);
                _ctx.Events.Emit(new NewDaySimEvent { Day = _scratch.Day, Month = _scratch.Month, Year = _scratch.Year });
            }
        }

        void WriteRegistry()
        {
            _ctx.WorldClock.Set(new WorldClockData
            {
                Year = _clock.Year,
                Month = _clock.Month,
                Day = _clock.Day,
                Hour = _clock.Hour,
                Minute = _clock.Minute,
                Second = _clock.Second,
                TimeScale = _timeScale,
                DeltaGameSeconds = _deltaGameSeconds,
            });
        }
    }
}
