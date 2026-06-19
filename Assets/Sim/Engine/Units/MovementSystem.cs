using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.MovementSystem. Walks Moving
    // entities along town streets: paths come from TownPathfinder (the parent-namespace
    // static helper, reused unqualified), cached per entity until the target changes.
    // Falls back to a straight line when no town grid is loaded or no path exists.
    //
    // Where state went: the old system's private `_plans` (per-entity active path — true
    // cross-tick state) had no home, so it becomes PathRegistry. MovementSystem now reads
    // Behavior+Position+TownGrid+Path read-only, advances along the cached plan (replanning
    // via TownPathfinder when the target has moved), and Publishes:
    //   - PositionSetIntent     (the move),
    //   - PathSetIntent         (cache the plan + its advanced Next cursor for next tick),
    //   - PathClearIntent       (journey done — drop the cache so the next replans),
    //   - ArrivedAtTargetEvent  (so OddSystem flips the phase; keeps Behavior single-writer).
    //
    // The old `_scratch` reusable waypoint buffer was per-tick scratch with no business
    // crossing ticks; it becomes a LOCAL list inside Update. The PathfindCalls /
    // MovingAgentTicks profiling counters were instance fields holding tick-spanning state
    // — they are dropped (no home under statelessness; pure instrumentation, not domain
    // logic). All movement math is preserved exactly.
    public sealed class MovementSystem : SimSystem
    {
        public const float WalkSpeed = 1.4f;        // meters per game-second
        const float ArriveDistance = 1.0f;          // meters
        const float ReplanDistance = 2.0f;          // target moved → new path

        readonly WorldClockRegistry _clock;
        readonly BehaviorRegistry _behavior;
        readonly PositionRegistry _position;
        readonly TownGridRegistry _townGrid;
        readonly PathRegistry _path;

        public MovementSystem(EventBus events, WorldClockRegistry clock, BehaviorRegistry behavior,
            PositionRegistry position, TownGridRegistry townGrid, PathRegistry path, int seed) : base(events)
        {
            _clock = clock;
            _behavior = behavior;
            _position = position;
            _townGrid = townGrid;
            _path = path;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;

            float budgetFull = (float)(WalkSpeed * clock.DeltaGameSeconds);
            var grid = _townGrid.Current;
            var scratch = new List<PathPoint>();   // per-tick scratch (was the instance _scratch)

            foreach (var kv in _behavior.All)
            {
                var behavior = kv.Value;
                if (behavior.Phase != ActivityPhase.Moving) continue;
                if (!_position.TryGet(kv.Key, out var pos)) continue;

                var plan = EnsurePlan(kv.Key, grid, pos, behavior, scratch);

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

                Events.Publish(new PositionSetIntent { Id = kv.Key, X = x, Y = pos.Y, Z = z, Yaw = yaw });

                float gx = behavior.TargetX - x, gz = behavior.TargetZ - z;
                if (plan.Next >= plan.Points.Count
                    || gx * gx + gz * gz <= ArriveDistance * ArriveDistance)
                {
                    Events.Publish(new PathClearIntent { Id = kv.Key });
                    Events.Publish(new ArrivedAtTargetEvent { Entity = kv.Key });
                }
                else
                {
                    // Journey continues — cache the plan with its advanced Next cursor for
                    // next tick (the old code mutated plan.Next in the live _plans entry).
                    Events.Publish(new PathSetIntent { Id = kv.Key, Plan = plan });
                }
            }
        }

        // Returns a WORKING Plan for this tick: either a fresh-copy of the cached plan
        // (preserving its Next cursor) when the target hasn't moved, or a newly planned one
        // when it has / when none is cached. Never mutates the registry's stored Plan in
        // place — under phase separation the registry is read-only here, so the cursor
        // advance lands on the copy and is published back via PathSetIntent.
        Plan EnsurePlan(EntityId id, TownGridData grid, PositionData pos, BehaviorData behavior, List<PathPoint> scratch)
        {
            if (_path.TryGet(id, out var cached) && cached != null)
            {
                float dx = cached.TargetX - behavior.TargetX, dz = cached.TargetZ - behavior.TargetZ;
                if (dx * dx + dz * dz < ReplanDistance * ReplanDistance)
                {
                    // Reuse the cached route; copy so the cursor advance doesn't touch the
                    // settled registry value during the read phase.
                    var copy = new Plan { TargetX = cached.TargetX, TargetZ = cached.TargetZ, Next = cached.Next };
                    copy.Points.AddRange(cached.Points);
                    return copy;
                }
            }

            var plan = new Plan { TargetX = behavior.TargetX, TargetZ = behavior.TargetZ };
            bool enterBuilding = behavior.TargetBuilding >= 0;
            if (grid != null && TownPathfinder.FindPath(grid, pos.X, pos.Z,
                behavior.TargetX, behavior.TargetZ, scratch, enterBuilding))
            {
                plan.Points.AddRange(scratch);
            }
            else
            {
                // No grid (unit tests, wilderness) or no route — walk straight.
                plan.Points.Add(new PathPoint { X = behavior.TargetX, Z = behavior.TargetZ });
            }
            return plan;
        }
    }
}
