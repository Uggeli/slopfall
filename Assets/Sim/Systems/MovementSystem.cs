using System;

namespace DaggerfallWorkshop.Sim
{
    /// Walks Moving entities toward their behavior target in straight lines
    /// (no collision or pathfinding yet — sim-side geometry is Phase 5 work).
    /// Owns civilian Position writes headlessly. Emits ArrivedAtTargetEvent;
    /// OddSystem flips the phase, keeping BehaviorRegistry single-writer.
    public sealed class MovementSystem : ISystem
    {
        public const float WalkSpeed = 1.4f;        // meters per game-second
        const float ArriveDistance = 1.0f;          // meters

        SimulationContext _ctx;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;

            float step = (float)(WalkSpeed * _ctx.Time.TickIntervalSeconds * clock.TimeScale);

            foreach (var kv in _ctx.Behavior.All)
            {
                var behavior = kv.Value;
                if (behavior.Phase != ActivityPhase.Moving) continue;
                if (!_ctx.Position.TryGet(kv.Key, out var pos)) continue;

                float dx = behavior.TargetX - pos.X;
                float dz = behavior.TargetZ - pos.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);

                if (dist <= ArriveDistance)
                {
                    _ctx.Events.Emit(new ArrivedAtTargetEvent { Entity = kv.Key });
                    continue;
                }

                float move = step < dist ? step : dist;
                float nx = pos.X + dx / dist * move;
                float nz = pos.Z + dz / dist * move;
                float yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);

                _ctx.Position.Set(kv.Key, nx, pos.Y, nz, yaw);

                if (dist - move <= ArriveDistance)
                    _ctx.Events.Emit(new ArrivedAtTargetEvent { Entity = kv.Key });
            }
        }
    }
}
