using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Per-entity fractional take progress (the old ItemSystem._takeProgress): whole
    /// provisions individuate into loaves; the sub-unit remainder carries across ticks.
    public struct TakeProgressSetIntent : IEvent { public EntityId Id; public double Progress; }

    public sealed class ItemTakeRegistry : Registry
    {
        readonly Dictionary<EntityId, double> _d = new Dictionary<EntityId, double>();
        public ItemTakeRegistry(EventBus events) : base(events) { }
        public double Get(EntityId id) => _d.TryGetValue(id, out var v) ? v : 0;

        public override void Update(long tick)
        {
            foreach (ref readonly var s in Events.GetEvents<TakeProgressSetIntent>()) _d[s.Id] = s.Progress;
            foreach (ref readonly var d in Events.GetEvents<DespawnedEvent>()) _d.Remove(d.Entity);
        }
    }

    /// Sole emitter of item intents: turns ProvisionsTakenEvent into discrete carried
    /// loaves (charging theft guilt), and applies UseItem/StoreItem item-blocks. Reads
    /// Behavior + Items + Conscience + take-progress; writes via ItemSetIntent /
    /// ItemRemoveIntent / ConscienceSetIntent / TakeProgressSetIntent.
    public sealed class ItemSystem : SimSystem
    {
        readonly ItemRegistry _items;
        readonly BehaviorRegistry _behavior;
        readonly ConscienceRegistry _conscience;
        readonly ItemTakeRegistry _take;
        readonly WorldClockRegistry _clock;

        const double TheftGuilt = 0.15;

        public ItemSystem(EventBus events, ItemRegistry items, BehaviorRegistry behavior,
            ConscienceRegistry conscience, ItemTakeRegistry take, WorldClockRegistry clock) : base(events)
        {
            _items = items; _behavior = behavior; _conscience = conscience; _take = take; _clock = clock;
        }

        public override void Update(long tick)
        {
            // --- individuate whole units from this tick's takes (was ProcessEvents) ---
            var takes = Events.GetEvents<ProvisionsTakenEvent>();
            if (takes.Length > 0)
            {
                // Sum per taker (deterministic: events arrive id-grouped enough; accrual is additive).
                var accrued = new Dictionary<EntityId, double>();
                var owners = new Dictionary<EntityId, EntityId>();
                for (int i = 0; i < takes.Length; i++)
                {
                    var e = takes[i];
                    accrued.TryGetValue(e.Taker, out var u); accrued[e.Taker] = u + e.Units;
                    owners[e.Taker] = e.Owner;   // last owner this tick (matches old per-event individuation owner)
                }
                foreach (var kv in accrued)
                {
                    double prog = _take.Get(kv.Key) + kv.Value;
                    while (prog >= 1.0) { prog -= 1.0; Individuate(kv.Key, owners[kv.Key]); }
                    Events.Publish(new TakeProgressSetIntent { Id = kv.Key, Progress = prog });
                }
            }

            // --- apply item-block effects (was Update): eat consumes, store relocates ---
            foreach (var bkv in _behavior.All)
            {
                var b = bkv.Value;
                if (b == null || b.Phase != ActivityPhase.Doing) continue;
                if (b.Activity != ActivityKind.UseItem && b.Activity != ActivityKind.StoreItem) continue;
                if (b.TargetItem.IsNone || !_items.TryGet(b.TargetItem, out var item) || item == null) continue;

                if (b.Activity == ActivityKind.UseItem)
                    Events.Publish(new ItemRemoveIntent { Id = b.TargetItem });   // eaten; self-guards (gone next tick)
                else
                {
                    var moved = CopyItem(item);
                    moved.Holder = EntityId.None;
                    moved.LocationKind = ItemLocationKind.InBuilding;
                    moved.Building = b.TargetBuilding; moved.X = 0; moved.Z = 0;
                    Events.Publish(new ItemSetIntent { Id = b.TargetItem, Data = moved });   // idempotent while Doing
                }
            }
        }

        void Individuate(EntityId taker, EntityId owner)
        {
            var loaf = GoodsCatalog.NewItem(Good.Provisions);
            loaf.Owner = owner;
            loaf.OriginOwner = owner;
            loaf.OriginTick = _clock.Current.Year == 0 ? 0 : (long)0;   // origin tick unused for fungible loaves
            bool theft = !owner.IsNone && owner != taker;
            loaf.LocationKind = ItemLocationKind.CarriedBy;
            loaf.Holder = taker; loaf.Building = -1; loaf.X = 0; loaf.Z = 0;
            Events.Publish(new ItemSetIntent { Id = _items.Allocate(), Data = loaf });
            if (theft) ChargeGuilt(taker, ActivityKind.Steal, TheftGuilt);
        }

        void ChargeGuilt(EntityId criminal, ActivityKind crime, double amount)
        {
            _conscience.TryGet(criminal, out var c);
            var charge = c != null ? new Dictionary<int, double>(c.Charge) : new Dictionary<int, double>();
            int k = (int)crime;
            charge.TryGetValue(k, out var cur);
            double next = cur + amount;
            charge[k] = next > 1.0 ? 1.0 : next;
            Events.Publish(new ConscienceSetIntent { Id = criminal, Data = new ConscienceData { Charge = charge } });
        }

        static ItemData CopyItem(ItemData s) => new ItemData
        {
            Name = s.Name, OriginOwner = s.OriginOwner, OriginTick = s.OriginTick,
            Edible = s.Edible, Drinkable = s.Drinkable, Wearable = s.Wearable, Valuable = s.Valuable,
            Weight = s.Weight, Owner = s.Owner, LocationKind = s.LocationKind, Holder = s.Holder,
            Building = s.Building, X = s.X, Z = s.Z, EquipSlot = s.EquipSlot,
        };
    }
}
