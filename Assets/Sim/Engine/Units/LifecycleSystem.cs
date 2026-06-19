using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.LifecycleSystem (L2 — the exit
    // pass). It consumes DeathEvent off the bus and emits, per dead entity:
    //   - DespawnedEvent (carrying the vacated Settlement + Building ids), which EVERY
    //     per-entity registry applies as its own removal (so this system no longer
    //     touches the 25-registry checklist by hand — each registry self-cleans on
    //     DespawnedEvent), and which RepopulationSystem reads to backfill the slot;
    //   - EscheatEvent for civilians whose purse must flow to the settlement treasury
    //     (EconomySystem applies the coin move; CoinRegistry zeroes the dead purse on
    //     DespawnedEvent), keeping the money supply conserved.
    //
    // Reads Identity / Residency / Settlement read-only; finds the dead's settlement by
    // scanning rosters (read-only — SettlementRegistry removes the entity from the
    // roster itself on DespawnedEvent). The old _pending/_seen spool+dedup becomes a
    // LOCAL: DeathEvents are read from the bus this tick, deduped in a local HashSet,
    // and processed in id order for a deterministic despawn sequence.
    public sealed class LifecycleSystem : SimSystem
    {
        readonly IdentityRegistry _identity;
        readonly ResidencyRegistry _residency;
        readonly CreatureRegistry _creatures;
        readonly SettlementRegistry _settlements;

        public LifecycleSystem(EventBus events, IdentityRegistry identity, ResidencyRegistry residency,
                               CreatureRegistry creatures, SettlementRegistry settlements) : base(events)
        {
            _identity = identity;
            _residency = residency;
            _creatures = creatures;
            _settlements = settlements;
        }

        public override void Update(long tick)
        {
            var deaths = Events.GetEvents<DeathEvent>();
            if (deaths.Length == 0) return;

            // Within-tick dedup → LOCAL, collected in id order for a deterministic
            // despawn sequence (transfers/escheat couple entities).
            var seen = new HashSet<EntityId>();
            var pending = new List<EntityId>();
            foreach (ref readonly var d in deaths)
                if (seen.Add(d.Entity)) pending.Add(d.Entity);
            pending.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int i = 0; i < pending.Count; i++)
                Despawn(pending[i]);
        }

        void Despawn(EntityId id)
        {
            // Creatures (V2) carry no civilian state and no residency/settlement — emit a
            // plain despawn; their handful of registries (Creature/Identity/Position/
            // Vitals) drop the id on DespawnedEvent.
            if (_creatures.Contains(id))
            {
                Events.Publish(new DespawnedEvent { Entity = id, Settlement = -1, Building = -1 });
                return;
            }

            // Only civilians despawn — never the player or quest-critical NPCs.
            if (_identity.TryGet(id, out var idn) && idn != null && idn.Kind != EntityKind.CivilianNPC)
                return;

            // Capture the vacated residency slot so RepopulationSystem can backfill the
            // same building — L2.4. (Role isn't carried on DespawnedEvent; RepopulationSystem
            // infers it from whether the building still has a live keeper.)
            int building = -1;
            if (_residency.TryGet(id, out var res) && res != null) building = res.BuildingIndex;

            // Find the settlement this civilian belongs to (read-only roster scan — the
            // SettlementRegistry removes them from the roster itself on DespawnedEvent).
            int settlementId = FindSettlement(id);

            // Escheat the purse to the settlement treasury (EconomySystem applies the
            // coin move so the money supply stays conserved). No settlement (e.g. a test
            // entity) → no escheat; CoinRegistry drops the purse on DespawnedEvent.
            if (settlementId >= 0)
                Events.Publish(new EscheatEvent { Dead = id });

            // One DespawnedEvent fans out to every per-entity registry, which each remove
            // the id (the old by-hand 25-registry checklist is gone).
            Events.Publish(new DespawnedEvent { Entity = id, Settlement = settlementId, Building = building });
        }

        int FindSettlement(EntityId id)
        {
            var all = _settlements.All;
            for (int s = 0; s < all.Count; s++)
            {
                var residents = all[s].Residents;
                for (int i = 0; i < residents.Count; i++)
                    if (residents[i] == id) return all[s].Id;
            }
            return -1;
        }
    }
}
