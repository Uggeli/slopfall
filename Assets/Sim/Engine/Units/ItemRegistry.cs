using System.Collections.Generic;
using System.Threading;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.ItemRegistry. Reuses the existing
    // ItemId / ItemData / ItemLocationKind from the parent namespace. Store is keyed
    // by ItemId (not EntityId), so it does NOT react to DespawnedEvent (entity death
    // doesn't key items). Item removal goes through ItemRemoveIntent. Allocate() keeps
    // the Interlocked id counter — it's a pure allocator (no store mutation), safe to
    // call from a system during the read phase. Derived queries (CarriedBy, InBuilding)
    // stay routed to the store.

    /// Intent: spawn-or-update a row. Last write wins per ItemId.
    public struct ItemSetIntent : IEvent { public ItemId Id; public ItemData Data; }

    /// Intent: remove a row.
    public struct ItemRemoveIntent : IEvent { public ItemId Id; }

    public sealed class ItemRegistry : Registry
    {
        readonly Dictionary<ItemId, ItemData> _d = new Dictionary<ItemId, ItemData>();
        int _nextId;

        public ItemRegistry(EventBus events) : base(events) { }

        /// Sequential allocation; deterministic given the single writer's ordered
        /// passes (same convention as the original). Never returns None. This is a
        /// pure id allocator — it does not touch the store, so a system may call it.
        public ItemId Allocate() => new ItemId(Interlocked.Increment(ref _nextId));

        public override void Update(long tick)
        {
            // Removes after sets so a same-tick set+remove resolves to removed.
            foreach (var s in Events.GetEvents<ItemSetIntent>())
                if (s.Data != null) _d[s.Id] = s.Data;
            foreach (var r in Events.GetEvents<ItemRemoveIntent>())
                _d.Remove(r.Id);
        }

        public bool TryGet(ItemId id, out ItemData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<ItemId, ItemData>> All => _d;

        /// What an agent physically holds (CarriedBy / EquippedBy), ItemId-sorted so
        /// callers iterate deterministically. Derived view — O(n) over the registry.
        public void CarriedBy(EntityId holder, List<ItemId> into)
        {
            into.Clear();
            foreach (var kv in _d)
            {
                var loc = kv.Value.LocationKind;
                if ((loc == ItemLocationKind.CarriedBy || loc == ItemLocationKind.EquippedBy)
                    && kv.Value.Holder == holder)
                    into.Add(kv.Key);
            }
            into.Sort((a, b) => a.Value.CompareTo(b.Value));
        }

        /// What rests in a building (InBuilding), ItemId-sorted. Derived view.
        public void InBuilding(int building, List<ItemId> into)
        {
            into.Clear();
            foreach (var kv in _d)
                if (kv.Value.LocationKind == ItemLocationKind.InBuilding && kv.Value.Building == building)
                    into.Add(kv.Key);
            into.Sort((a, b) => a.Value.CompareTo(b.Value));
        }
    }
}
