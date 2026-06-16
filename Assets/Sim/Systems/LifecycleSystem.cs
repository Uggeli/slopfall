using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The exit pass (L2 — docs/living_world_L2_lifecycle.md). Collects
    /// DeathSimEvents during the tick and, at the end of its Update (it is
    /// registered LAST, so every other system this tick has already run against
    /// a still-live entity), frees the dead from every per-entity registry.
    ///
    /// Monotonic EntityIds (never reused) mean any reference left elsewhere
    /// "derefs as gone" (Atoms A2), so there is no inbound scrub — L1's relation
    /// decay GCs cold edges and read sites already TryGet-guard. Coin is handled
    /// by EconomySystem via EscheatEvent (purse → settlement treasury) so the
    /// money supply is conserved across death.
    public sealed class LifecycleSystem : ISystem
    {
        SimulationContext _ctx;
        readonly List<EntityId> _pending = new List<EntityId>();
        readonly HashSet<EntityId> _seen = new HashSet<EntityId>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<DeathSimEvent>(e =>
            {
                if (_seen.Add(e.Entity)) _pending.Add(e.Entity);
            });
        }

        public void ProcessEvents() { }

        public void Update(long tick)
        {
            if (_pending.Count == 0) return;
            _pending.Sort((a, b) => a.Value.CompareTo(b.Value));   // deterministic despawn order
            for (int i = 0; i < _pending.Count; i++)
                Despawn(_pending[i]);
            _pending.Clear();
            _seen.Clear();
        }

        void Despawn(EntityId id)
        {
            // Only civilians despawn — never the player or quest-critical NPCs.
            if (_ctx.Identity.TryGet(id, out var idn) && idn != null && idn.Kind != EntityKind.CivilianNPC)
                return;

            // Capture the vacated residency slot BEFORE removing it, so
            // RepopulationSystem can backfill the same (building, role) — L2.4.
            int building = -1;
            var role = ResidentRole.Resident;
            if (_ctx.Residency.TryGet(id, out var res) && res != null) { building = res.BuildingIndex; role = res.Role; }

            // Escheat the purse to the settlement treasury and detach from the
            // roster. EconomySystem applies the coin move (sole writer of Coin +
            // Treasury) so the money supply stays conserved.
            int settlementId;
            if (RemoveFromSettlement(id, out settlementId, out var treasury))
                _ctx.Events.Emit(new EscheatEvent { Dead = id, To = treasury });
            else
                _ctx.Coin.Remove(id);   // no settlement (e.g. a test entity) — just drop any purse

            // Free every per-entity registry (the SimulationContext checklist).
            // Globals (clock/weather/buildings/grid/stock/treasury/settlements/
            // ledger/market/lighting) are not entity-keyed; Occupancy + Sensed
            // are rebuilt every tick, so they self-clean; Coin is removed by
            // EconomySystem once the escheat settles.
            _ctx.Identity.Remove(id);
            _ctx.Position.Remove(id);
            _ctx.Vitals.Remove(id);
            _ctx.Effects.Remove(id);
            _ctx.EffectAggregate.Remove(id);
            _ctx.StatusFlags.Remove(id);
            _ctx.Stats.Remove(id);
            _ctx.Progression.Remove(id);
            _ctx.Residency.Remove(id);
            _ctx.Needs.Remove(id);
            _ctx.Behavior.Remove(id);
            _ctx.Relations.Remove(id);
            _ctx.Memory.Remove(id);
            _ctx.Personality.Remove(id);
            _ctx.PlaceMemory.Remove(id);
            _ctx.Intent.Remove(id);
            _ctx.Subjective.Remove(id);
            _ctx.Affects.Remove(id);
            _ctx.Employment.Remove(id);
            _ctx.Life.Remove(id);

            _ctx.Events.Emit(new DespawnedEvent { Entity = id, Settlement = settlementId, Building = building, Role = role });
        }

        bool RemoveFromSettlement(EntityId id, out int settlementId, out OwnerId treasury)
        {
            var all = _ctx.Settlements.All;
            for (int s = 0; s < all.Count; s++)
            {
                var residents = all[s].Residents;
                for (int i = residents.Count - 1; i >= 0; i--)
                {
                    if (residents[i] == id)
                    {
                        residents.RemoveAt(i);
                        settlementId = all[s].Id;
                        treasury = all[s].Treasury;
                        return true;
                    }
                }
            }
            settlementId = -1;
            treasury = default;
            return false;
        }
    }
}
