using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class SkillProgressionTests
    {
        [Fact]
        public void SkillExp_Accumulates_BelowThreshold()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(new SkillUsedEvent { Entity = id, Skill = "Stealth", Magnitude = 10 });
            h.Step();

            Assert.True(h.Ctx.Stats.TryGet(id, out var stats));
            Assert.Equal(10, stats.SkillExp["Stealth"]);
            Assert.False(stats.Skills.ContainsKey("Stealth") && stats.Skills["Stealth"] > 0);
        }

        [Fact]
        public void Skill_Promotes_AtThreshold()
        {
            // From skill 0 the first point costs (0+1) * 35 exp.
            var h = new SimHarness();
            var id = h.SpawnEntity();
            var advanced = h.Collect<SkillAdvancedEvent>();

            h.Ctx.Inputs.Enqueue(new SkillUsedEvent { Entity = id, Skill = "Stealth", Magnitude = 35 });
            h.Step(2);

            Assert.True(h.Ctx.Stats.TryGet(id, out var stats));
            Assert.Equal(1, stats.Skills["Stealth"]);
            Assert.Equal(0, stats.SkillExp["Stealth"]);
            Assert.Single(advanced);
            Assert.Equal(1, advanced[0].NewValue);
        }

        [Fact]
        public void BigExpGrant_PromotesMultipleTimes_EventsCarryEachStep()
        {
            // 35 + 70 = 105 exp -> two promotions from 0.
            var h = new SimHarness();
            var id = h.SpawnEntity();
            var advanced = h.Collect<SkillAdvancedEvent>();

            h.Ctx.Inputs.Enqueue(new SkillUsedEvent { Entity = id, Skill = "Stealth", Magnitude = 105 });
            h.Step(2);

            Assert.True(h.Ctx.Stats.TryGet(id, out var stats));
            Assert.Equal(2, stats.Skills["Stealth"]);
            Assert.Equal(2, advanced.Count);
            Assert.Equal(1, advanced[0].NewValue);
            Assert.Equal(2, advanced[1].NewValue);
        }

        [Fact]
        public void SkillUse_OnUnknownEntity_IsIgnored()
        {
            var h = new SimHarness();
            h.Ctx.Inputs.Enqueue(new SkillUsedEvent { Entity = new EntityId(999), Skill = "Stealth", Magnitude = 50 });
            h.Step(2);          // must not throw
        }

        [Fact]
        public void FifteenSkillPoints_TriggerLevelUp()
        {
            // ProgressionSystem ramp: level 2 at level*15 = 15 skill points.
            // 35 * (1+2+...+15) = 4200 exp buys exactly 15 promotions from 0.
            var h = new SimHarness();
            var id = h.SpawnEntity();
            var levelUps = h.Collect<LevelUpEvent>();

            h.Ctx.Inputs.Enqueue(new SkillUsedEvent { Entity = id, Skill = "Stealth", Magnitude = 4200 });
            h.Step(3);

            Assert.True(h.Ctx.Progression.TryGet(id, out var prog));
            Assert.Equal(2, prog.Level);
            Assert.Equal(0, prog.SkillPointsThisLevel);
            Assert.Single(levelUps);
            Assert.Equal(2, levelUps[0].NewLevel);
        }

        [Fact]
        public void KnownLimitation_IdentityLevel_NotSyncedByProgression()
        {
            // ProgressionRegistry's doc comment claims IdentityRegistry.Level is
            // kept in sync after level-up, but ProgressionSystem never writes
            // Identity. Pins the gap; flip when the sync is implemented.
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(new SkillUsedEvent { Entity = id, Skill = "Stealth", Magnitude = 4200 });
            h.Step(3);

            Assert.True(h.Ctx.Progression.TryGet(id, out var prog));
            Assert.Equal(2, prog.Level);
            Assert.True(h.Ctx.Identity.TryGet(id, out var identity));
            Assert.Equal(1, identity.Level);    // stale — documented gap
        }
    }
}
