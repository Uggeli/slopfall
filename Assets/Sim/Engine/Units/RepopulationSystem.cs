using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.RepopulationSystem (L2.4 — the
    // entry half of turnover). Backfills each vacated residency slot with a broke adult
    // immigrant so headcount stays roughly stationary and a dead keeper's shop is
    // re-staffed.
    //
    // The old system called TownLoader.SpawnImmigrant, which wrote ~11 registries
    // directly via ctx.* setters. Here the SAME seeding is re-expressed as INTENT
    // EMISSIONS: Identity.Allocate (direct — the allocator stays synchronous, as the
    // CQRS IdentityRegistry exposes it) gives the new id, then one whole-value set
    // intent per registry the spawn seeds (IdentitySetIntent, VitalsSetIntent,
    // StatsSetIntent, PositionSetIntent, ResidencySetIntent, NeedsSetIntent,
    // CoinSetIntent, PersonalitySetIntent, LifeSetIntent, ConscienceSetIntent), plus a
    // PlaceLearnIntent for each settlement building, plus ResidentJoinIntent to add the
    // newcomer to the settlement roster. The registries apply them next tick.
    //
    // RNG: the old SpawnImmigrant drew from the shared stateful ctx.Random for gender /
    // health / stats / needs / coin / traits. That stream is order-dependent and has no
    // CQRS-safe equivalent, so — matching SeedLife / SeedConscience, which already hash
    // the id — every per-spawn draw here is a STATELESS hash of (newId, field-salt). The
    // distributions' SHAPES are preserved exactly; the concrete RNG values differ from
    // the legacy stream (unavoidable when retiring the shared SimRandom).
    //
    // Role: DespawnedEvent does not carry the vacated role, so it's inferred — if the
    // building has no live keeper left in ResidencyRegistry, the dead was its keeper and
    // the slot is re-staffed as Keeper; otherwise a plain Resident.
    public sealed class RepopulationSystem : SimSystem
    {
        readonly IdentityRegistry _identity;
        readonly ResidencyRegistry _residency;
        readonly BuildingRegistry _buildings;
        readonly SettlementRegistry _settlements;

        public RepopulationSystem(EventBus events, IdentityRegistry identity, ResidencyRegistry residency,
                                  BuildingRegistry buildings, SettlementRegistry settlements) : base(events)
        {
            _identity = identity;
            _residency = residency;
            _buildings = buildings;
            _settlements = settlements;
        }

        public override void Update(long tick)
        {
            var despawns = Events.GetEvents<DespawnedEvent>();
            if (despawns.Length == 0) return;

            // Only real residency vacancies, processed in a stable (settlement, building,
            // entity) order so spawns are deterministic.
            var vacancies = new List<DespawnedEvent>();
            foreach (ref readonly var d in despawns)
                if (d.Settlement >= 0 && d.Building >= 0) vacancies.Add(d);
            if (vacancies.Count == 0) return;
            vacancies.Sort(Order);

            for (int i = 0; i < vacancies.Count; i++)
            {
                var v = vacancies[i];
                if (v.Settlement < 0 || v.Settlement >= _settlements.Count) continue;
                SpawnImmigrant(_settlements.Get(v.Settlement), v.Building);
            }
        }

        /// Re-staff a vacated slot with a broke adult immigrant — TownLoader.SpawnImmigrant's
        /// logic re-expressed as intent emissions. Returns nothing; everything is on the bus.
        void SpawnImmigrant(SettlementData settlement, int buildingIndex)
        {
            if (settlement == null) return;
            if (!_buildings.TryGet(buildingIndex, out var row) || row == null) return;

            var role = InferRole(buildingIndex);
            var id = _identity.Allocate();
            int v = id.Value;

            // --- Identity (TownLoader.Spawn) ---
            Events.Publish(new IdentitySetIntent
            {
                Id = id,
                Data = new IdentityData
                {
                    Name = "Newcomer (" + settlement.Name + ")",
                    Kind = EntityKind.CivilianNPC,
                    Race = -1,
                    Gender = (Hash(v, 0x01) & 1) == 1 ? 0 : 1,
                    CareerIndex = -1,
                    Level = 1,
                    FactionId = row.FactionId,
                    Team = 0,
                }
            });

            // --- Vitals: maxHealth = 40 + [0,21) ---
            int maxHealth = 40 + (int)(Hash(v, 0x02) % 21u);
            Events.Publish(new VitalsSetIntent
            {
                Id = id,
                Data = new VitalsData
                {
                    CurrentHealth = maxHealth, MaxHealth = maxHealth,
                    CurrentMagicka = 10, MaxMagicka = 10,
                    CurrentFatigue = 100, MaxFatigue = 100,
                    CurrentBreath = 10, MaxBreath = 10,
                }
            });

            // --- Stats: each = 30 + [0,31) ---
            var stats = new StatsData();
            for (int s = 0; s < StatIndex.Count; s++)
                stats.Stats[s] = 30 + (int)(Hash(v, (uint)(0x10 + s)) % 31u);
            Events.Publish(new StatsSetIntent { Id = id, Data = stats });

            // --- Position: at the building, facing its rotation ---
            Events.Publish(new PositionSetIntent { Id = id, X = row.X, Y = 0f, Z = row.Z, Yaw = row.YRotation });

            // --- Residency ---
            Events.Publish(new ResidencySetIntent
            {
                Id = id,
                Data = new ResidencyData { BuildingIndex = buildingIndex, Role = role }
            });

            // --- Needs: mid-range poles so the first day starts varied ---
            var needs = new NeedsData();
            needs.V[NeedAxis.Hunger] = 0.2 + Unit(v, 0x20) * 0.3;
            needs.V[NeedAxis.EnergyDef] = 0.1 + Unit(v, 0x21) * 0.3;
            needs.V[NeedAxis.SocialDef] = 0.3 + Unit(v, 0x22) * 0.4;
            needs.V[NeedAxis.GoodsDef] = 0.2 + Unit(v, 0x23) * 0.3;
            needs.V[NeedAxis.Attire] = 0.2 + Unit(v, 0x24) * 0.3;
            needs.V[NeedAxis.CoinDef] = 0.0;             // retired axis
            Events.Publish(new NeedsSetIntent { Id = id, Data = needs });

            // --- Coin: SpawnImmigrant overrides Spawn's purse with 0 (broke newcomer
            //     mints no money → conservation holds). The intermediate keeper/resident
            //     coin in Spawn is moot, so we seed 0 directly. ---
            Events.Publish(new CoinSetIntent { Id = id, Amount = 0 });

            // --- Personality: traits = (u + u) * 0.5, biased to the middle ---
            var traits = new double[TraitIndex.Count];
            for (int t = 0; t < TraitIndex.Count; t++)
                traits[t] = (Unit(v, (uint)(0x30 + 2 * t)) + Unit(v, (uint)(0x31 + 2 * t))) * 0.5;
            Events.Publish(new PersonalitySetIntent { Id = id, Data = PersonalityData.Derive(traits) });

            // --- Life + Conscience: hashed off the id, exactly as TownLoader does ---
            Events.Publish(SeedLife(id));
            Events.Publish(new ConscienceSetIntent { Id = id, Data = SeedConscience(id) });

            // --- Roster join (SettlementRegistry applies) ---
            Events.Publish(new ResidentJoinIntent { Settlement = settlement.Id, Entity = id });

            // --- "residents know the whole town" — learn every settlement building ---
            for (int b = 0; b < settlement.Buildings.Count; b++)
                Events.Publish(new PlaceLearnIntent { Id = id, Building = settlement.Buildings[b] });
        }

        /// Keeper if the vacated building has no surviving keeper (re-staff a dead
        /// keeper's shop); a plain Resident otherwise.
        ResidentRole InferRole(int buildingIndex)
        {
            foreach (var kv in _residency.All)
                if (kv.Value.BuildingIndex == buildingIndex && kv.Value.Role == ResidentRole.Keeper)
                    return ResidentRole.Resident;
            return ResidentRole.Keeper;
        }

        // --- Life / Conscience seeds, copied verbatim from TownLoader (id-hashed, so
        //     they never perturbed the spawn RNG stream there either). ---

        static LifeSetIntent SeedLife(EntityId id)
        {
            uint h = LifeHash(id.Value);
            double a = (h & 0xFFFF) / 65535.0;
            double b = ((h >> 16) & 0xFFFF) / 65535.0;
            return new LifeSetIntent
            {
                Id = id,
                AgeYears = 18 + a * 42,        // 18..60 — adults at immigration
                LifespanYears = 58 + b * 24,   // 58..82
            };
        }

        static ConscienceData SeedConscience(EntityId id)
        {
            double pride = (LifeHash(id.Value ^ 0x5A5A5A5A) & 0xFFFF) / 65535.0;
            double honesty = (LifeHash(id.Value ^ 0x3C3C3C3C) & 0xFFFF) / 65535.0;
            var c = new ConscienceData();
            c.Charge[(int)ActivityKind.Beg] = pride * 0.8;
            c.Charge[(int)ActivityKind.Steal] = 0.5 + 0.5 * honesty;
            return c;
        }

        static uint LifeHash(int x)
        {
            uint v = (uint)(x * 2654435761u);
            v ^= v >> 13; v *= 0x5bd1e995; v ^= v >> 15;
            return v;
        }

        // Stateless per-(id, salt) draw, replacing the shared stateful ctx.Random. Same
        // hash shape as SeedLife but salted per field so independent draws don't
        // correlate. Unit() maps it to [0,1).
        static uint Hash(int idValue, uint salt)
        {
            unchecked
            {
                uint x = (uint)(idValue * 2654435761u) ^ (salt * 2246822519u);
                x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
                return x;
            }
        }

        static double Unit(int idValue, uint salt) => Hash(idValue, salt) / 4294967296.0;

        static int Order(DespawnedEvent a, DespawnedEvent b)
        {
            int c = a.Settlement.CompareTo(b.Settlement);
            if (c != 0) return c;
            c = a.Building.CompareTo(b.Building);
            return c != 0 ? c : a.Entity.Value.CompareTo(b.Entity.Value);
        }
    }
}
