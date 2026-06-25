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
            // Place atoms are named identities with the right category — distinct, no magic bands.
            Assert.Equal(AtomName.PlaceTavern.ToId(), PlaceAtoms.Kind(BuildingKind.Tavern));
            Assert.Equal(AtomCategory.PlaceKind,       AtomCatalog.For(PlaceAtoms.Kind(BuildingKind.Tavern)).Category);
            Assert.Equal(AtomCategory.PlaceProvisions, AtomCatalog.For(PlaceAtoms.Provisions).Category);
            Assert.Equal(AtomCategory.PlaceDanger,     AtomCatalog.For(PlaceAtoms.Danger).Category);
            Assert.NotEqual(PlaceAtoms.Provisions.Value, PlaceAtoms.Danger.Value);
            Assert.NotEqual(PlaceAtoms.Kind(BuildingKind.Tavern).Value, PlaceAtoms.Provisions.Value);
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
            Assert.Equal(2, rec.DeltaBag.Count);
            Assert.True(rec.DeltaBag.Contains(PlaceAtoms.Kind(BuildingKind.Tavern)), "delta bag missing the tavern-kind atom");
            Assert.True(rec.DeltaBag.Contains(PlaceAtoms.Danger), "delta bag missing the danger atom");

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
