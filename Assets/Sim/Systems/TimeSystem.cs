namespace DaggerfallWorkshop.Sim
{
    /// First real sim system. Observes WorldClockRegistry each tick and emits
    /// sim-native time events on hour / day / month / year / dawn / dusk / etc.
    /// transitions, mirroring DFU's WorldTime.RaiseEvents semantics.
    ///
    /// Phase 3 scope: pure observation, not authoritative. The registry is still
    /// written by WorldClockMirror (DFU → registry). Once consumers migrate to
    /// these sim events, a follow-up commit flips the bridge direction and
    /// TimeSystem becomes the writer.
    public sealed class TimeSystem : ISystem
    {
        SimulationContext _ctx;
        int _lastHour = -1, _lastDay = -1, _lastMonth = -1, _lastYear = -1;
        bool _seeded;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
        }

        public void ProcessEvents() { /* no upstream consumers yet */ }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            // Initial clock is all zeros until Phase 1 mirror runs once. Skip
            // until a real year value appears.
            if (clock.Year == 0) return;

            _ctx.Events.Emit(new TimeTickedEvent { Tick = tick, SimSeconds = _ctx.Time.Elapsed });

            if (!_seeded)
            {
                _lastHour = clock.Hour;
                _lastDay = clock.Day;
                _lastMonth = clock.Month;
                _lastYear = clock.Year;
                _seeded = true;
                return;
            }

            if (clock.Hour != _lastHour)
            {
                // Hour transition events — fire BEFORE NewHour so handlers that
                // listen to both see the specific event first.
                if (clock.Hour == DaggerfallDateTime.DawnHour)
                    _ctx.Events.Emit(new DawnSimEvent());
                if (clock.Hour == DaggerfallDateTime.DuskHour)
                    _ctx.Events.Emit(new DuskSimEvent());
                if (clock.Hour == DaggerfallDateTime.MiddayHour)
                    _ctx.Events.Emit(new MiddaySimEvent());
                if (clock.Hour == DaggerfallDateTime.MidnightHour)
                    _ctx.Events.Emit(new MidnightSimEvent());
                if (clock.Hour == DaggerfallDateTime.LightsOnHour)
                    _ctx.Events.Emit(new CityLightsOnSimEvent());
                if (clock.Hour == DaggerfallDateTime.LightsOffHour)
                    _ctx.Events.Emit(new CityLightsOffSimEvent());

                _lastHour = clock.Hour;
                _ctx.Events.Emit(new NewHourSimEvent { Hour = clock.Hour, Day = clock.Day, Month = clock.Month, Year = clock.Year });
            }

            if (clock.Day != _lastDay)
            {
                _lastDay = clock.Day;
                _ctx.Events.Emit(new NewDaySimEvent { Day = clock.Day, Month = clock.Month, Year = clock.Year });
            }

            if (clock.Month != _lastMonth)
            {
                _lastMonth = clock.Month;
                _ctx.Events.Emit(new NewMonthSimEvent { Month = clock.Month, Year = clock.Year });
            }

            if (clock.Year != _lastYear)
            {
                _lastYear = clock.Year;
                _ctx.Events.Emit(new NewYearSimEvent { Year = clock.Year });
            }
        }
    }
}
