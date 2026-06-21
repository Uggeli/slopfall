using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AgentMemoryRegistryTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static (EventBus, AgentMemoryRegistry) New()
        {
            var e = new EventBus();
            return (e, new AgentMemoryRegistry(e, AgentMemoryConfig.Default));
        }
        static void Tick(EventBus e, AgentMemoryRegistry r) { e.Tick(); r.Update(0); }

        [Fact]
        public void Seed_CreatesEmptyMemory()
        {
            var (_, r) = New();
            r.Seed(new EntityId(1));
            Assert.True(r.TryGet(new EntityId(1), out var mem));
            Assert.Equal(0, mem.Meanings.Count);
            Assert.Equal(0, mem.Stores.Things.Count);
        }

        [Fact]
        public void Perceive_NovelEntity_WritesThingsRecord()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            var sig = Bag((1001, 1.0), (3007, 1.0));   // kind + race
            e.Publish(new MemoryPerceiveIntent { Perceiver = new EntityId(1), Perceived = new EntityId(50), Signature = sig, Percept = sig, Arousal = Fixed.Zero });
            Tick(e, r);

            r.TryGet(new EntityId(1), out var mem);
            Assert.Equal(1, mem.Stores.Things.Count);                  // novel -> verbatim record
            Assert.True(mem.Stores.Things.TryGet(new MemoryKey(50), out var rec));
            Assert.True(rec.IsNovel);
        }

        [Fact]
        public void Perceive_UnseededPerceiver_NoCrash()
        {
            var (e, r) = New();
            e.Publish(new MemoryPerceiveIntent { Perceiver = new EntityId(9), Perceived = new EntityId(50), Signature = AtomBag.Empty, Percept = AtomBag.Empty, Arousal = Fixed.Zero });
            Tick(e, r);
            Assert.Equal(0, r.Count);
        }

        [Fact]
        public void Consolidate_MintsCategory_FromClusteredNovels()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            var sig = Bag((1001, 1.0), (3007, 1.0));
            // Three distinct perceived entities, same atoms -> three novel THINGS records.
            foreach (var pid in new[] { 50, 51, 52 })
                e.Publish(new MemoryPerceiveIntent { Perceiver = new EntityId(1), Perceived = new EntityId(pid), Signature = sig, Percept = sig, Arousal = Fixed.Zero });
            Tick(e, r);
            r.TryGet(new EntityId(1), out var mem);
            Assert.Equal(3, mem.Stores.Things.Count);
            Assert.Equal(0, mem.Meanings.Count);

            e.Publish(new MemoryConsolidateIntent { Agent = new EntityId(1) });
            Tick(e, r);
            r.TryGet(new EntityId(1), out mem);
            Assert.True(mem.Meanings.Count >= 1);                      // a category was minted from the cluster
        }

        [Fact]
        public void Despawn_DropsMemory()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            e.Publish(new DespawnedEvent { Entity = new EntityId(1) });
            Tick(e, r);
            Assert.False(r.TryGet(new EntityId(1), out _));
        }
    }
}
