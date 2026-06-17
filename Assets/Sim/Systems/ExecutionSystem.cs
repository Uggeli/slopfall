using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Carries out decisions. Sole writer of BehaviorRegistry — the split from
    /// OddSystem, which now only *decides* (writes an Intent). Execution:
    ///   - reifies a fresh Intent into Behavior (Moving toward the spot, or Doing
    ///     if already there), announcing it;
    ///   - ticks the Doing countdown each tick;
    ///   - flips Moving → Doing when MovementSystem reports arrival;
    ///   - applies rung-2 interrupts (a passing friend → Chat; an embodied ask →
    ///     SeekHelp journey).
    /// Runs immediately after OddSystem and before MovementSystem, so a decision
    /// this tick becomes movement this tick — same timing as before the split.
    public sealed class ExecutionSystem : ISystem
    {
        const float ArriveImmediatelyDistance = 3f;     // meters

        SimulationContext _ctx;
        readonly List<EntityId> _arrivals = new List<EntityId>();
        readonly List<GreetingEvent> _greetings = new List<GreetingEvent>();
        readonly List<AskJourneyEvent> _askJourneys = new List<AskJourneyEvent>();
        readonly HashSet<EntityId> _appliedThisTick = new HashSet<EntityId>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<ArrivedAtTargetEvent>(e => _arrivals.Add(e.Entity));
            ctx.Events.Subscribe<GreetingEvent>(e => _greetings.Add(e));
            ctx.Events.Subscribe<AskJourneyEvent>(e => _askJourneys.Add(e));
        }

        public void ProcessEvents()
        {
            // Arrival: the walk is done — settle into the activity.
            for (int i = 0; i < _arrivals.Count; i++)
            {
                if (_ctx.Behavior.TryGet(_arrivals[i], out var b) && b.Phase == ActivityPhase.Moving)
                {
                    _ctx.Behavior.Set(_arrivals[i], new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Doing,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = b.TargetX,
                        TargetZ = b.TargetZ,
                        RemainingGameMinutes = b.RemainingGameMinutes,
                    });
                }
            }
            _arrivals.Clear();

            // Rung-2 interrupts: a friend on the street trumps the errand — both
            // stop for a quick chat where they stand, then re-decide.
            for (int i = 0; i < _greetings.Count; i++)
            {
                InterruptIntoChat(_greetings[i].A);
                InterruptIntoChat(_greetings[i].B);
            }
            _greetings.Clear();

            // Embodied asking: put the pauper on the road to their mark.
            for (int i = 0; i < _askJourneys.Count; i++)
            {
                var e = _askJourneys[i];
                _ctx.Behavior.Set(e.Asker, new BehaviorData
                {
                    Activity = ActivityKind.SeekHelp,
                    Phase = ActivityPhase.Moving,
                    TargetBuilding = -1,                // stop on the street beside them
                    TargetX = e.TargetX,
                    TargetZ = e.TargetZ,
                    RemainingGameMinutes = ActivityCatalog.SeekHelp.DurationMinutes,
                });
            }
            _askJourneys.Clear();
        }

        void InterruptIntoChat(EntityId id)
        {
            if (!_ctx.Behavior.TryGet(id, out var b)) return;
            bool interruptible = b.Phase == ActivityPhase.Moving
                || b.Activity == ActivityKind.Wander
                || b.Activity == ActivityKind.Visit
                || b.Activity == ActivityKind.Idle;
            if (!interruptible) return;
            if (!_ctx.Position.TryGet(id, out var pos)) return;

            _ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = ActivityKind.Chat,
                Phase = ActivityPhase.Doing,
                TargetBuilding = -1,
                TargetX = pos.X,
                TargetZ = pos.Z,
                RemainingGameMinutes = ActivityCatalog.Chat.DurationMinutes,
            });
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = clock.DeltaGameSeconds / 60.0;

            // 1. Reify intents the decider committed to this tick.
            _appliedThisTick.Clear();
            foreach (var kv in _ctx.Intent.All)
            {
                ApplyIntent(kv.Key, kv.Value);
                _appliedThisTick.Add(kv.Key);
            }
            foreach (var id in _appliedThisTick)
                _ctx.Intent.Remove(id);

            // 2. Advance the clock on activities in progress that weren't just
            //    (re)decided. Moving agents keep walking — MovementSystem owns
            //    them until they arrive.
            foreach (var kv in _ctx.Behavior.All)
            {
                if (_appliedThisTick.Contains(kv.Key)) continue;
                var b = kv.Value;
                if (b.Phase != ActivityPhase.Doing) continue;

                _ctx.Behavior.Set(kv.Key, new BehaviorData
                {
                    Activity = b.Activity,
                    Phase = ActivityPhase.Doing,
                    TargetBuilding = b.TargetBuilding,
                    TargetX = b.TargetX,
                    TargetZ = b.TargetZ,
                    RemainingGameMinutes = b.RemainingGameMinutes - gameMinutes,
                    SinceDecisionGameMinutes = b.SinceDecisionGameMinutes + gameMinutes,
                });
            }
        }

        void ApplyIntent(EntityId id, IntentData intent)
        {
            if (!_ctx.Position.TryGet(id, out var pos)) return;
            _ctx.Behavior.TryGet(id, out var current);

            // Same activity still winning mid-flight = resume: keep the remaining
            // duration and target, don't re-announce it.
            double remaining;
            float targetX, targetZ;
            if (intent.Resume && current != null)
            {
                remaining = current.RemainingGameMinutes;
                targetX = current.TargetX;
                targetZ = current.TargetZ;
            }
            else
            {
                remaining = intent.Duration;
                targetX = intent.X;
                targetZ = intent.Z;
            }

            float ddx = intent.X - pos.X, ddz = intent.Z - pos.Z;
            bool atSpot = intent.Resume
                || (ddx * ddx + ddz * ddz) <= ArriveImmediatelyDistance * ArriveImmediatelyDistance;

            _ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = intent.Activity,
                Phase = atSpot ? ActivityPhase.Doing : ActivityPhase.Moving,
                TargetBuilding = intent.Building,
                TargetX = targetX,
                TargetZ = targetZ,
                RemainingGameMinutes = remaining,
                SinceDecisionGameMinutes = 0,
            });

            if (!intent.Resume)
                _ctx.Events.Emit(new ActivityStartedEvent { Entity = id, Activity = intent.Activity, TargetBuilding = intent.Building });
        }
    }
}
