namespace DaggerfallWorkshop.Sim.Engine
{
    /// Bridges ServiceQueue membership (SharedActivityRegistry) to agent behaviour:
    ///  - a Served agent still in Queued phase is flipped to Doing (service begins,
    ///    its clock reset to the activity's full duration — waiting doesn't erode it);
    ///  - each Waiter is steered to its slot position so the line is physical & visible.
    /// CQRS: reads registries, publishes BehaviorSetIntent only.
    public sealed class SharedActivitySystem : SimSystem
    {
        public const float QueueSpacing = 1.5f;   // metres between people in line (tunable)

        readonly SharedActivityRegistry _shared;
        readonly BehaviorRegistry _behavior;

        public SharedActivitySystem(EventBus events, SharedActivityRegistry shared, BehaviorRegistry behavior)
            : base(events)
        {
            _shared = shared;
            _behavior = behavior;
        }

        public override void Update(long tick)
        {
            // Promote: Served agents whose behaviour still says Queued → start service.
            var served = _shared.ServedSnapshot();
            for (int i = 0; i < served.Count; i++)
            {
                var id = served[i];
                if (!_behavior.TryGet(id, out var b) || b.Phase != ActivityPhase.Queued) continue;
                var spec = ActivityCatalog.SpecFor(b.Activity);
                Events.Publish(new BehaviorSetIntent
                {
                    Id = id,
                    Data = new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Doing,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = b.TargetX,
                        TargetZ = b.TargetZ,
                        RemainingGameMinutes = spec != null ? spec.DurationMinutes : 0,
                        SinceDecisionGameMinutes = 0,
                        TargetItem = b.TargetItem,
                    }
                });
            }

            // Place waiters at their slot so the line is visible and shuffles forward.
            foreach (var kv in _behavior.All)
            {
                var id = kv.Key;
                var b = kv.Value;
                if (b.Phase != ActivityPhase.Queued) continue;
                int pos = _shared.PositionOf(id);
                if (pos < 0) continue;                          // not waiting (e.g. served this tick)
                if (!_shared.AnchorOf(id, out var anchor)) continue;
                if (!_shared.TryGet(anchor, out var inst)) continue;
                float slotX = inst.AnchorX;
                float slotZ = inst.AnchorZ - pos * QueueSpacing;
                if (b.TargetX == slotX && b.TargetZ == slotZ) continue;   // already targeting it
                Events.Publish(new BehaviorSetIntent
                {
                    Id = id,
                    Data = new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Queued,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = slotX,
                        TargetZ = slotZ,
                        RemainingGameMinutes = b.RemainingGameMinutes,
                        SinceDecisionGameMinutes = b.SinceDecisionGameMinutes,
                        TargetItem = b.TargetItem,
                    }
                });
            }
        }
    }
}
