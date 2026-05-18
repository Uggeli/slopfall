using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Phase 3b: bridge runs registry → DFU. On the first frame DFU's WorldTime
    /// is ready it sends one SeedClockInput to the sim, then sets
    /// DaggerfallUnity.Instance.WorldTime.TimeScale = 0 so DFU stops auto-
    /// advancing its DaggerfallDateTime. From then on this mirror reads
    /// WorldClockRegistry each frame, computes the game-seconds delta since the
    /// previous reading, and writes that into WorldTime.RaiseTimeInSeconds.
    /// DFU's WorldTime.Update consumes RaiseTimeInSeconds and calls
    /// DaggerfallDateTime.RaiseTime + RaiseEvents, which keeps the existing
    /// static OnDawn/OnDusk/OnNewHour/etc. subscribers firing unchanged.
    [DefaultExecutionOrder(10000)]
    public sealed class WorldClockMirror : MonoBehaviour
    {
        bool _seedSent;
        bool _hasLastReading;
        ulong _lastGameSeconds;

        void Update()
        {
            var driver = SimDriver.Instance;
            if (driver == null) return;
            var dfu = DaggerfallUnity.Instance;
            if (dfu == null || dfu.WorldTime == null) return;
            var worldTime = dfu.WorldTime;
            var t = worldTime.DaggerfallDateTime;
            if (t == null) return;

            if (!_seedSent)
            {
                driver.Inputs.Enqueue(new SeedClockInput
                {
                    Year = t.Year,
                    Month = t.Month,
                    Day = t.Day,
                    Hour = t.Hour,
                    Minute = t.Minute,
                    Second = t.Second,
                    TimeScale = worldTime.TimeScale,
                });
                worldTime.TimeScale = 0f;
                _seedSent = true;
                return;
            }

            var clock = driver.Context.WorldClock.Current;
            if (clock.Year == 0) return; // sim hasn't applied seed yet

            ulong current = ComputeGameSeconds(clock);
            if (!_hasLastReading)
            {
                _lastGameSeconds = current;
                _hasLastReading = true;
                return;
            }

            if (current > _lastGameSeconds)
            {
                ulong delta = current - _lastGameSeconds;
                _lastGameSeconds = current;
                worldTime.RaiseTimeInSeconds = (float)delta;
            }
        }

        static ulong ComputeGameSeconds(WorldClockData c)
        {
            long total =
                (long)DaggerfallDateTime.SecondsPerYear * c.Year +
                DaggerfallDateTime.SecondsPerMonth * c.Month +
                DaggerfallDateTime.SecondsPerDay * c.Day +
                DaggerfallDateTime.SecondsPerHour * c.Hour +
                DaggerfallDateTime.SecondsPerMinute * c.Minute +
                (int)c.Second;
            return (ulong)total;
        }
    }
}
