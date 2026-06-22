using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AgentMemoryStoresTests
    {
        [Fact]
        public void Stores_HaveSpecCapacities()
        {
            var m = new AgentMemoryStores();
            Assert.Equal(128, m.Places.Capacity);
            Assert.Equal(64, m.Things.Capacity);
            Assert.Equal(128, m.Events.Capacity);

            Assert.Equal(128, AgentMemoryStores.PlacesCap);
            Assert.Equal(64, AgentMemoryStores.ThingsCap);
            Assert.Equal(128, AgentMemoryStores.EventsCap);
            Assert.Equal(128, AgentMemoryStores.MeaningsCap);
        }

        [Fact]
        public void Stores_AreIndependent()
        {
            var m = new AgentMemoryStores();
            m.Places.Encode(new MemoryRecord(new MemoryKey(1), CategoryId.None, AtomBag.Empty, 100, 0, 0, MemoryFlags.None));
            Assert.Equal(1, m.Places.Count);
            Assert.Equal(0, m.Things.Count);
            Assert.Equal(0, m.Events.Count);
        }

        [Fact]
        public void InnateRecord_SurvivesAFullStoreUnderPressure_EndToEnd()
        {
            // Fill a small store; an INNATE record must never be evicted no matter how many
            // stronger writes pour in (the home-burrow guarantee).
            var bag = AtomBag.Create(new[] { new Atom(new AtomTypeId(1), Fixed.One) });
            var store = new MemoryStore(4);
            store.Encode(new MemoryRecord(new MemoryKey(0), CategoryId.None, bag, 10, 0, 0, MemoryFlags.Innate));
            for (long k = 1; k <= 50; k++)
                store.Encode(new MemoryRecord(new MemoryKey(k), CategoryId.None, bag, 200, k, k, MemoryFlags.None));

            Assert.Equal(4, store.Count);
            Assert.True(store.TryGet(new MemoryKey(0), out var innate));   // still there
            Assert.True(innate.IsInnate);
        }
    }
}
