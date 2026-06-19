using System;
using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.SunlightSystem. Pure-derive: reads
    // WorldClockRegistry.Current + WeatherRegistry.Current (read-only), computes a
    // LightingData, and Publishes a LightingSetIntent. Holds no tick state.
    //
    // Daylight curve is sin(πt) over [dawn, dusk] — noon=1, dawn/dusk=0. Weather
    // attenuation matches DFU's defaults. Logic preserved EXACTLY from the original.
    // (Reuses LightingData / WeatherKind from the parent DaggerfallWorkshop.Sim namespace.)
    public sealed class SunlightSystem : SimSystem
    {
        const float OvercastScale = 0.65f;
        const float RainScale     = 0.45f;
        const float StormScale    = 0.25f;
        const float SnowScale     = 0.45f;
        const float FogScale      = 0.55f;

        readonly WorldClockRegistry _clock;
        readonly WeatherRegistry _weather;

        public SunlightSystem(EventBus events, WorldClockRegistry clock, WeatherRegistry weather)
            : base(events)
        {
            _clock = clock;
            _weather = weather;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;
            var weather = _weather.Current;

            float dawn = DaggerfallDateTime.DawnHour * DaggerfallDateTime.MinutesPerHour;
            float dayRange = DaggerfallDateTime.DuskHour * DaggerfallDateTime.MinutesPerHour - dawn;
            float minuteOfDay = clock.Hour * DaggerfallDateTime.MinutesPerHour + clock.Minute;
            float time01 = (minuteOfDay - dawn) / dayRange;

            bool isNight = clock.Hour < DaggerfallDateTime.DawnHour || clock.Hour >= DaggerfallDateTime.DuskHour;

            float daylight;
            if (isNight)
                daylight = 0f;
            else
                daylight = (float)Math.Sin(Math.PI * Math.Max(0.0, Math.Min(1.0, time01)));

            float weatherScale = 1f;
            switch (weather.Kind)
            {
                case WeatherKind.Overcast: weatherScale = OvercastScale; break;
                case WeatherKind.Fog:      weatherScale = FogScale;      break;
                case WeatherKind.Rain:     weatherScale = RainScale;     break;
                case WeatherKind.Thunder:  weatherScale = StormScale;    break;
                case WeatherKind.Snow:     weatherScale = SnowScale;     break;
            }

            Events.Publish(new LightingSetIntent
            {
                Value = new LightingData
                {
                    IsNight = isNight,
                    DaylightScale = daylight,
                    WeatherScale = weatherScale,
                    SunIntensity = daylight * weatherScale,
                    SunPitchDegrees = 180f * Clamp01(time01),
                    DayTime01 = time01,
                },
            });
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
