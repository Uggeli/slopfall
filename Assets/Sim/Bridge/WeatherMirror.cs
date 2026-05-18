using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Weather;
using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Mirrors GameManager.Instance.WeatherManager state into WeatherRegistry
    /// each frame. Phase 3c: observer direction (DFU → sim). Will flip later
    /// when a sim-side weather scheduler becomes authoritative.
    [DefaultExecutionOrder(10000)]
    public sealed class WeatherMirror : MonoBehaviour
    {
        void Update()
        {
            var driver = SimDriver.Instance;
            if (driver == null) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.WeatherManager == null) return;
            var wm = gm.WeatherManager;

            var kind = WeatherKind.Sunny;
            if (wm.PlayerWeather != null)
                kind = TranslateWeather(wm.PlayerWeather.WeatherType);

            driver.Context.Weather.Set(new WeatherData
            {
                Kind = kind,
                IsRaining = wm.IsRaining,
                IsStorming = wm.IsStorming,
                IsSnowing = wm.IsSnowing,
                IsOvercast = wm.IsOvercast,
            });
        }

        static WeatherKind TranslateWeather(WeatherType w)
        {
            switch (w)
            {
                case WeatherType.Sunny:    return WeatherKind.Sunny;
                case WeatherType.Cloudy:   return WeatherKind.Cloudy;
                case WeatherType.Overcast: return WeatherKind.Overcast;
                case WeatherType.Fog:      return WeatherKind.Fog;
                case WeatherType.Rain:     return WeatherKind.Rain;
                case WeatherType.Thunder:  return WeatherKind.Thunder;
                case WeatherType.Snow:     return WeatherKind.Snow;
                default:                   return WeatherKind.Sunny;
            }
        }
    }
}
