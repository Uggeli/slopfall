using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// S1 membrane gates (docs/cognitive_substrate_S1_membrane.md). The test that
    /// matters for the substrate refactor: is the system wired into the loop, and
    /// do entities actually get + consume a SubjectiveView? Plus the pure pieces
    /// (per-agent interpret; the clamp). Old MeanRegard pure-math tests are gone —
    /// that logic now lives behind the one interpret() path.
    public class MembraneTests
    {
        static BehaviorData Loitering() => new BehaviorData
        {
            Activity = ActivityKind.Idle, Phase = ActivityPhase.Doing,
            TargetBuilding = -1, RemainingGameMinutes = 100000,
        };

        [Fact]
        public void SubjectiveSystem_IsWired_AndEntitiesGetAView()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var a = h.SpawnEntity("A");
            var b = h.SpawnEntity("B");
            // a regards b warmly; both loiter in sensing range, out-and-about so
            // they sense each other (no grid in the harness → no occlusion).
            var rels = new RelationsData();
            rels.Of[b] = new RelationData { Regard = 0.7, Familiarity = 0.5 };
            h.Ctx.Relations.Set(a, rels);
            h.Ctx.Position.Set(a, 0f, 0f, 0f, 0f);
            h.Ctx.Position.Set(b, 5f, 0f, 0f, 0f);
            h.Ctx.Behavior.Set(a, Loitering());
            h.Ctx.Behavior.Set(b, Loitering());
            h.SeedClock(hour: 12, timeScale: 60f);

            h.Step(10);     // sense-ticks build the view

            // The system is in the loop AND the entity uses it: a holds an
            // interpreted view of the sensed b, and the read reflects a's regard.
            Assert.True(h.Ctx.Subjective.TryGet(a, out var view), "no SubjectiveView for a — membrane not wired/used");
            var read = view.Entities.Find(e => e.Other == b);
            Assert.Equal(b, read.Other);
            Assert.True(read.Valence > 0, "a's read of warmly-regarded b is not positive: " + read.Valence);
        }

        [Fact]
        public void Interpret_IsPerAgent_SameOther_OppositeReads()
        {
            var h = new SimHarness();
            var c = new EntityId(9);
            var liker = new EntityId(1);
            var hater = new EntityId(2);
            var lr = new RelationsData(); lr.Of[c] = new RelationData { Regard = 0.8 }; h.Ctx.Relations.Set(liker, lr);
            var hr = new RelationsData(); hr.Of[c] = new RelationData { Regard = -0.8 }; h.Ctx.Relations.Set(hater, hr);

            // Same objective other, opposite subjective reads — the whole point.
            Assert.True(SubjectiveSystem.Interpret(h.Ctx, liker, c).Valence > 0);
            Assert.True(SubjectiveSystem.Interpret(h.Ctx, hater, c).Valence < 0);
            Assert.Equal(0.0, SubjectiveSystem.Interpret(h.Ctx, liker, new EntityId(99)).Valence, 6); // stranger neutral
        }

        [Fact]
        public void RelationFactor_ColorsButNeverFlipsOrSwamps()
        {
            Assert.Equal(1.0, OddSystem.RelationFactor(0.0), 6);
            for (double r = -2.0; r <= 2.0; r += 0.25)
            {
                double f = OddSystem.RelationFactor(r);
                Assert.True(f >= 0.5 && f <= 1.5, "out of band at " + r + ": " + f);
                Assert.True(f > 0, "would flip the gate's sign at " + r);
            }
            Assert.True(OddSystem.RelationFactor(-0.5) < OddSystem.RelationFactor(0.5), "not monotone");
        }
    }
}
