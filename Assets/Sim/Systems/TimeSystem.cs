using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim
{
    /// Owns the world clock. Advances DaggerfallDateTime each tick by
    /// TickIntervalSeconds * TimeScale, writes WorldClockRegistry, and emits
    /// sim-native transition events (hour / day / month / year / dawn / dusk / etc.).
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
        float _timeScale = 12f;
        bool _seeded;
        int _lastHour = -1, _lastDay = -1, _lastMonth = -1, _lastYear = -1;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<SeedClockInput>(OnSeed);
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

            _lastHour = _clock.Hour;
            _lastDay = _clock.Day;
            _lastMonth = _clock.Month;
            _lastYear = _clock.Year;
            _seeded = true;

            WriteRegistry();
        }

        public void ProcessEvents() { /* SeedClockInput consumed via Subscribe */ }

        public void Update(long tick)
        {
            if (!_seeded) return;

            float delta = (float)(_ctx.Time.TickIntervalSeconds * _timeScale);
            if (delta > 0f) _clock.RaiseTime(delta);

            WriteRegistry();

            _ctx.Events.Emit(new TimeTickedEvent { Tick = tick, SimSeconds = _ctx.Time.Elapsed });

            if (_clock.Hour != _lastHour)
            {
                if (_clock.Hour == DaggerfallDateTime.DawnHour)
                    _ctx.Events.Emit(new DawnSimEvent());
                if (_clock.Hour == DaggerfallDateTime.DuskHour)
                    _ctx.Events.Emit(new DuskSimEvent());
                if (_clock.Hour == DaggerfallDateTime.MiddayHour)
                    _ctx.Events.Emit(new MiddaySimEvent());
                if (_clock.Hour == DaggerfallDateTime.MidnightHour)
                    _ctx.Events.Emit(new MidnightSimEvent());
                if (_clock.Hour == DaggerfallDateTime.LightsOnHour)
                    _ctx.Events.Emit(new CityLightsOnSimEvent());
                if (_clock.Hour == DaggerfallDateTime.LightsOffHour)
                    _ctx.Events.Emit(new CityLightsOffSimEvent());

                _lastHour = _clock.Hour;
                _ctx.Events.Emit(new NewHourSimEvent { Hour = _clock.Hour, Day = _clock.Day, Month = _clock.Month, Year = _clock.Year });
            }

            if (_clock.Day != _lastDay)
            {
                _lastDay = _clock.Day;
                _ctx.Events.Emit(new NewDaySimEvent { Day = _clock.Day, Month = _clock.Month, Year = _clock.Year });
            }

            if (_clock.Month != _lastMonth)
            {
                _lastMonth = _clock.Month;
                _ctx.Events.Emit(new NewMonthSimEvent { Month = _clock.Month, Year = _clock.Year });
            }

            if (_clock.Year != _lastYear)
            {
                _lastYear = _clock.Year;
                _ctx.Events.Emit(new NewYearSimEvent { Year = _clock.Year });
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
            });
        }
    }
}
