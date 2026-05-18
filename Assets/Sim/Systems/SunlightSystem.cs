using System;
using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim
{
    /// Derives lighting state from WorldClock + Weather and writes
    /// LightingRegistry every tick. Pure-derive system: no DFU input, no
    /// bridge component. Rendering code can later read LightingRegistry on
    /// the main thread to drive actual Unity Light components.
    ///
    /// Daylight curve is sin(πt) over [dawn, dusk] — close enough to DFU's
    /// LightCurve at noon=1, dawn/dusk=0 boundaries. Weather attenuation
    /// matches DFU's defaults: overcast 0.65, rain 0.45, storm 0.25, snow 0.45.
    public sealed class SunlightSystem : ISystem
    {
        const float OvercastScale = 0.65f;
        const float RainScale     = 0.45f;
        const float StormScale    = 0.25f;
        const float SnowScale     = 0.45f;
        const float FogScale      = 0.55f;

        SimulationContext _ctx;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
        }

        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            var weather = _ctx.Weather.Current;

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

            _ctx.Lighting.Set(new LightingData
            {
                IsNight = isNight,
                DaylightScale = daylight,
                WeatherScale = weatherScale,
                SunIntensity = daylight * weatherScale,
                SunPitchDegrees = 180f * Clamp01(time01),
                DayTime01 = time01,
            });
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
