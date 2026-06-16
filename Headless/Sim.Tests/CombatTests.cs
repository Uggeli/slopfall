using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Combat (V2b, fight-or-flight). CombatSystem is in the harness loop but
    /// inert unless an agent is Doing Attack; CreatureSystem is NOT (so creatures
    /// don't respawn here — a killed one simply despawns, which is what we assert).
    public class CombatTests
    {
        [Fact]
        public void AnAttackingAgent_DamagesAndKillsACreature()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);

            // A creature with 20 HP standing still.
            var beast = h.Ctx.Identity.Allocate();
            h.Ctx.Identity.Set(beast, new IdentityData { Name = "Beast", Kind = EntityKind.EnemyMonster, Level = 1 });
            h.Ctx.Position.Set(beast, 0f, 0f, 0f, 0f);
            h.Ctx.Vitals.Set(beast, new VitalsData { CurrentHealth = 20, MaxHealth = 20 });
            h.Ctx.Creatures.Set(beast, new CreatureData());

            // A fighter parked right on it, Doing Attack (no Residency → OddSystem
            // won't re-decide them; they keep swinging).
            var fighter = h.Ctx.Identity.Allocate();
            h.Ctx.Identity.Set(fighter, new IdentityData { Name = "Guard", Kind = EntityKind.CivilianNPC, Level = 1 });
            h.Ctx.Position.Set(fighter, 1f, 0f, 0f, 0f);
            h.Ctx.Vitals.Set(fighter, new VitalsData { CurrentHealth = 30, MaxHealth = 30 });
            h.Ctx.Needs.Set(fighter, new NeedsData());
            h.Ctx.Behavior.Set(fighter, new BehaviorData
            {
                Activity = ActivityKind.Attack, Phase = ActivityPhase.Doing,
                TargetBuilding = -1, RemainingGameMinutes = 100000,
            });

            h.SeedClock(timeScale: 60f);
            h.Step(160);   // ~4 blows at a 30-tick cooldown, +death/despawn

            // The creature took its blows and was killed → LifecycleSystem despawned it.
            Assert.Equal(0, h.Ctx.Creatures.Count);
            Assert.False(h.Ctx.Vitals.TryGet(beast, out _), "the slain creature wasn't despawned");
        }

        [Fact]
        public void ChargeViolenceGuilt_TagsAttackOnTheConscience()
        {
            var h = new SimHarness();
            var attacker = h.Ctx.Identity.Allocate();
            Assert.Equal(0, h.Ctx.Conscience.ChargeFor(attacker, ActivityKind.Attack));

            CombatSystem.ChargeViolenceGuilt(h.Ctx, attacker, 0.5);
            Assert.Equal(0.5, h.Ctx.Conscience.ChargeFor(attacker, ActivityKind.Attack), 6);

            // Guilt accumulates and saturates at 1.
            CombatSystem.ChargeViolenceGuilt(h.Ctx, attacker, 0.8);
            Assert.Equal(1.0, h.Ctx.Conscience.ChargeFor(attacker, ActivityKind.Attack), 6);
        }
    }
}
