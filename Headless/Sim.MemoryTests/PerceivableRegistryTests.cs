using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class PerceivableRegistryTests
    {
        static (EventBus, PerceivableRegistry) New()
        {
            var e = new EventBus();
            return (e, new PerceivableRegistry(e));
        }

        static void Tick(EventBus e, PerceivableRegistry r) { e.Tick(); r.Update(0); }

        [Fact]
        public void Stamp_AddsAtom_SortedBag()
        {
            var (e, r) = New();
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002), Value = Fixed.One });
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(1004), Value = Fixed.One });
            Tick(e, r);

            var bag = r.Bag(new EntityId(1));
            Assert.Equal(new[] { 1004, 4002 }, bag.Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void Restamp_ReplacesValue()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(4002), Fixed.One);
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002), Value = Fixed.FromDouble(0.5) });
            Tick(e, r);
            r.Bag(new EntityId(1)).TryGet(new AtomTypeId(4002), out var v);
            Assert.Equal(Fixed.FromDouble(0.5), v);
            Assert.Equal(1, r.Bag(new EntityId(1)).Count);
        }

        [Fact]
        public void Clear_RemovesAtom_AbsentIsNoop()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(4002), Fixed.One);
            r.Seed(new EntityId(1), new AtomTypeId(1004), Fixed.One);
            e.Publish(new ClearAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002) });
            e.Publish(new ClearAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(9999) }); // absent
            Tick(e, r);
            Assert.Equal(new[] { 1004 }, r.Bag(new EntityId(1)).Atoms.Select(a => a.Type.Value).ToArray());
        }

        [Fact]
        public void ClearBeforeStamp_SameType_StampWins()
        {
            // A re-stamp emits clear(old) + stamp(new) in one tick. Clears apply first, so the new value lands.
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(4002), Fixed.One);
            e.Publish(new ClearAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002) });
            e.Publish(new StampAtomIntent { Entity = new EntityId(1), Type = new AtomTypeId(4002), Value = Fixed.FromDouble(0.25) });
            Tick(e, r);
            r.Bag(new EntityId(1)).TryGet(new AtomTypeId(4002), out var v);
            Assert.Equal(Fixed.FromDouble(0.25), v);
        }

        [Fact]
        public void Despawn_DropsBag()
        {
            var (e, r) = New();
            r.Seed(new EntityId(1), new AtomTypeId(1004), Fixed.One);
            e.Publish(new DespawnedEvent { Entity = new EntityId(1) });
            Tick(e, r);
            Assert.Equal(0, r.Bag(new EntityId(1)).Count);
        }

        [Fact]
        public void Bag_UnknownEntity_IsEmpty()
        {
            var (_, r) = New();
            Assert.Same(AtomBag.Empty, r.Bag(new EntityId(99)));
        }
    }
}
