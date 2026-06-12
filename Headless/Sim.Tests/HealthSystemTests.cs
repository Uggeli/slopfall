using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class HealthSystemTests
    {
        [Fact]
        public void Damage_ReducesHealth()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity(health: 50);

            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 20, Type = DamageType.Physical });
            h.Step();

            Assert.True(h.Ctx.Vitals.TryGet(id, out var v));
            Assert.Equal(30, v.CurrentHealth);
            Assert.False(v.IsDead);
        }

        [Fact]
        public void FatalDamage_ClampsToZero_SetsDead_EmitsDeathOnce()
        {
            var h = new SimHarness();
            var killer = h.SpawnEntity("Killer");
            var victim = h.SpawnEntity("Victim", health: 10);
            var deaths = h.Collect<DeathSimEvent>();

            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = victim, Source = killer, Amount = 25, Type = DamageType.Fire });
            h.Step(3);

            Assert.True(h.Ctx.Vitals.TryGet(victim, out var v));
            Assert.Equal(0, v.CurrentHealth);
            Assert.True(v.IsDead);
            Assert.Single(deaths);
            Assert.Equal(victim, deaths[0].Entity);
            Assert.Equal(killer, deaths[0].Killer);
            Assert.Equal(DamageType.Fire, deaths[0].FatalDamageType);
        }

        [Fact]
        public void DamageToDeadEntity_IsIgnored()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity(health: 5);
            var deaths = h.Collect<DeathSimEvent>();

            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 10 });
            h.Step();
            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 10 });
            h.Step(2);

            Assert.Single(deaths);          // second hit on a corpse emits nothing
        }

        [Fact]
        public void Heal_ClampsToMax()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity(health: 40, maxHealth: 50);

            h.Ctx.Inputs.Enqueue(new HealEvent { Target = id, Amount = 100 });
            h.Step();

            Assert.True(h.Ctx.Vitals.TryGet(id, out var v));
            Assert.Equal(50, v.CurrentHealth);
        }

        [Fact]
        public void Heal_OnDeadEntity_IsIgnored()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity(health: 5);

            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 10 });
            h.Step();
            h.Ctx.Inputs.Enqueue(new HealEvent { Target = id, Amount = 10 });
            h.Step();

            Assert.True(h.Ctx.Vitals.TryGet(id, out var v));
            Assert.Equal(0, v.CurrentHealth);
            Assert.True(v.IsDead);
        }

        [Fact]
        public void ZeroOrNegativeAmounts_AreIgnored()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity(health: 30);

            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 0 });
            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = -5 });
            h.Ctx.Inputs.Enqueue(new HealEvent { Target = id, Amount = -5 });
            h.Step();

            Assert.True(h.Ctx.Vitals.TryGet(id, out var v));
            Assert.Equal(30, v.CurrentHealth);
        }

        [Fact]
        public void TwoLethalHitsSameTick_EmitOneDeath()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity(health: 10);
            var deaths = h.Collect<DeathSimEvent>();

            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 15 });
            h.Ctx.Inputs.Enqueue(new DamageEvent { Target = id, Amount = 15 });
            h.Step(2);

            Assert.Single(deaths);
        }
    }
}
