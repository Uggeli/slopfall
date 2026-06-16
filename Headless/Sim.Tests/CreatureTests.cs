using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// The threat layer (V2a): creatures spawn, are read as threats through the
    /// one membrane, and bite adjacent civilians (the combat emitter). CreatureSystem
    /// isn't in the harness loop (it would pollute every other test with hostiles),
    /// so these drive it directly.
    public class CreatureTests
    {
        static EntityId Civilian(SimHarness h, float x, float z, int health = 20)
        {
            var id = h.Ctx.Identity.Allocate();
            h.Ctx.Identity.Set(id, new IdentityData { Name = "Civ", Kind = EntityKind.CivilianNPC, Level = 1 });
            h.Ctx.Position.Set(id, x, 0f, z, 0f);
            h.Ctx.Vitals.Set(id, new VitalsData { CurrentHealth = health, MaxHealth = health });
            return id;
        }

        [Fact]
        public void Interpret_ReadsACreatureAsAThreat()
        {
            var h = new SimHarness();
            var self = Civilian(h, 0, 0);

            var beast = h.Ctx.Identity.Allocate();
            h.Ctx.Creatures.Set(beast, new CreatureData());   // membership = creature

            var read = SubjectiveSystem.Interpret(h.Ctx, self, beast);
            Assert.True(read.Threat > 0, "a creature must read as a threat");
            Assert.True(read.Valence < 0, "a threat must read aversive");

            // A normal civilian carries no threat — the channel is creature-only.
            var neighbour = Civilian(h, 1, 1);
            Assert.Equal(0.0, SubjectiveSystem.Interpret(h.Ctx, self, neighbour).Threat, 6);
        }

        [Fact]
        public void CreatureSystem_SpawnsToTargetPopulation()
        {
            var h = new SimHarness();
            Civilian(h, 10, 10);          // someone to be a threat to (spawn centroid)
            h.SeedClock(timeScale: 60f);
            h.Step(1);                    // seed the clock (Year > 0)

            var cs = new CreatureSystem();
            cs.Init(h.Ctx);
            cs.Update(10);

            Assert.Equal(3, h.Ctx.Creatures.Count);   // TargetPopulation, FROZEN
            // Spawned out past the edge of habitation (≈ SpawnRadius from centroid).
            foreach (var kv in h.Ctx.Creatures.All)
            {
                Assert.True(h.Ctx.Identity.TryGet(kv.Key, out var idn) && idn.Kind == EntityKind.EnemyMonster);
                Assert.True(h.Ctx.Position.TryGet(kv.Key, out _));
            }
        }

        [Fact]
        public void CreatureSystem_BitesAnAdjacentCivilian()
        {
            var h = new SimHarness();
            Civilian(h, 100, 100);
            h.SeedClock(timeScale: 60f);
            h.Step(1);

            var cs = new CreatureSystem();
            cs.Init(h.Ctx);
            cs.Update(10);                // spawns

            // Park one creature on the civilian (target = its spot, so it doesn't
            // wander off before the bite) with its cooldown ready.
            EntityId beast = default;
            foreach (var kv in h.Ctx.Creatures.All) { beast = kv.Key; break; }
            h.Ctx.Position.Set(beast, 100f, 0f, 100f, 0f);
            h.Ctx.Creatures.TryGet(beast, out var cr);
            cr.TargetX = 100f; cr.TargetZ = 100f; cr.NextAttackTick = 0;
            h.Ctx.Creatures.Set(beast, cr);

            cs.Update(11);

            Assert.True(h.Ctx.Creatures.TryGet(beast, out var after) && after.NextAttackTick > 11,
                "creature didn't bite the adjacent civilian");
        }
    }
}
