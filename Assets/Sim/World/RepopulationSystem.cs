using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The entry half of turnover (L2.4 — docs/living_world_L2_lifecycle.md).
    /// Backfills each vacated residency slot with a broke adult immigrant, so a
    /// settlement's headcount stays roughly stationary and a dead keeper's shop
    /// is re-staffed. 1:1 replacement is the MVP; reproduction (kits that age to
    /// adulthood) is the richer future model.
    ///
    /// Deterministic: vacancies are processed in a stable order and the spawn
    /// draws from the shared RNG in that order, so replays are exact.
    public sealed class RepopulationSystem : ISystem
    {
        SimulationContext _ctx;
        readonly List<DespawnedEvent> _vacancies = new List<DespawnedEvent>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<DespawnedEvent>(e =>
            {
                if (e.Settlement >= 0 && e.Building >= 0) _vacancies.Add(e);
            });
        }

        public void ProcessEvents() { }

        public void Update(long tick)
        {
            if (_vacancies.Count == 0) return;
            _vacancies.Sort(Order);
            for (int i = 0; i < _vacancies.Count; i++)
            {
                var v = _vacancies[i];
                if (v.Settlement < 0 || v.Settlement >= _ctx.Settlements.Count) continue;
                TownLoader.SpawnImmigrant(_ctx, _ctx.Settlements.Get(v.Settlement), v.Building, v.Role);
            }
            _vacancies.Clear();
        }

        static int Order(DespawnedEvent a, DespawnedEvent b)
        {
            int c = a.Settlement.CompareTo(b.Settlement);
            if (c != 0) return c;
            c = a.Building.CompareTo(b.Building);
            return c != 0 ? c : a.Entity.Value.CompareTo(b.Entity.Value);
        }
    }
}
