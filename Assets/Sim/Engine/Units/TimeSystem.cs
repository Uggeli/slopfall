using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.TimeSystem. FOUNDATIONAL: it owns the
    // advance of the world clock and EMITS the time-boundary events the rest of the sim
    // reacts to. Holds NO tick state — the clock state lives in WorldClockRegistry; this
    // system reads Current, reconstructs a DaggerfallDateTime, advances a fixed step, and
    // Publishes a WorldClockSetIntent plus boundary signal events.
    //
    // Init/ProcessEvents/Update collapse into Update():
    //   * SeedClockInput  (GetEvents) — seeds the clock; like the old OnSeed it writes the
    //     registry but does NOT emit boundary events that tick.
    //   * SetTimeScaleInput (GetEvents) — folded into the advance step (last-wins).
    //   * unseeded guard is Current.Year == 0 (same convention SunlightSystem/HolidaySystem use).
    //
    // Boundary detection walks total-seconds markers between `before` and `after`, so a
    // tick spanning several hours/days fires each crossed marker exactly once — identical
    // math to the original (DaggerfallDateTime SecondsPerHour/Day/Month/Year boundaries).
    public sealed class TimeSystem : SimSystem
    {
        readonly WorldClockRegistry _clock;
        readonly double _tickIntervalSeconds;   // fixed sim-seconds per tick (e.g. 0.1)

        public TimeSystem(EventBus events, WorldClockRegistry clock, double tickIntervalSeconds)
            : base(events)
        {
            _clock = clock;
            _tickIntervalSeconds = tickIntervalSeconds;
        }

        public override void Update(long tick)
        {
            var current = _clock.Current;

            // --- seed: apply the latest SeedClockInput, write registry, emit no boundaries ---
            var seeds = Events.GetEvents<SeedClockInput>();
            if (seeds.Length > 0)
            {
                var seed = seeds[seeds.Length - 1];          // last write wins
                float scale = seed.TimeScale > 0f ? seed.TimeScale : EffectiveScale(current);
                Events.Publish(new WorldClockSetIntent
                {
                    Year = seed.Year,
                    Month = seed.Month,
                    Day = seed.Day,
                    Hour = seed.Hour,
                    Minute = seed.Minute,
                    Second = seed.Second,
                    TimeScale = scale,
                    DeltaGameSeconds = _tickIntervalSeconds,   // sane pre-first-advance value
                });
                return;
            }

            // --- not yet seeded: no-op (Year == 0) ---
            if (current.Year == 0) return;

            // --- effective time scale (SetTimeScaleInput last-wins, else current) ---
            float timeScale = EffectiveScale(current);

            // Reconstruct the clock, advance a FIXED step. PAUSE (scale <= 0) advances 0.
            var c = FromData(current);
            ulong before = c.ToSeconds();
            double deltaGameSeconds = timeScale > 0f ? _tickIntervalSeconds : 0.0;
            if (deltaGameSeconds > 0.0) c.RaiseTime((float)deltaGameSeconds);
            ulong after = c.ToSeconds();

            Events.Publish(new WorldClockSetIntent
            {
                Year = c.Year,
                Month = c.Month,
                Day = c.Day,
                Hour = c.Hour,
                Minute = c.Minute,
                Second = c.Second,
                TimeScale = timeScale,
                DeltaGameSeconds = deltaGameSeconds,
            });

            EmitCrossedHours(before, after);
            EmitCrossedDays(before, after);

            ulong monthsBefore = before / (ulong)DaggerfallDateTime.SecondsPerMonth;
            ulong monthsAfter = after / (ulong)DaggerfallDateTime.SecondsPerMonth;
            var scratch = new DaggerfallDateTime();
            for (ulong m = monthsBefore + 1; m <= monthsAfter; m++)
            {
                scratch.FromSeconds(m * (ulong)DaggerfallDateTime.SecondsPerMonth);
                Events.Publish(new NewMonthEvent { Month = scratch.Month, Year = scratch.Year });
            }

            ulong yearsBefore = before / (ulong)DaggerfallDateTime.SecondsPerYear;
            ulong yearsAfter = after / (ulong)DaggerfallDateTime.SecondsPerYear;
            for (ulong y = yearsBefore + 1; y <= yearsAfter; y++)
                Events.Publish(new NewYearEvent { Year = (int)y });
        }

        float EffectiveScale(WorldClockData current)
        {
            var scales = Events.GetEvents<SetTimeScaleInput>();
            for (int i = scales.Length - 1; i >= 0; i--)
                if (scales[i].TimeScale >= 0f) return scales[i].TimeScale;   // last valid wins
            return current.TimeScale;
        }

        void EmitCrossedHours(ulong before, ulong after)
        {
            ulong hoursBefore = before / (ulong)DaggerfallDateTime.SecondsPerHour;
            ulong hoursAfter = after / (ulong)DaggerfallDateTime.SecondsPerHour;

            var scratch = new DaggerfallDateTime();
            for (ulong h = hoursBefore + 1; h <= hoursAfter; h++)
            {
                scratch.FromSeconds(h * (ulong)DaggerfallDateTime.SecondsPerHour);
                int hour = scratch.Hour;

                if (hour == DaggerfallDateTime.DawnHour)
                    Events.Publish(new DawnEvent());
                if (hour == DaggerfallDateTime.DuskHour)
                    Events.Publish(new DuskEvent());
                if (hour == DaggerfallDateTime.MiddayHour)
                    Events.Publish(new MiddayEvent());
                if (hour == DaggerfallDateTime.MidnightHour)
                    Events.Publish(new MidnightEvent());
                if (hour == DaggerfallDateTime.LightsOnHour)
                    Events.Publish(new CityLightsOnEvent());
                if (hour == DaggerfallDateTime.LightsOffHour)
                    Events.Publish(new CityLightsOffEvent());

                Events.Publish(new NewHourEvent { Hour = hour, Day = scratch.Day, Month = scratch.Month, Year = scratch.Year });
            }
        }

        void EmitCrossedDays(ulong before, ulong after)
        {
            ulong daysBefore = before / (ulong)DaggerfallDateTime.SecondsPerDay;
            ulong daysAfter = after / (ulong)DaggerfallDateTime.SecondsPerDay;

            var scratch = new DaggerfallDateTime();
            for (ulong d = daysBefore + 1; d <= daysAfter; d++)
            {
                scratch.FromSeconds(d * (ulong)DaggerfallDateTime.SecondsPerDay);
                Events.Publish(new NewDayEvent { Day = scratch.Day, Month = scratch.Month, Year = scratch.Year });
            }
        }

        static DaggerfallDateTime FromData(WorldClockData d)
        {
            return new DaggerfallDateTime
            {
                Year = d.Year,
                Month = d.Month,
                Day = d.Day,
                Hour = d.Hour,
                Minute = d.Minute,
                Second = d.Second,
            };
        }
    }
}
