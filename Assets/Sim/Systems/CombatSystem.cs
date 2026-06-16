using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Combat resolution (V2b — the fight half of fight-or-flight). An agent Doing
    /// Attack that has closed to within reach of a creature strikes it: a
    /// DamageEvent the existing HealthSystem applies (and fires DeathSimEvent at 0
    /// HP), LifecycleSystem despawns, and CreatureSystem respawns the population —
    /// so creatures genuinely fight to the death. A struck creature remembers its
    /// attacker (CreatureData.LastAttacker → CreatureSystem retaliation). Guards
    /// hunt and the bold fight; the timid flee — OddSystem scores which.
    ///
    /// All constants are FROZEN placeholders, tuned with the rest.
    public sealed class CombatSystem : ISystem
    {
        const float AttackRange = 2.5f;
        const int AttackDamage = 6;            // a person's blow — a creature (20 HP) falls in ~4 hits
        const long AttackCooldownTicks = 30;   // ~30 game-minutes between blows

        SimulationContext _ctx;
        readonly Dictionary<EntityId, long> _nextAttack = new Dictionary<EntityId, long>();

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;

            foreach (var kv in _ctx.Behavior.All)
            {
                if (kv.Value.Phase != ActivityPhase.Doing || kv.Value.Activity != ActivityKind.Attack) continue;
                var attacker = kv.Key;
                if (_nextAttack.TryGetValue(attacker, out var ready) && tick < ready) continue;
                if (!_ctx.Position.TryGet(attacker, out var ap) || ap == null) continue;

                var target = NearestCreatureInRange(ap);
                if (target.IsNone) continue;

                _ctx.Events.Emit(new DamageEvent
                {
                    Target = target, Source = attacker, Amount = AttackDamage, Type = DamageType.Physical,
                });
                _nextAttack[attacker] = tick + AttackCooldownTicks;

                // The creature remembers who struck it (retaliation).
                if (_ctx.Creatures.TryGet(target, out var cr))
                {
                    cr.LastAttacker = attacker; cr.LastStruckTick = tick;
                    _ctx.Creatures.Set(target, cr);
                }
            }
        }

        EntityId NearestCreatureInRange(PositionData from)
        {
            float best = AttackRange * AttackRange;
            EntityId nearest = EntityId.None; int bestId = int.MaxValue;
            foreach (var kv in _ctx.Creatures.All)
            {
                if (!_ctx.Position.TryGet(kv.Key, out var tp) || tp == null) continue;
                if (_ctx.Vitals.TryGet(kv.Key, out var v) && v != null && v.IsDead) continue;
                float dx = tp.X - from.X, dz = tp.Z - from.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 < best || (d2 == best && kv.Key.Value < bestId))
                { best = d2; nearest = kv.Key; bestId = kv.Key.Value; }
            }
            return nearest;
        }

        /// Crime-as-tag (drive doc): striking an INNOCENT installs an aversive
        /// charge on Attack — the agent comes to feel that violence is wrong. Wired
        /// and tested, but dormant in normal play: nothing makes an agent attack a
        /// non-threat yet (that needs an anger/aggression drive — future work). The
        /// a-priori, target-conditional tag at decision time is deferred S4.
        public static void ChargeViolenceGuilt(SimulationContext ctx, EntityId attacker, double amount)
        {
            if (!ctx.Conscience.TryGet(attacker, out var c) || c == null)
            {
                c = new ConscienceData();
                ctx.Conscience.Set(attacker, c);
            }
            int k = (int)ActivityKind.Attack;
            c.Charge.TryGetValue(k, out var cur);
            double next = cur + amount;
            c.Charge[k] = next > 1.0 ? 1.0 : next;
        }
    }
}
