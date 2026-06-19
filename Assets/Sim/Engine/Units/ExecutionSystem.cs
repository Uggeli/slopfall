using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.ExecutionSystem. Carries out
    // decisions: sole emitter of BehaviorSetIntent (BehaviorRegistry is the sole
    // applier) and of IntentClearIntent (its explicit handoff ack replacing the old
    // in-place Intent.Remove after a read). Reads Intent + Position + Behavior +
    // WorldClock read-only; reacts to ArrivedAtTargetEvent / GreetingEvent /
    // AskJourneyEvent off the bus instead of the old subscribe-and-spool lists.
    //
    // Domain logic is preserved EXACTLY (arrival settle, rung-2 chat/ask interrupts,
    // intent reification, the Doing countdown). The old _arrivals/_greetings/_askJourneys
    // spools become GetEvents spans; _appliedThisTick (a within-tick dedup of agents
    // (re)decided this tick) becomes a LOCAL HashSet rebuilt each Update.
    public sealed class ExecutionSystem : SimSystem
    {
        const float ArriveImmediatelyDistance = 3f;     // meters

        readonly IntentRegistry _intent;
        readonly BehaviorRegistry _behavior;
        readonly PositionRegistry _position;
        readonly WorldClockRegistry _clock;

        public ExecutionSystem(EventBus events, IntentRegistry intent, BehaviorRegistry behavior,
                               PositionRegistry position, WorldClockRegistry clock) : base(events)
        {
            _intent = intent;
            _behavior = behavior;
            _position = position;
            _clock = clock;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = clock.DeltaGameSeconds / 60.0;

            // --- arrivals: the walk is done — settle into the activity. ---
            foreach (ref readonly var a in Events.GetEvents<ArrivedAtTargetEvent>())
            {
                if (_behavior.TryGet(a.Entity, out var b) && b.Phase == ActivityPhase.Moving)
                {
                    Events.Publish(new BehaviorSetIntent
                    {
                        Id = a.Entity,
                        Data = new BehaviorData
                        {
                            Activity = b.Activity,
                            Phase = ActivityPhase.Doing,
                            TargetBuilding = b.TargetBuilding,
                            TargetX = b.TargetX,
                            TargetZ = b.TargetZ,
                            RemainingGameMinutes = b.RemainingGameMinutes,
                            TargetItem = b.TargetItem,
                        }
                    });
                }
            }

            // --- rung-2 interrupts: a friend on the street trumps the errand — both
            //     stop for a quick chat where they stand, then re-decide. ---
            foreach (ref readonly var g in Events.GetEvents<GreetingEvent>())
            {
                InterruptIntoChat(g.A);
                InterruptIntoChat(g.B);
            }

            // --- embodied asking: put the pauper on the road to their mark. ---
            foreach (ref readonly var e in Events.GetEvents<AskJourneyEvent>())
            {
                Events.Publish(new BehaviorSetIntent
                {
                    Id = e.Asker,
                    Data = new BehaviorData
                    {
                        Activity = ActivityKind.SeekHelp,
                        Phase = ActivityPhase.Moving,
                        TargetBuilding = -1,                // stop on the street beside them
                        TargetX = e.TargetX,
                        TargetZ = e.TargetZ,
                        RemainingGameMinutes = ActivityCatalog.SeekHelp.DurationMinutes,
                    }
                });
            }

            // 1. Reify intents the decider committed to this tick. _appliedThisTick is a
            //    LOCAL within-tick dedup so the countdown below doesn't double-advance an
            //    agent that was just (re)decided.
            var appliedThisTick = new HashSet<EntityId>();
            foreach (var kv in _intent.All)
            {
                ApplyIntent(kv.Key, kv.Value);
                appliedThisTick.Add(kv.Key);
                Events.Publish(new IntentClearIntent { Id = kv.Key });   // explicit handoff ack
            }

            // 2. Advance the clock on activities in progress that weren't just
            //    (re)decided. Moving agents keep walking — MovementSystem owns them
            //    until they arrive.
            foreach (var kv in _behavior.All)
            {
                if (appliedThisTick.Contains(kv.Key)) continue;
                var b = kv.Value;
                if (b.Phase != ActivityPhase.Doing) continue;

                Events.Publish(new BehaviorSetIntent
                {
                    Id = kv.Key,
                    Data = new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Doing,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = b.TargetX,
                        TargetZ = b.TargetZ,
                        RemainingGameMinutes = b.RemainingGameMinutes - gameMinutes,
                        SinceDecisionGameMinutes = b.SinceDecisionGameMinutes + gameMinutes,
                        TargetItem = b.TargetItem,
                    }
                });
            }
        }

        void InterruptIntoChat(EntityId id)
        {
            if (!_behavior.TryGet(id, out var b)) return;
            bool interruptible = b.Phase == ActivityPhase.Moving
                || b.Activity == ActivityKind.Wander
                || b.Activity == ActivityKind.Visit
                || b.Activity == ActivityKind.Idle;
            if (!interruptible) return;
            if (!_position.TryGet(id, out var pos)) return;

            Events.Publish(new BehaviorSetIntent
            {
                Id = id,
                Data = new BehaviorData
                {
                    Activity = ActivityKind.Chat,
                    Phase = ActivityPhase.Doing,
                    TargetBuilding = -1,
                    TargetX = pos.X,
                    TargetZ = pos.Z,
                    RemainingGameMinutes = ActivityCatalog.Chat.DurationMinutes,
                }
            });
        }

        void ApplyIntent(EntityId id, IntentData intent)
        {
            if (!_position.TryGet(id, out var pos)) return;
            _behavior.TryGet(id, out var current);

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

            Events.Publish(new BehaviorSetIntent
            {
                Id = id,
                Data = new BehaviorData
                {
                    Activity = intent.Activity,
                    Phase = atSpot ? ActivityPhase.Doing : ActivityPhase.Moving,
                    TargetBuilding = intent.Building,
                    TargetX = targetX,
                    TargetZ = targetZ,
                    RemainingGameMinutes = remaining,
                    SinceDecisionGameMinutes = 0,
                    TargetItem = intent.Item,
                }
            });

            if (!intent.Resume)
                Events.Publish(new ActivityStartedEvent { Entity = id, Activity = intent.Activity, TargetBuilding = intent.Building });
        }
    }
}
