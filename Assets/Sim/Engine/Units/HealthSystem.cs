using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.HealthSystem. The health MATH
    // lives here (the old system did the same): consume DamageEvent / HealEvent, read
    // the settled VitalsData, compute the new health, and Publish a VitalsSetIntent.
    // On a fatal hit, also Publish a DeathEvent. VitalsRegistry is the sole applier of
    // VitalsSetIntent — it does no health math.
    //
    // Old (subscribe/spool) → new (GetEvents) note on multi-hit-in-a-tick semantics:
    // the old system applied each DamageEvent/HealEvent sequentially, so a second hit
    // saw the first's reduced health. Under double-buffered events every intent reads
    // the SAME settled VitalsData for this tick, so to preserve that cumulative,
    // order-sensitive behavior EXACTLY we fold all of this tick's hits per entity in a
    // single pass (a local working copy), then emit one VitalsSetIntent per touched
    // entity. Death still fires the first time an entity's running health crosses 0.
    public sealed class HealthSystem : SimSystem
    {
        readonly VitalsRegistry _vitals;

        public HealthSystem(EventBus events, VitalsRegistry vitals) : base(events)
        {
            _vitals = vitals;
        }

        public override void Update(long tick)
        {
            var damage = Events.GetEvents<DamageEvent>();
            var heal = Events.GetEvents<HealEvent>();
            if (damage.Length == 0 && heal.Length == 0) return;

            // Per-entity working copy so sequential hits accumulate (matches the old
            // apply-in-order semantics) and one combined intent lands per entity.
            var work = new Dictionary<EntityId, VitalsData>();
            var fatalOf = new Dictionary<EntityId, DamageEvent>();   // first fatal hit per entity

            // Damage first, then heal — same relative ordering the old subscriptions
            // produced (Damage and Heal were independent callbacks; within a kind, in
            // emission order).
            for (int i = 0; i < damage.Length; i++)
            {
                var e = damage[i];
                if (e.Amount <= 0) continue;
                if (!TryWorking(work, e.Target, out var v) || v == null) continue;
                if (v.IsDead) continue;

                int newHealth = v.CurrentHealth - e.Amount;
                bool fatal = newHealth <= 0;
                if (fatal) newHealth = 0;

                work[e.Target] = new VitalsData
                {
                    CurrentHealth   = newHealth,
                    MaxHealth       = v.MaxHealth,
                    CurrentMagicka  = v.CurrentMagicka,
                    MaxMagicka      = v.MaxMagicka,
                    CurrentFatigue  = v.CurrentFatigue,
                    MaxFatigue      = v.MaxFatigue,
                    CurrentBreath   = v.CurrentBreath,
                    MaxBreath       = v.MaxBreath,
                    IsDead          = fatal || v.IsDead,
                };

                if (fatal && !v.IsDead && !fatalOf.ContainsKey(e.Target))
                    fatalOf[e.Target] = e;
            }

            for (int i = 0; i < heal.Length; i++)
            {
                var e = heal[i];
                if (e.Amount <= 0) continue;
                if (!TryWorking(work, e.Target, out var v) || v == null) continue;
                if (v.IsDead) continue;

                int newHealth = v.CurrentHealth + e.Amount;
                if (newHealth > v.MaxHealth) newHealth = v.MaxHealth;

                work[e.Target] = new VitalsData
                {
                    CurrentHealth   = newHealth,
                    MaxHealth       = v.MaxHealth,
                    CurrentMagicka  = v.CurrentMagicka,
                    MaxMagicka      = v.MaxMagicka,
                    CurrentFatigue  = v.CurrentFatigue,
                    MaxFatigue      = v.MaxFatigue,
                    CurrentBreath   = v.CurrentBreath,
                    MaxBreath       = v.MaxBreath,
                    IsDead          = false,
                };
            }

            foreach (var kv in work)
                Events.Publish(new VitalsSetIntent { Id = kv.Key, Data = kv.Value });

            foreach (var kv in fatalOf)
            {
                var e = kv.Value;
                Events.Publish(new DeathEvent { Entity = e.Target, Killer = e.Source, FatalDamageType = e.Type });
            }
        }

        // Returns the working copy for id, seeding it from the settled registry value
        // on first touch. False (with null) if the entity has no vitals row.
        bool TryWorking(Dictionary<EntityId, VitalsData> work, EntityId id, out VitalsData v)
        {
            if (work.TryGetValue(id, out v)) return v != null;
            if (!_vitals.TryGet(id, out var stored) || stored == null) { v = null; return false; }
            v = stored;
            return true;
        }
    }
}
