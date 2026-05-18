using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Mirrors DaggerfallUnity.Instance.WorldTime into WorldClockRegistry each frame.
    [DefaultExecutionOrder(10000)]
    public sealed class WorldClockMirror : MonoBehaviour
    {
        void Update()
        {
            var driver = SimDriver.Instance;
            if (driver == null) return;
            var dfu = DaggerfallUnity.Instance;
            if (dfu == null || dfu.WorldTime == null) return;
            var t = dfu.WorldTime.DaggerfallDateTime;
            if (t == null) return;

            driver.Context.WorldClock.Set(new WorldClockData
            {
                Year = t.Year,
                Month = t.Month,
                Day = t.Day,
                Hour = t.Hour,
                Minute = t.Minute,
                Second = t.Second,
                TimeScale = dfu.WorldTime.TimeScale,
            });
        }
    }
}
