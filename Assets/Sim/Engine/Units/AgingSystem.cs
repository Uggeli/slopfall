using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.AgingSystem (L2 lifecycle —
    // docs/living_world_L2_lifecycle.md). Once per game-year every agent ages and rolls
    // against a mortality hazard that climbs past its lifespan. A fatal roll routes
    // through the SAME death path as combat — a lethal DamageEvent(Age) → HealthSystem
    // fires the one DeathEvent → LifecycleSystem despawns — so nothing downstream cares
    // how an agent died.
    //
    // Cadence: the old _lastYear delta-tracking is replaced by reacting to NewYearEvent.
    // Each NewYearEvent is exactly one year boundary, so every agent ages by 1 — the old
    // "first observed year, establish baseline, don't age" guard existed only to suppress
    // a spurious age on the very first Update before _lastYear was seeded; an event only
    // fires on a real transition, so that case no longer arises.
    //
    // Mortality is a deterministic per-(agent, year) hash draw (keyed by the new year +
    // entity id + ctor seed), never a shared RNG stream, so replays are exact (A5). The
    // hazard shape and lifespan distribution are FROZEN placeholders (living_world.md ->
    // Tuning).
    public sealed class AgingSystem : SimSystem
    {
        const int AgeFatalDamage = 1000000;   // dwarfs any civilian MaxHealth → guaranteed fatal hit

        readonly LifeRegistry _life;
        readonly VitalsRegistry _vitals;
        readonly int _seed;
        readonly List<EntityId> _keys = new List<EntityId>();

        public AgingSystem(EventBus events, LifeRegistry life, VitalsRegistry vitals, int seed) : base(events)
        {
            _life = life;
            _vitals = vitals;
            _seed = seed;
        }

        public override void Update(long tick)
        {
            var years = Events.GetEvents<NewYearEvent>();
            if (years.Length == 0) return;                 // age only on a year boundary

            // Apply each year boundary observed this tick (normally exactly one). Each is
            // a single-year step, matching the old per-year cadence.
            for (int i = 0; i < years.Length; i++)
                AgeAndCull(1, years[i].Year);
        }

        void AgeAndCull(int years, int newYear)
        {
            _keys.Clear();
            foreach (var kv in _life.All) _keys.Add(kv.Key);
            _keys.Sort((a, b) => a.Value.CompareTo(b.Value));      // key order → determinism

            for (int i = 0; i < _keys.Count; i++)
            {
                var id = _keys[i];
                if (!_life.TryGet(id, out var life) || life == null) continue;
                if (_vitals.TryGet(id, out var v) && v != null && v.IsDead) continue;  // already dying

                double newAge = life.AgeYears + years;
                Events.Publish(new LifeSetIntent
                {
                    Id = id,
                    AgeYears = newAge,
                    LifespanYears = life.LifespanYears,
                });

                double hazard = MortalityHazard(newAge, life.LifespanYears);
                if (hazard <= 0) continue;
                double roll = Hash(_seed, id.Value, newYear) / (double)uint.MaxValue;
                if (roll < hazard)
                    Events.Publish(new DamageEvent
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

        // Stateless per-(agent, year) draw folded with the ctor seed (WeatherSystem.Roll
        // shape). Replaces the old Hash(id.Value, tick): keyed by the deterministic year
        // boundary, not a wall-clock tick, so replays are exact.
        static uint Hash(int seed, int idValue, int year)
        {
            unchecked
            {
                uint x = (uint)(idValue * 2654435761u) ^ (uint)(seed * 2246822519u) ^ (uint)(year * 40503u);
                x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
                return x;
            }
        }
    }
}
