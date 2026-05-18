using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    public sealed class LightingData
    {
        public bool IsNight;
        /// 0..1, peaks at midday. Analytic approximation of DFU's LightCurve.
        public float DaylightScale;
        /// 0..1, weather attenuation (1 = sunny, lower = clouded/raining/etc).
        public float WeatherScale;
        /// Combined exterior sun intensity factor (DaylightScale * WeatherScale).
        public float SunIntensity;
        /// Sun pitch in degrees, 0 at dawn, 90 at midday, 180 at dusk.
        public float SunPitchDegrees;
        /// 0..1, fraction of daytime elapsed. Negative outside [dawn, dusk].
        public float DayTime01;
    }

    /// Derived lighting state — written by SunlightSystem, read by rendering
    /// bridges (later). Single-global atomic-swap registry.
    public sealed class LightingRegistry
    {
        LightingData _data = new LightingData();

        public LightingData Current => Volatile.Read(ref _data);
        public void Set(LightingData data) => Interlocked.Exchange(ref _data, data);
    }
}
