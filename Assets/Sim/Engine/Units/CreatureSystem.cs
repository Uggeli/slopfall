using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.CreatureSystem. The threat layer
    // (V2a): maintains a small population of mobile hostile creatures at the edge of
    // habitation. It spawns them, wanders them (it is the sole writer of creature Position —
    // they don't use the civilian ODD/Movement path), and emits DamageEvent when one
    // reaches a civilian (the combat emitter side; HealthSystem applies it and fires the
    // DeathEvent, LifecycleSystem despawns).
    //
    // Where state went: none was homeless. The attack-cooldown gate already lives in
    // CreatureData.NextAttackTick, and the retaliation grudge in LastAttacker/LastStruckTick —
    // all carried on the creature row, so the conversion holds no instance tick state. RNG
    // stays the existing stateless Hash(idValue, tick) (reused verbatim). Spawns still draw an
    // id from Identity.Allocate (an allocation, not tick-buffered data) and then Publish the
    // Identity/Position/Vitals/Creature rows as intents instead of direct Sets.
    //
    // Reads Position+Creatures+Identity+Vitals+WorldClock read-only; Publishes CreatureSetIntent
    // + PositionSetIntent + DamageEvent (+ IdentitySetIntent/VitalsSetIntent on spawn).
    //
    // All constants are FROZEN placeholders — deliberately gentle (few creatures, light bite).
    public sealed class CreatureSystem : SimSystem
    {
        const int TargetPopulation = 3;        // how many roam at once
        const float WalkSpeed = 1.1f;          // metres / game-second (a touch slower than a person's 1.4)
        const float WanderRadius = 25f;        // repick a destination within this of the current spot
        const float ArriveDistance = 1.5f;
        const float SpawnRadius = 40f;         // out past the edge of where people are
        const float AttackRange = 2.0f;
        const int AttackDamage = 1;            // light bite (placeholder)
        const long AttackCooldownTicks = 240;  // ~4 game-hours between bites at 1440 ticks/day
        const long RetaliationWindowTicks = 120;   // a creature holds its grudge this long

        readonly WorldClockRegistry _clock;
        readonly PositionRegistry _position;
        readonly CreatureRegistry _creatures;
        readonly IdentityRegistry _identity;
        readonly VitalsRegistry _vitals;
        readonly int _seed;

        public CreatureSystem(EventBus events, WorldClockRegistry clock, PositionRegistry position,
            CreatureRegistry creatures, IdentityRegistry identity, VitalsRegistry vitals, int seed) : base(events)
        {
            _clock = clock;
            _position = position;
            _creatures = creatures;
            _identity = identity;
            _vitals = vitals;
            _seed = seed;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;       // town not loaded yet

            // Keep the population topped up. Under CQRS the spawn lands next tick (intent),
            // so we can't observe Creatures.Count rising mid-loop; track how many we've
            // queued this tick locally so we don't over-spawn before the registry applies.
            int spawnedThisTick = 0;
            while (_creatures.Count + spawnedThisTick < TargetPopulation && TrySpawn(tick))
                spawnedThisTick++;

            double gameSeconds = clock.DeltaGameSeconds;
            float step = (float)(WalkSpeed * gameSeconds);

            // Snapshot keys: deterministic order (the old code sorted by id value).
            var ids = new List<EntityId>();
            foreach (var kv in _creatures.All) ids.Add(kv.Key);
            ids.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!_creatures.TryGet(id, out var cr)) continue;
                if (!_position.TryGet(id, out var pos)) continue;

                // Where the creature is THIS tick after moving — a local copy so we never
                // mutate the settled (read-phase, shared) PositionData object; the move
                // lands authoritatively via PositionSetIntent next tick. The old code wrote
                // pos.X/pos.Z in place; here we track them locally for the attack check.
                float here_x = pos.X, here_z = pos.Z;

                // Wander toward the current destination; on arrival, pick another.
                float dx = cr.TargetX - pos.X, dz = cr.TargetZ - pos.Z;
                float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (dist <= ArriveDistance)
                {
                    uint h = Hash(id.Value, tick);
                    double ang = (h & 0xFFFF) / 65535.0 * 2.0 * System.Math.PI;
                    double r = WanderRadius * (0.3 + 0.7 * (((h >> 16) & 0xFF) / 255.0));
                    cr.TargetX = pos.X + (float)(System.Math.Cos(ang) * r);
                    cr.TargetZ = pos.Z + (float)(System.Math.Sin(ang) * r);
                    Events.Publish(new CreatureSetIntent { Id = id, Data = cr });
                }
                else
                {
                    float nx = pos.X + dx / dist * step;
                    float nz = pos.Z + dz / dist * step;
                    float yaw = (float)(System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI);
                    Events.Publish(new PositionSetIntent { Id = id, X = nx, Y = pos.Y, Z = nz, Yaw = yaw });
                    here_x = nx; here_z = nz;
                }

                // Reach a civilian → bite (the combat emitter), on cooldown.
                if (tick >= cr.NextAttackTick)
                {
                    // Retaliate against a recent attacker if it's in reach; else the
                    // nearest civilian. Use the post-move position (here_x/z).
                    var victim = Retaliation(cr, here_x, here_z, tick);
                    if (victim.IsNone) victim = NearestCivilian(id, here_x, here_z, AttackRange);
                    if (!victim.IsNone)
                    {
                        Events.Publish(new DamageEvent
                        {
                            Target = victim, Source = id, Amount = AttackDamage, Type = DamageType.Physical,
                        });
                        cr.NextAttackTick = tick + AttackCooldownTicks;
                        Events.Publish(new CreatureSetIntent { Id = id, Data = cr });
                    }
                }
            }
        }

        /// Spawn one creature out past the edge of habitation: the centroid of the
        /// living crowd, pushed out by SpawnRadius along a deterministic bearing.
        /// Returns false if there's no one to be a threat to yet.
        bool TrySpawn(long tick)
        {
            double sx = 0, sz = 0; int n = 0;
            foreach (var kv in _position.All)
            {
                if (_creatures.Contains(kv.Key)) continue;
                sx += kv.Value.X; sz += kv.Value.Z; n++;
            }
            if (n == 0) return false;
            float cx = (float)(sx / n), cz = (float)(sz / n);

            var id = _identity.Allocate();
            uint h = Hash(id.Value, tick);
            double ang = (h & 0xFFFF) / 65535.0 * 2.0 * System.Math.PI;
            float px = cx + (float)(System.Math.Cos(ang) * SpawnRadius);
            float pz = cz + (float)(System.Math.Sin(ang) * SpawnRadius);

            Events.Publish(new IdentitySetIntent
            {
                Id = id,
                Data = new IdentityData
                {
                    Name = "Beast", Kind = EntityKind.EnemyMonster,
                    Race = -1, Gender = 0, CareerIndex = -1, Level = 1, FactionId = 0, Team = 0,
                },
            });
            Events.Publish(new PositionSetIntent { Id = id, X = px, Y = 0f, Z = pz, Yaw = 0f });
            Events.Publish(new VitalsSetIntent { Id = id, Data = new VitalsData { CurrentHealth = 20, MaxHealth = 20 } });
            Events.Publish(new CreatureSetIntent { Id = id, Data = new CreatureData { TargetX = px, TargetZ = pz, NextAttackTick = 0 } });
            return true;
        }

        /// The creature's recent attacker, if it's still in reach and the grudge is
        /// fresh — so striking a creature draws its wrath onto you. None otherwise.
        EntityId Retaliation(CreatureData cr, float fromX, float fromZ, long tick)
        {
            if (cr.LastAttacker.IsNone || tick - cr.LastStruckTick > RetaliationWindowTicks) return EntityId.None;
            if (!_position.TryGet(cr.LastAttacker, out var tp) || tp == null) return EntityId.None;
            if (_vitals.TryGet(cr.LastAttacker, out var v) && v != null && v.IsDead) return EntityId.None;
            float dx = tp.X - fromX, dz = tp.Z - fromZ;
            return dx * dx + dz * dz <= AttackRange * AttackRange ? cr.LastAttacker : EntityId.None;
        }

        EntityId NearestCivilian(EntityId self, float fromX, float fromZ, float range)
        {
            float best = range * range;
            EntityId nearest = EntityId.None;
            foreach (var kv in _position.All)
            {
                var other = kv.Key;
                if (other == self || _creatures.Contains(other)) continue;
                if (!_identity.TryGet(other, out var oid) || oid == null || oid.Kind != EntityKind.CivilianNPC) continue;
                if (_vitals.TryGet(other, out var v) && v != null && v.IsDead) continue;
                float dx = kv.Value.X - fromX, dz = kv.Value.Z - fromZ;
                float d2 = dx * dx + dz * dz;
                // Deterministic tie-break by id so the same victim is chosen on replay.
                if (d2 < best || (d2 == best && !nearest.IsNone && other.Value < nearest.Value))
                {
                    best = d2; nearest = other;
                }
            }
            return nearest;
        }

        static uint Hash(int idValue, long tick)
        {
            uint x = (uint)(idValue * 2654435761u) ^ (uint)(tick * 40503u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }
    }
}
