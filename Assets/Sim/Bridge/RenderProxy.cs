using UnityEngine;

namespace DaggerfallWorkshop.Sim
{
    /// Unity-side render driver. Reads the latest sim snapshot each frame and
    /// (in Phase 1+) drives GameObject transforms / animator params to match,
    /// interpolating between consecutive snapshots for smoothness.
    ///
    /// Phase 0: tracks the last two snapshots so interpolation infra is in place;
    /// no visual output yet.
    public sealed class RenderProxy : MonoBehaviour
    {
        public RenderSnapshot Previous { get; private set; }
        public RenderSnapshot Current { get; private set; }

        /// Fraction of the way from Previous to Current, based on wall clock.
        /// 0 = at Previous, 1 = at Current. Pass to Vector3.Lerp etc.
        public float InterpolationAlpha { get; private set; }

        void Update()
        {
            var driver = SimDriver.Instance;
            if (driver == null) return;
            var latest = driver.Snapshots.Latest;
            if (latest == null) return;

            if (!ReferenceEquals(latest, Current))
            {
                Previous = Current;
                Current = latest;
            }

            if (Previous != null && Current != null && Current.Tick > Previous.Tick)
            {
                var tickIntervalSec = (float)driver.Context.Time.TickIntervalSeconds;
                var wallNow = Time.realtimeSinceStartup;
                var elapsed = wallNow - (float)Current.WallClockSeconds;
                InterpolationAlpha = Mathf.Clamp01(elapsed / tickIntervalSec);
            }
            else
            {
                InterpolationAlpha = 0f;
            }
        }
    }
}
