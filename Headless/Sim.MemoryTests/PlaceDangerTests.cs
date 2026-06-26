using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PlaceDangerTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly SensedRegistry Sensed;
            public readonly CreatureRegistry Creatures;
            public readonly PositionRegistry Position;
            public readonly BuildingRegistry Buildings;
            public readonly PerceivableRegistry Perceivable;
            public readonly PlaceDangerSystem System;

            public Rig()
            {
                Sensed = new SensedRegistry(E);
                Creatures = new CreatureRegistry(E);
                Position = new PositionRegistry(E);
                Buildings = new BuildingRegistry(E);
                Perceivable = new PerceivableRegistry(E);
                System = new PlaceDangerSystem(E, Sensed, Creatures, Position, Buildings, Perceivable);
            }

            // Flip + apply queued intents into the registries.
            public void Apply() { E.Tick(); Sensed.Update(0); Creatures.Update(0); Position.Update(0); Perceivable.Update(0); }
            // Flip so the DeathEvent is readable, then run the danger system.
            public void Run() { E.Tick(); System.Update(0); }
            // Flip + read what the system published.
            public PlaceObserveIntent[] Emitted() { E.Tick(); return E.GetEvents<PlaceObserveIntent>().ToArray(); }
            // Flip + run the danger system + flip again so the emitted Utterances are readable.
            public Utterance[] RunCapturingUtterances()
            { E.Tick(); System.Update(0); E.Tick(); return E.GetEvents<Utterance>().ToArray(); }
        }

        [Fact]
        public void WitnessOfCreatureKill_LearnsDangerAtNearestBuilding()
        {
            var r = new Rig();
            var witness = new EntityId(1);
            var victim = new EntityId(2);
            var killer = new EntityId(99);   // the creature

            int building = r.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 0f, Z = 0f });
            r.Position.Seed(victim, 1f, 0f, 1f, 0f);   // dies right next to the building

            r.E.Publish(new CreatureSetIntent { Id = killer, Data = new CreatureData() });
            r.E.Publish(new SensedSetIntent { Id = witness, Sensed = new List<EntityId> { victim } });
            r.Apply();

            r.E.Publish(new DeathEvent { Entity = victim, Killer = killer });
            r.Run();

            var emitted = r.Emitted();
            Assert.Single(emitted);
            Assert.Equal(witness, emitted[0].Agent);
            Assert.Equal(building, emitted[0].Building);
            Assert.Equal(PlaceAtoms.Danger, emitted[0].Atom);
            Assert.Equal(Fixed.One, emitted[0].Value);
        }

        [Fact]
        public void NonWitness_LearnsNothing()
        {
            var r = new Rig();
            var blind = new EntityId(1);     // sensed nobody
            var victim = new EntityId(2);
            var killer = new EntityId(99);

            r.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 0f, Z = 0f });
            r.Position.Seed(victim, 1f, 0f, 1f, 0f);
            r.E.Publish(new CreatureSetIntent { Id = killer, Data = new CreatureData() });
            r.E.Publish(new SensedSetIntent { Id = blind, Sensed = new List<EntityId>() });
            r.Apply();

            r.E.Publish(new DeathEvent { Entity = victim, Killer = killer });
            r.Run();

            Assert.Empty(r.Emitted());
        }

        [Fact]
        public void NonCreatureKill_LearnsNoDanger()
        {
            var r = new Rig();
            var witness = new EntityId(1);
            var victim = new EntityId(2);
            var killer = new EntityId(50);   // NOT a creature (e.g. a guard execution)

            r.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 0f, Z = 0f });
            r.Position.Seed(victim, 1f, 0f, 1f, 0f);
            r.E.Publish(new SensedSetIntent { Id = witness, Sensed = new List<EntityId> { victim } });
            r.Apply();

            r.E.Publish(new DeathEvent { Entity = victim, Killer = killer });
            r.Run();

            Assert.Empty(r.Emitted());
        }

        [Fact]
        public void WitnessShout_CarriesTheKillersKind()
        {
            var r = new Rig();
            var witness = new EntityId(1);
            var victim = new EntityId(2);
            var killer = new EntityId(99);   // the creature

            r.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 0f, Z = 0f });
            r.Position.Seed(victim, 1f, 0f, 1f, 0f);
            r.E.Publish(new CreatureSetIntent { Id = killer, Data = new CreatureData() });
            r.E.Publish(new SensedSetIntent { Id = witness, Sensed = new List<EntityId> { victim } });
            r.Apply();
            r.Perceivable.Seed(killer, AtomName.Drifter.ToId(), Fixed.One);   // this killer is a Drifter

            r.E.Publish(new DeathEvent { Entity = victim, Killer = killer });
            var shouts = r.RunCapturingUtterances();   // ticks PlaceDangerSystem; returns this tick's Utterances

            var shout = shouts.Single(u => u.Speaker == witness);
            Assert.NotNull(shout.SubjectKind);
            Assert.Contains(shout.SubjectKind.Atoms, a => a.Type == AtomName.Drifter.ToId());  // names the ACTUAL kind
        }
    }
}
