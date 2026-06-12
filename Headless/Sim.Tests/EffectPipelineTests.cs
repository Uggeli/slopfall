using System.Linq;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class EffectPipelineTests
    {
        static ApplyEffectEvent Poison(EntityId target, int magnitude = 2, int duration = 3) =>
            new ApplyEffectEvent
            {
                Target = target, Key = "Poison-Drug", Magnitude = magnitude,
                DurationTicks = duration, AppliesPerTick = true,
            };

        [Fact]
        public void ApplyEffect_StoresInstance_SameTick()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(Poison(id, duration: 5));
            h.Step();

            Assert.True(h.Ctx.Effects.TryGet(id, out var fx));
            Assert.Single(fx.Active);
            Assert.Equal("Poison-Drug", fx.Active[0].Key);
            // EffectTickSystem already decremented once this tick.
            Assert.Equal(4, fx.Active[0].RemainingTicks);
        }

        [Fact]
        public void InstantaneousEffect_EmitsApplied_NeverStored()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();
            var applied = h.Collect<EffectAppliedEvent>();

            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "InstantHit", Magnitude = 9, DurationTicks = 0 });
            h.Step(2);

            Assert.Single(applied);
            bool hasRow = h.Ctx.Effects.TryGet(id, out var fx);
            Assert.True(!hasRow || fx.Active.Count == 0);
        }

        [Fact]
        public void PerTickEffect_EmitsTicked_EachTickUntilExpiry()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();
            var ticked = h.Collect<EffectTickedEvent>();

            h.Ctx.Inputs.Enqueue(Poison(id, duration: 3));
            h.Step(6);                      // generous margin past expiry

            Assert.Equal(3, ticked.Count);  // exactly DurationTicks fires
        }

        [Fact]
        public void ExpiredEffect_IsRemoved_AndExpiryEventFires()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();
            var expired = h.Collect<EffectExpiredEvent>();

            h.Ctx.Inputs.Enqueue(Poison(id, duration: 2));
            h.Step(4);

            Assert.Single(expired);
            Assert.Equal("Poison-Drug", expired[0].Key);
            bool hasRow = h.Ctx.Effects.TryGet(id, out var fx);
            Assert.True(!hasRow || fx.Active.Count == 0);
        }

        [Fact]
        public void RemoveEffect_RemovesFirstMatchingInstanceOnly()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(Poison(id, magnitude: 1, duration: 100));
            h.Ctx.Inputs.Enqueue(Poison(id, magnitude: 2, duration: 100));
            h.Step();
            h.Ctx.Inputs.Enqueue(new RemoveEffectEvent { Target = id, Key = "Poison-Drug" });
            h.Step();

            Assert.True(h.Ctx.Effects.TryGet(id, out var fx));
            Assert.Single(fx.Active);
            Assert.Equal(2, fx.Active[0].Magnitude);   // first instance was removed
        }

        [Fact]
        public void Aggregate_SumsSameKeyMagnitudes()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "Strength", Magnitude = 10, DurationTicks = 100 });
            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "Strength", Magnitude = 5, DurationTicks = 100 });
            h.Step();

            Assert.True(h.Ctx.EffectAggregate.TryGet(id, out var agg));
            Assert.Equal(15, agg.Modifiers["Strength"]);
        }

        [Fact]
        public void Aggregate_RowDisappears_AfterAllEffectsExpire()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "Strength", Magnitude = 10, DurationTicks = 2 });
            h.Step();
            Assert.True(h.Ctx.EffectAggregate.TryGet(id, out var agg) && agg.Modifiers.Count > 0);

            h.Step(4);
            bool hasRow = h.Ctx.EffectAggregate.TryGet(id, out agg);
            Assert.True(!hasRow || agg.Modifiers.Count == 0);
        }

        [Fact]
        public void StatusFlags_DeriveFromEffectKeys_AndClear()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "Paralyze", Magnitude = 1, DurationTicks = 2 });
            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "Invisibility", Magnitude = 1, DurationTicks = 2 });
            h.Step();

            var flags = h.Ctx.StatusFlags.Get(id);
            Assert.True(flags.HasFlag(StatusFlags.Paralyzed));
            Assert.True(flags.HasFlag(StatusFlags.Invisible));

            h.Step(4);                      // both expire and get removed
            Assert.Equal(StatusFlags.None, h.Ctx.StatusFlags.Get(id));
        }

        [Fact]
        public void UnknownEffectKey_MapsToNoFlag()
        {
            var h = new SimHarness();
            var id = h.SpawnEntity();

            h.Ctx.Inputs.Enqueue(new ApplyEffectEvent { Target = id, Key = "TotallyUnknown", Magnitude = 1, DurationTicks = 10 });
            h.Step();

            Assert.Equal(StatusFlags.None, h.Ctx.StatusFlags.Get(id));
        }

        [Fact]
        public void TwoEntities_EffectsStayIsolated()
        {
            var h = new SimHarness();
            var a = h.SpawnEntity("A");
            var b = h.SpawnEntity("B");

            h.Ctx.Inputs.Enqueue(Poison(a, duration: 50));
            h.Step();

            Assert.True(h.Ctx.Effects.TryGet(a, out var fxA) && fxA.Active.Count == 1);
            bool bHas = h.Ctx.Effects.TryGet(b, out var fxB) && fxB.Active.Count > 0;
            Assert.False(bHas);
        }
    }
}
