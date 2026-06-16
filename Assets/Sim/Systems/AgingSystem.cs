using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The age half of entry→exit (L2 — docs/living_world_L2_lifecycle.md). Once
    /// per game-year every agent ages and rolls against a mortality hazard that
    /// climbs past its lifespan. A fatal roll routes through the SAME death path
    /// as combat — a lethal DamageEvent(Age) → HealthSystem fires the one
    /// DeathSimEvent → LifecycleSystem despawns — so nothing downstream cares how
    /// an agent died.
    ///
    /// Mortality is a deterministic per-(agent, year) hash draw, never a shared
    /// RNG stream, so replays are exact (A5). The hazard shape and the lifespan
    /// distribution are FROZEN placeholders until the whole L-stack lands
    /// (living_world.md -> Tuning).
    public sealed class AgingSystem : ISystem
    {
        const int AgeFatalDamage = 1000000;   // dwarfs any civilian MaxHealth → guaranteed fatal hit

        SimulationContext _ctx;
        int _lastYear = -1;
        readonly List<EntityId> _keys = new List<EntityId>();

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;                 // clock not seeded yet
            if (_lastYear < 0) { _lastYear = clock.Year; return; }   // first observed year — establish baseline, don't age
            if (clock.Year <= _lastYear) return;
            int years = clock.Year - _lastYear;
            _lastYear = clock.Year;
            AgeAndCull(years, tick);
        }

        void AgeAndCull(int years, long tick)
        {
            _keys.Clear();
            foreach (var kv in _ctx.Life.All) _keys.Add(kv.Key);
            _keys.Sort((a, b) => a.Value.CompareTo(b.Value));      // key order → determinism

            for (int i = 0; i < _keys.Count; i++)
            {
                var id = _keys[i];
                if (!_ctx.Life.TryGet(id, out var life) || life == null) continue;
                if (_ctx.Vitals.TryGet(id, out var v) && v != null && v.IsDead) continue;  // already dying

                life.AgeYears += years;
                _ctx.Life.Set(id, life);

                double hazard = MortalityHazard(life.AgeYears, life.LifespanYears);
                if (hazard <= 0) continue;
                double roll = Hash(id.Value, tick) / (double)uint.MaxValue;
                if (roll < hazard)
                    _ctx.Events.Emit(new DamageEvent
                    {
                        Target = id, Source = EntityId.None,
                        Amount = AgeFatalDamage, Type = DamageType.Age,
                    });
            }
        }

        /// Annual probability of dying of age: negligible when young, climbing
        /// steeply past the expected lifespan, certain well beyond it. Placeholder
        /// shape (a crude Gompertz-like ramp) — not tuned.
        public static double MortalityHazard(double age, double lifespan)
        {
            if (lifespan <= 0) return 1.0;
            double ratio = age / lifespan;
            if (ratio < 0.5) return 0.001;                 // rare accidental mortality when young
            double h = 0.002 * System.Math.Pow(ratio / 0.5, 6.0);
            return h > 1.0 ? 1.0 : h;
        }

        static uint Hash(int idValue, long tick)
        {
            uint x = (uint)(idValue * 2654435761u) ^ (uint)(tick * 40503u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }
    }
}
