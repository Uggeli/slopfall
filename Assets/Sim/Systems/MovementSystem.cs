using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Walks Moving entities along town streets: paths come from
    /// TownPathfinder (hierarchical block-gate + tile A*), cached per entity
    /// until the target changes. Falls back to a straight line when no town
    /// grid is loaded or no path exists. Owns civilian Position writes
    /// headlessly. Emits ArrivedAtTargetEvent; OddSystem flips the phase,
    /// keeping BehaviorRegistry single-writer.
    public sealed class MovementSystem : ISystem
    {
        public const float WalkSpeed = 1.4f;        // meters per game-second
        const float ArriveDistance = 1.0f;          // meters
        const float ReplanDistance = 2.0f;          // target moved → new path

        sealed class Plan
        {
            public float TargetX, TargetZ;
            public List<PathPoint> Points = new List<PathPoint>();
            public int Next;
        }

        SimulationContext _ctx;
        readonly Dictionary<EntityId, Plan> _plans = new Dictionary<EntityId, Plan>();
        readonly List<PathPoint> _scratch = new List<PathPoint>();

        // Profiling: pathfinds vs moving-agent-ticks. Ratio near 1 = per-tick
        // replanning (churn); near 0 = the per-journey cache is doing its job.
        public long PathfindCalls, MovingAgentTicks;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;

            float budgetFull = (float)(WalkSpeed * clock.DeltaGameSeconds);
            var grid = _ctx.TownGrid.Current;

            foreach (var kv in _ctx.Behavior.All)
            {
                var behavior = kv.Value;
                if (behavior.Phase != ActivityPhase.Moving) continue;
                if (!_ctx.Position.TryGet(kv.Key, out var pos)) continue;
                MovingAgentTicks++;

                var plan = EnsurePlan(kv.Key, grid, pos, behavior);

                // Walk the waypoint chain with this tick's movement budget.
                float budget = budgetFull;
                float x = pos.X, z = pos.Z, yaw = pos.Yaw;
                while (budget > 0 && plan.Next < plan.Points.Count)
                {
                    var wp = plan.Points[plan.Next];
                    float dx = wp.X - x, dz = wp.Z - z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist <= 0.01f) { plan.Next++; continue; }

                    if (dist <= budget)
                    {
                        x = wp.X; z = wp.Z;
                        budget -= dist;
                        yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                        plan.Next++;
                    }
                    else
                    {
                        x += dx / dist * budget;
                        z += dz / dist * budget;
                        yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                        budget = 0;
                    }
                }

                _ctx.Position.Set(kv.Key, x, pos.Y, z, yaw);

                float gx = behavior.TargetX - x, gz = behavior.TargetZ - z;
                if (plan.Next >= plan.Points.Count
                    || gx * gx + gz * gz <= ArriveDistance * ArriveDistance)
                {
                    _plans.Remove(kv.Key);
                    _ctx.Events.Emit(new ArrivedAtTargetEvent { Entity = kv.Key });
                }
            }
        }

        Plan EnsurePlan(EntityId id, TownGridData grid, PositionData pos, BehaviorData behavior)
        {
            if (_plans.TryGetValue(id, out var plan))
            {
                float dx = plan.TargetX - behavior.TargetX, dz = plan.TargetZ - behavior.TargetZ;
                if (dx * dx + dz * dz < ReplanDistance * ReplanDistance)
                    return plan;
            }

            PathfindCalls++;
            plan = new Plan { TargetX = behavior.TargetX, TargetZ = behavior.TargetZ };
            bool enterBuilding = behavior.TargetBuilding >= 0;
            if (grid != null && TownPathfinder.FindPath(grid, pos.X, pos.Z,
                behavior.TargetX, behavior.TargetZ, _scratch, enterBuilding))
            {
                plan.Points.AddRange(_scratch);
            }
            else
            {
                // No grid (unit tests, wilderness) or no route — walk straight.
                plan.Points.Add(new PathPoint { X = behavior.TargetX, Z = behavior.TargetZ });
            }
            _plans[id] = plan;
            return plan;
        }
    }
}
