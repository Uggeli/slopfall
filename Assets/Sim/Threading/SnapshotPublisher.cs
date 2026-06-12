using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// One entity as a client sees it. Plain data — this struct is the
    /// per-entity unit of the server→client payload.
    public struct EntitySnap
    {
        public int Id;
        public float X, Y, Z, Yaw;
        public EntityKind Kind;
        public ActivityKind Activity;
        public ActivityPhase Phase;
    }

    /// Immutable snapshot of sim state for render/client consumption. Sim
    /// publishes one at end of every tick; clients read Latest and interpolate
    /// between consecutive snapshots. Static world data (buildings, map
    /// layout) is NOT here — clients fetch that once at connect time.
    public sealed class RenderSnapshot
    {
        public long Tick;
        public double SimSeconds;
        public double WallClockSeconds;

        // World state.
        public int Hour, Minute;
        public bool IsNight;
        public float SunIntensity;
        public WeatherKind Weather;

        // Who's where doing what. Array is owned by the snapshot — never
        // mutated after publish.
        public EntitySnap[] Entities = System.Array.Empty<EntitySnap>();
    }

    /// Builds a RenderSnapshot from the registries. Runs on the sim thread at
    /// end of tick, so reads are coherent with the tick that just completed.
    public static class SnapshotBuilder
    {
        public static RenderSnapshot Build(SimulationContext ctx, double wallClockSeconds)
        {
            var clock = ctx.WorldClock.Current;
            var light = ctx.Lighting.Current;
            var weather = ctx.Weather.Current;

            var entities = new EntitySnap[ctx.Identity.Count];
            int n = 0;
            foreach (var kv in ctx.Identity.All)
            {
                if (n >= entities.Length) break;    // registry grew mid-walk; rare, harmless
                var snap = new EntitySnap { Id = kv.Key.Value, Kind = kv.Value.Kind };

                if (ctx.Position.TryGet(kv.Key, out var pos))
                {
                    snap.X = pos.X; snap.Y = pos.Y; snap.Z = pos.Z; snap.Yaw = pos.Yaw;
                }
                if (ctx.Behavior.TryGet(kv.Key, out var behavior))
                {
                    snap.Activity = behavior.Activity;
                    snap.Phase = behavior.Phase;
                }
                entities[n++] = snap;
            }
            if (n < entities.Length)
                System.Array.Resize(ref entities, n);

            return new RenderSnapshot
            {
                SimSeconds = ctx.Time.Elapsed,
                Tick = ctx.Time.Tick,
                WallClockSeconds = wallClockSeconds,
                Hour = clock.Hour,
                Minute = clock.Minute,
                IsNight = light.IsNight,
                SunIntensity = light.SunIntensity,
                Weather = weather.Kind,
                Entities = entities,
            };
        }
    }

    /// Single-slot atomic publisher. Sim writes via Publish (Interlocked.Exchange),
    /// main thread reads via Latest (Volatile.Read).
    public sealed class SnapshotPublisher
    {
        RenderSnapshot _latest;

        public RenderSnapshot Latest => Volatile.Read(ref _latest);

        public void Publish(RenderSnapshot snapshot) => Interlocked.Exchange(ref _latest, snapshot);
    }
}
