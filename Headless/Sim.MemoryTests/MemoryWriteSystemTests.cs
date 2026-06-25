using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryWriteSystemTests
    {
        // Cadence 1 so every agent is "due" every tick — keeps the test simple.
        static readonly AgentMemoryConfig Cfg = new AgentMemoryConfig(1, 4, 36000, EncodeConfig.Default, ConsolidationConfig.Default);

        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly SensedRegistry Sensed;
            public readonly PerceivableRegistry Perceivable;
            public readonly MemoryWriteSystem System;
            public Rig()
            {
                Sensed = new SensedRegistry(E);
                Perceivable = new PerceivableRegistry(E);
                System = new MemoryWriteSystem(E, Sensed, Perceivable, Cfg);
            }
            public void Step() { E.Tick(); Sensed.Update(0); Perceivable.Update(0); System.Update(0); }
            public List<MemoryPerceiveIntent> NextPerceives()
            {
                E.Tick();
                return E.GetEvents<MemoryPerceiveIntent>().ToArray().ToList();
            }
        }

        [Fact]
        public void DueAgent_EmitsPerceive_ForSensed_WithSignaturePerceptSplit()
        {
            var r = new Rig();
            var agent = new EntityId(1);
            var seen = new EntityId(50);
            // perceived entity's bag: kind(1001) + race(3007) [identity] + activity(4005) [state]
            r.Perceivable.Seed(seen, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);
            r.Perceivable.Seed(seen, new AtomTypeId(3007), Fixed.One);
            r.Perceivable.Seed(seen, new AtomTypeId(4005), Fixed.One);
            r.E.Publish(new SensedSetIntent { Id = agent, Sensed = new List<EntityId> { seen } });
            r.Step();

            var emitted = r.NextPerceives();
            Assert.Single(emitted);
            var p = emitted[0];
            Assert.Equal(agent, p.Perceiver);
            Assert.Equal(seen, p.Perceived);
            // v1 THINGS = identity dossiers: signature is identity-only; percept may carry
            // transient (activity/somatic) atoms. Categories form on identity and recognition matches.
            // Signature is identity-only; the percept may carry transient (activity/somatic) atoms,
            // so assert the split via the catalog, not a band boundary.
            Assert.All(p.Signature.Atoms, a => Assert.True(AtomCatalog.For(a.Type).IsIdentity,
                "signature carried a non-identity atom: " + AtomCatalog.NameOf(a.Type)));
            Assert.DoesNotContain(p.Percept.Atoms, a => AtomCatalog.For(a.Type).Category == AtomCategory.Activity);
            Assert.Contains(p.Signature.Atoms, a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
        }

        [Fact]
        public void NotDueAgent_EmitsNothing()
        {
            // Cadence 10, agent whose phase makes it not due at tick 0.
            var cfg = new AgentMemoryConfig(10, 4, 36000, EncodeConfig.Default, ConsolidationConfig.Default);
            var e = new EventBus();
            var sensed = new SensedRegistry(e);
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryWriteSystem(e, sensed, perceivable, cfg);
            var agent = new EntityId(3);   // (0 + 3) % 10 != 0 -> not due at tick 0
            e.Publish(new SensedSetIntent { Id = agent, Sensed = new List<EntityId> { new EntityId(50) } });
            e.Tick(); sensed.Update(0); perceivable.Update(0); sys.Update(0);
            e.Tick();
            Assert.Empty(e.GetEvents<MemoryPerceiveIntent>().ToArray());
        }
    }
}
