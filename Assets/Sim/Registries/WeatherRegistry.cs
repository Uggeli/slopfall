using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Sim's weather taxonomy. Mirrored from DaggerfallWorkshop.Game.Weather.WeatherType
    /// by WeatherMirror so the sim doesn't have to know about DFU's enum.
    public enum WeatherKind
    {
        Sunny,
        Cloudy,
        Overcast,
        Fog,
        Rain,
        Thunder,
        Snow,
    }

    public sealed class WeatherData
    {
        public WeatherKind Kind;
        public bool IsRaining;
        public bool IsStorming;
        public bool IsSnowing;
        public bool IsOvercast;
    }

    /// Single-global current weather. Same atomic-swap pattern as WorldClockRegistry.
    public sealed class WeatherRegistry
    {
        WeatherData _data = new WeatherData();

        public WeatherData Current => Volatile.Read(ref _data);
        public void Set(WeatherData data) => Interlocked.Exchange(ref _data, data);
    }
}
