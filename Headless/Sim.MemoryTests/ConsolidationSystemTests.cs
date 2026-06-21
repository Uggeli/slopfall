using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationSystemTests
    {
        static readonly AgentMemoryConfig Cfg = new AgentMemoryConfig(1, 4, 1, EncodeConfig.Default, ConsolidationConfig.Default);

        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly BehaviorRegistry Behavior;
            public readonly ConsolidationSystem System;
            public Rig() { Behavior = new BehaviorRegistry(E); System = new ConsolidationSystem(E, Behavior, Cfg); }
            public void SetActivity(EntityId id, ActivityKind a) => E.Publish(new BehaviorSetIntent { Id = id, Data = new BehaviorData { Activity = a } });
            public void Step() { E.Tick(); Behavior.Update(0); System.Update(0); }
            public MemoryConsolidateIntent[] Next() { E.Tick(); return E.GetEvents<MemoryConsolidateIntent>().ToArray(); }
        }

        [Fact]
        public void SleepingAgent_EmitsConsolidate()
        {
            var r = new Rig();
            r.SetActivity(new EntityId(1), ActivityKind.Sleep);
            r.Step();
            var c = r.Next();
            Assert.Single(c);
            Assert.Equal(new EntityId(1), c[0].Agent);
        }

        [Fact]
        public void AwakeAgent_EmitsNothing()
        {
            var r = new Rig();
            r.SetActivity(new EntityId(1), ActivityKind.Work);
            r.Step();
            Assert.Empty(r.Next());
        }
    }
}
