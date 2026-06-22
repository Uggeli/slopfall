using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PlaceMemoryWriteTests
    {
        static (EventBus, AgentMemoryRegistry) New()
        { var e = new EventBus(); return (e, new AgentMemoryRegistry(e, AgentMemoryConfig.Default)); }

        [Fact]
        public void PlaceAtoms_RangesDistinct()
        {
            Assert.Equal(5000 + (int)BuildingKind.Tavern, PlaceAtoms.Kind(BuildingKind.Tavern).Value);
            Assert.Equal(6000, PlaceAtoms.Provisions.Value);
            Assert.Equal(6001, PlaceAtoms.Danger.Value);
            Assert.True(PlaceAtoms.Kind(BuildingKind.Tavern).Value >= 5000);   // above the activity range
        }

        [Fact]
        public void Observe_AccumulatesAtoms_OnBuildingRecord_ValueWins()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1));
            e.Publish(new PlaceObserveIntent { Agent = new EntityId(1), Building = 5, Atom = PlaceAtoms.Kind(BuildingKind.Tavern), Value = Fixed.One });
            e.Publish(new PlaceObserveIntent { Agent = new EntityId(1), Building = 5, Atom = PlaceAtoms.Danger, Value = Fixed.FromDouble(0.5) });
            e.Tick(); r.Update(0);

            r.TryGet(new EntityId(1), out var mem);
            Assert.True(mem.Stores.Places.TryGet(new MemoryKey(5), out var rec));
            Assert.Equal(new[] { 5000 + (int)BuildingKind.Tavern, 6001 }, rec.DeltaBag.Atoms.Select(a => a.Type.Value).OrderBy(x => x).ToArray());

            // re-observe danger with a new value -> value wins
            e.Publish(new PlaceObserveIntent { Agent = new EntityId(1), Building = 5, Atom = PlaceAtoms.Danger, Value = Fixed.FromDouble(0.9) });
            e.Tick(); r.Update(0);
            r.TryGet(new EntityId(1), out mem);
            mem.Stores.Places.TryGet(new MemoryKey(5), out rec);
            rec.DeltaBag.TryGet(PlaceAtoms.Danger, out var d);
            Assert.Equal(Fixed.FromDouble(0.9), d);
        }

        [Fact]
        public void SeedPlace_WritesDirectly()
        {
            var (_, r) = New();
            r.Seed(new EntityId(1));
            r.SeedPlace(new EntityId(1), 7, PlaceAtoms.Kind(BuildingKind.GeneralStore), Fixed.One);
            r.TryGet(new EntityId(1), out var mem);
            Assert.True(mem.Stores.Places.TryGet(new MemoryKey(7), out var rec));
            Assert.True(rec.DeltaBag.Contains(PlaceAtoms.Kind(BuildingKind.GeneralStore)));
        }
    }
}
