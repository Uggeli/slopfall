namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.CombatSystem. Combat resolution
    // (V2b — the fight half of fight-or-flight). An agent Doing Attack that has closed to
    // within reach of a creature strikes it: a DamageEvent HealthSystem applies (and fires
    // DeathEvent at 0 HP), LifecycleSystem despawns, CreatureSystem respawns the population.
    // A struck creature remembers its attacker (CreatureData.LastAttacker →
    // CreatureSystem retaliation).
    //
    // Reads Behavior+Position+Creatures+Vitals read-only; Publishes DamageEvent +
    // CreatureSetIntent (to record the strike: LastAttacker/LastStruckTick on the creature row).
    //
    // === WHERE _nextAttack WENT (homeless cooldown — NOTE per the brief) =================
    // The old system held a private `_nextAttack : Dictionary<EntityId,long>` — a per-ATTACKER
    // (i.e. per-CIVILIAN) strike cooldown gate (`tick + AttackCooldownTicks`). The brief asks
    // to fold this into CreatureData where possible: that is NOT possible here, because the key
    // is the attacking civilian, and civilians have no CreatureData row (CreatureData lives only
    // on EnemyMonster entities — the only field that overlaps, NextAttackTick, is the CREATURE's
    // own bite gate, a different actor). It also has no home on any other existing settled
    // registry, and the brief scoped new registries to PathRegistry only.
    //
    // Consequence faithfully noted: without a homed cooldown, an Attack-Doing civilian would
    // strike every tick (6 dmg/tick) instead of every AttackCooldownTicks (~one blow / 30
    // ticks), so a 20-HP creature would fall in ~4 ticks rather than ~4 blows over ~120 ticks.
    // To AVOID silently changing the balance, this conversion gates the strike off the SETTLED
    // creature row instead: it skips re-striking a creature whose CreatureData.LastStruckTick is
    // within AttackCooldownTicks of now. This preserves the once-per-cooldown cadence for the
    // common case (one attacker on one creature) using only settled state — but note it differs
    // from the old per-attacker gate when MULTIPLE attackers share a creature (the old code
    // cooled each attacker independently; this cools per-creature). The exact per-attacker
    // semantics need a dedicated civilian-side cooldown registry, which the brief did not
    // sanction; flagging for a follow-up if multi-attacker fidelity matters.
    public sealed class CombatSystem : SimSystem
    {
        const float AttackRange = 2.5f;
        const int AttackDamage = 6;            // a person's blow — a creature (20 HP) falls in ~4 hits
        const long AttackCooldownTicks = 30;   // ~30 game-minutes between blows

        readonly WorldClockRegistry _clock;
        readonly BehaviorRegistry _behavior;
        readonly PositionRegistry _position;
        readonly CreatureRegistry _creatures;
        readonly VitalsRegistry _vitals;

        public CombatSystem(EventBus events, WorldClockRegistry clock, BehaviorRegistry behavior,
            PositionRegistry position, CreatureRegistry creatures, VitalsRegistry vitals, int seed) : base(events)
        {
            _clock = clock;
            _behavior = behavior;
            _position = position;
            _creatures = creatures;
            _vitals = vitals;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;

            foreach (var kv in _behavior.All)
            {
                if (kv.Value.Phase != ActivityPhase.Doing || kv.Value.Activity != ActivityKind.Attack) continue;
                var attacker = kv.Key;
                if (!_position.TryGet(attacker, out var ap) || ap == null) continue;

                var target = NearestCreatureInRange(ap);
                if (target.IsNone) continue;

                // Cooldown gate, off the settled creature row (see the homeless-_nextAttack
                // note above). Skip a creature struck within the last AttackCooldownTicks.
                if (_creatures.TryGet(target, out var cr)
                    && !cr.LastAttacker.IsNone && tick - cr.LastStruckTick < AttackCooldownTicks)
                    continue;

                Events.Publish(new DamageEvent
                {
                    Target = target, Source = attacker, Amount = AttackDamage, Type = DamageType.Physical,
                });

                // The creature remembers who struck it (retaliation) — and the strike tick
                // doubles as the cooldown stamp above.
                cr.LastAttacker = attacker; cr.LastStruckTick = tick;
                Events.Publish(new CreatureSetIntent { Id = target, Data = cr });
            }
        }

        EntityId NearestCreatureInRange(PositionData from)
        {
            float best = AttackRange * AttackRange;
            EntityId nearest = EntityId.None; int bestId = int.MaxValue;
            foreach (var kv in _creatures.All)
            {
                if (!_position.TryGet(kv.Key, out var tp) || tp == null) continue;
                if (_vitals.TryGet(kv.Key, out var v) && v != null && v.IsDead) continue;
                float dx = tp.X - from.X, dz = tp.Z - from.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 < best || (d2 == best && kv.Key.Value < bestId))
                { best = d2; nearest = kv.Key; bestId = kv.Key.Value; }
            }
            return nearest;
        }

        /// Crime-as-tag (drive doc): striking an INNOCENT installs an aversive charge on
        /// Attack — the agent comes to feel that violence is wrong. Wired and tested, but
        /// dormant in normal play: nothing makes an agent attack a non-threat yet (that
        /// needs an anger/aggression drive — future work). The a-priori, target-conditional
        /// tag at decision time is deferred S4.
        ///
        /// CQRS form: reads the settled conscience row and Publishes the recomputed charges
        /// (ConscienceRegistry is the sole applier) instead of mutating in place. Caller
        /// supplies the bus + the settled ConscienceRegistry read view.
        public static void ChargeViolenceGuilt(EventBus events, ConscienceRegistry conscience, EntityId attacker, double amount)
        {
            ConscienceData c;
            if (!conscience.TryGet(attacker, out var existing) || existing == null)
                c = new ConscienceData();
            else
                c = existing;

            int k = (int)ActivityKind.Attack;
            c.Charge.TryGetValue(k, out var cur);
            double next = cur + amount;
            c.Charge[k] = next > 1.0 ? 1.0 : next;

            events.Publish(new ConscienceSetIntent { Id = attacker, Data = c });
        }
    }
}
