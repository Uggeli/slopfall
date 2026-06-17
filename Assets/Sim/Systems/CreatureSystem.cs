namespace DaggerfallWorkshop.Sim
{
    /// The threat layer (V2a). Maintains a small population of mobile hostile
    /// creatures at the edge of habitation: it spawns them, wanders them (it is the
    /// sole writer of creature Position — they don't use the civilian ODD/Movement
    /// path), and emits DamageEvent when one reaches a civilian (the combat emitter
    /// side; HealthSystem applies it and fires DeathSimEvent, LifecycleSystem
    /// despawns). Civilians read a creature as a threat through the one membrane
    /// (SubjectiveSystem.Interpret), which gives the fear drive its target (V2b).
    ///
    /// All constants are FROZEN placeholders — deliberately gentle (few creatures,
    /// light bite) so the threat has teeth without collapsing the town; tuned with
    /// everything else once the drive+fear program is in. See docs/drive_engine.md.
    public sealed class CreatureSystem : ISystem
    {
        const int TargetPopulation = 3;        // how many roam at once
        const float WalkSpeed = 1.1f;          // metres / game-second (a touch slower than a person's 1.4)
        const float WanderRadius = 25f;        // repick a destination within this of the current spot
        const float ArriveDistance = 1.5f;
        const float SpawnRadius = 40f;         // out past the edge of where people are
        const float AttackRange = 2.0f;
        const int AttackDamage = 1;            // light bite (placeholder)
        const long AttackCooldownTicks = 240;  // ~4 game-hours between bites at 1440 ticks/day

        SimulationContext _ctx;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;       // town not loaded yet

            // Keep the population topped up (creatures only die once V2b lets
            // civilians fight back; until then this just seeds the initial roam).
            while (_ctx.Creatures.Count < TargetPopulation && TrySpawn(tick)) { }

            double gameSeconds = clock.DeltaGameSeconds;
            float step = (float)(WalkSpeed * gameSeconds);

            // Snapshot keys: we mutate creature Position/data inside the loop.
            var ids = new System.Collections.Generic.List<EntityId>();
            foreach (var kv in _ctx.Creatures.All) ids.Add(kv.Key);
            ids.Sort((a, b) => a.Value.CompareTo(b.Value));   // deterministic order

            for (int i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!_ctx.Creatures.TryGet(id, out var cr)) continue;
                if (!_ctx.Position.TryGet(id, out var pos)) continue;

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
                    _ctx.Creatures.Set(id, cr);
                }
                else
                {
                    float nx = pos.X + dx / dist * step;
                    float nz = pos.Z + dz / dist * step;
                    float yaw = (float)(System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI);
                    _ctx.Position.Set(id, nx, pos.Y, nz, yaw);
                    pos.X = nx; pos.Z = nz;
                }

                // Reach a civilian → bite (the combat emitter), on cooldown.
                if (tick >= cr.NextAttackTick)
                {
                    // Retaliate against a recent attacker if it's in reach; else the
                    // nearest civilian.
                    var victim = Retaliation(cr, pos, tick);
                    if (victim.IsNone) victim = NearestCivilian(id, pos, AttackRange);
                    if (!victim.IsNone)
                    {
                        _ctx.Events.Emit(new DamageEvent
                        {
                            Target = victim, Source = id, Amount = AttackDamage, Type = DamageType.Physical,
                        });
                        cr.NextAttackTick = tick + AttackCooldownTicks;
                        _ctx.Creatures.Set(id, cr);
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
            foreach (var kv in _ctx.Position.All)
            {
                if (_ctx.Creatures.Contains(kv.Key)) continue;
                sx += kv.Value.X; sz += kv.Value.Z; n++;
            }
            if (n == 0) return false;
            float cx = (float)(sx / n), cz = (float)(sz / n);

            var id = _ctx.Identity.Allocate();
            uint h = Hash(id.Value, tick);
            double ang = (h & 0xFFFF) / 65535.0 * 2.0 * System.Math.PI;
            float px = cx + (float)(System.Math.Cos(ang) * SpawnRadius);
            float pz = cz + (float)(System.Math.Sin(ang) * SpawnRadius);

            _ctx.Identity.Set(id, new IdentityData
            {
                Name = "Beast", Kind = EntityKind.EnemyMonster,
                Race = -1, Gender = 0, CareerIndex = -1, Level = 1, FactionId = 0, Team = 0,
            });
            _ctx.Position.Set(id, px, 0f, pz, 0f);
            _ctx.Vitals.Set(id, new VitalsData { CurrentHealth = 20, MaxHealth = 20 });
            _ctx.Creatures.Set(id, new CreatureData { TargetX = px, TargetZ = pz, NextAttackTick = 0 });
            return true;
        }

        const long RetaliationWindowTicks = 120;   // a creature holds its grudge this long

        /// The creature's recent attacker, if it's still in reach and the grudge is
        /// fresh — so striking a creature draws its wrath onto you. None otherwise.
        EntityId Retaliation(CreatureData cr, PositionData from, long tick)
        {
            if (cr.LastAttacker.IsNone || tick - cr.LastStruckTick > RetaliationWindowTicks) return EntityId.None;
            if (!_ctx.Position.TryGet(cr.LastAttacker, out var tp) || tp == null) return EntityId.None;
            if (_ctx.Vitals.TryGet(cr.LastAttacker, out var v) && v != null && v.IsDead) return EntityId.None;
            float dx = tp.X - from.X, dz = tp.Z - from.Z;
            return dx * dx + dz * dz <= AttackRange * AttackRange ? cr.LastAttacker : EntityId.None;
        }

        EntityId NearestCivilian(EntityId self, PositionData from, float range)
        {
            float best = range * range;
            EntityId nearest = EntityId.None;
            foreach (var kv in _ctx.Position.All)
            {
                var other = kv.Key;
                if (other == self || _ctx.Creatures.Contains(other)) continue;
                if (!_ctx.Identity.TryGet(other, out var oid) || oid == null || oid.Kind != EntityKind.CivilianNPC) continue;
                if (_ctx.Vitals.TryGet(other, out var v) && v != null && v.IsDead) continue;
                float dx = kv.Value.X - from.X, dz = kv.Value.Z - from.Z;
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
