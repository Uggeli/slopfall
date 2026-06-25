using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SenseClearTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly WorldClockRegistry Clock;
            public readonly BehaviorRegistry Behavior;
            public readonly PositionRegistry Position;
            public readonly CreatureRegistry Creatures;
            public readonly TownGridRegistry Grid;
            public readonly SensedRegistry Sensed;
            public readonly SenseSystem System;

            public Rig()
            {
                Clock = new WorldClockRegistry(E);
                Behavior = new BehaviorRegistry(E);
                Position = new PositionRegistry(E);
                Creatures = new CreatureRegistry(E);
                Grid = new TownGridRegistry(E);                 // null grid → no occlusion checks
                Sensed = new SensedRegistry(E);
                System = new SenseSystem(E, Clock, Behavior, Position, Creatures, Grid, seed: 42);
                E.Publish(new WorldClockSetIntent { Year = 405, Month = 1, Day = 1, Hour = 8,
                    Minute = 0, Second = 0, TimeScale = 12f, DeltaGameSeconds = 1.2 });
                E.Tick(); Clock.Update(0);
            }

            public void Place(int id, float x, float z)
                => E.Publish(new PositionSetIntent { Id = new EntityId(id), X = x, Y = 0f, Z = z, Yaw = 0f });
            public void Act(int id, ActivityPhase phase, ActivityKind a)
                => E.Publish(new BehaviorSetIntent { Id = new EntityId(id), Data = new BehaviorData { Phase = phase, Activity = a } });
            public void Step(long t)
            {
                E.Tick();
                Clock.Update(t); Behavior.Update(t); Position.Update(t); Creatures.Update(t);
                Grid.Update(t); Sensed.Update(t); System.Update(t);
            }
        }

        [Fact]
        public void IndoorAgent_SensedList_IsCleared()
        {
            var r = new Rig();
            r.Place(1, 0f, 0f); r.Place(2, 1f, 0f);
            r.Act(1, ActivityPhase.Moving, ActivityKind.Wander);
            r.Act(2, ActivityPhase.Moving, ActivityKind.Wander);
            r.Step(0);                                          // sense-tick (0 % 5 == 0): agent 1 senses agent 2
            r.Step(1);                                          // Sensed applies
            Assert.Contains(new EntityId(2), r.Sensed.Of(new EntityId(1)));

            r.Act(1, ActivityPhase.Doing, ActivityKind.EatTavern);   // agent 1 goes indoors
            r.Step(5);                                          // next sense-tick: indoor → empty SensedSetIntent
            r.Step(6);                                          // applies
            Assert.Empty(r.Sensed.Of(new EntityId(1)));
        }
    }
}
