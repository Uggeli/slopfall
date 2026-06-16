using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// The fear drive (V2b): the emotion-as-controller. CompleteThreat is the
    /// completion operator (the ignition bifurcation in pure form); the integration
    /// test drives the full membrane → controller loop with a real creature.
    public class FearTests
    {
        [Fact]
        public void CompleteThreat_IgnitionBifurcation()
        {
            // An ambiguous cue (clarity 0.3): a BOLD soul (floor 0, no arousal)
            // leaves it faint — it never crosses into felt threat (ignores it).
            Assert.Equal(0.3, NeedsSystem.CompleteThreat(0.3, floor: 0.0, arousal: 0.0), 6);

            // A TIMID soul (high floor) completes the same cue UP toward threat —
            // fear can bootstrap from rest. This is the bifurcation.
            double timid = NeedsSystem.CompleteThreat(0.3, floor: 0.3, arousal: 0.0);
            Assert.True(timid > 0.45, "a timid soul must complete an ambiguous cue upward: " + timid);

            // Rising arousal completes harder still — the positive-feedback spiral.
            double spiraling = NeedsSystem.CompleteThreat(0.3, floor: 0.3, arousal: 0.5);
            Assert.True(spiraling > timid, "arousal must feed completion (the spiral)");

            // A CLEAR cue is full threat for everyone — personality only colors the
            // ambiguous middle, not an unmistakable danger.
            Assert.Equal(1.0, NeedsSystem.CompleteThreat(1.0, floor: 0.0, arousal: 0.0), 6);
            Assert.Equal(1.0, NeedsSystem.CompleteThreat(1.0, floor: 0.3, arousal: 0.9), 6);
        }

        [Fact]
        public void Fear_RisesNearAThreat_AndResetsWhenItsGone()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);

            var civ = h.Ctx.Identity.Allocate();
            h.Ctx.Identity.Set(civ, new IdentityData { Name = "Civ", Kind = EntityKind.CivilianNPC, Level = 1 });
            h.Ctx.Position.Set(civ, 0f, 0f, 0f, 0f);
            h.Ctx.Needs.Set(civ, new NeedsData());
            h.Ctx.Vitals.Set(civ, new VitalsData { CurrentHealth = 20, MaxHealth = 20 });
            // Idle is "out and about", so SenseSystem watches the street for them.
            // No Residency → OddSystem won't re-decide them; they hold this spot.
            h.Ctx.Behavior.Set(civ, new BehaviorData
            {
                Activity = ActivityKind.Idle, Phase = ActivityPhase.Doing, TargetBuilding = -1,
            });

            // A creature right beside them — a clear, close threat.
            var beast = h.Ctx.Identity.Allocate();
            h.Ctx.Identity.Set(beast, new IdentityData { Name = "Beast", Kind = EntityKind.EnemyMonster, Level = 1 });
            h.Ctx.Position.Set(beast, 1f, 0f, 1f, 0f);
            h.Ctx.Creatures.Set(beast, new CreatureData());

            h.SeedClock(timeScale: 60f);   // 1 game-minute per tick
            h.Step(20);

            Assert.True(h.Ctx.Needs.TryGet(civ, out var n1) && n1.V[NeedAxis.Fear] > 0.5,
                "fear didn't rise beside a close threat: " + (h.Ctx.Needs.TryGet(civ, out var z) ? z.V[NeedAxis.Fear] : -1));

            // The threat leaves; fear ebbs back toward the vigilance floor.
            h.Ctx.Creatures.Remove(beast);
            h.Ctx.Position.Remove(beast);
            h.Ctx.Identity.Remove(beast);
            h.Step(50);

            Assert.True(h.Ctx.Needs.TryGet(civ, out var n2) && n2.V[NeedAxis.Fear] < 0.3,
                "fear didn't reset once the threat was gone: " + n2.V[NeedAxis.Fear]);
        }

        static readonly double[] W = ActivityCatalog.Weights;

        [Fact]
        public void Flee_OutscoresIdle_WhenAfraid()
        {
            var v = new double[NeedAxis.Count];
            v[NeedAxis.Fear] = 1.0;   // a loud threat

            double flee = OddScore.Compute(v, ActivityCatalog.Flee.Delta, W, 1.0, 0);
            double idle = OddScore.Compute(v, ActivityCatalog.Idle.Delta, W, 1.0, ActivityCatalog.Idle.BaseUtility);
            Assert.True(flee > idle, "a frightened agent should want to flee over idling");
        }

        [Fact]
        public void EscapeAffordability_StarvationPinsTheAgent()
        {
            // Both afraid; one fed, one starving.
            var fed = new double[NeedAxis.Count];      fed[NeedAxis.Fear] = 1.0;      fed[NeedAxis.Hunger] = 0.1;
            var starving = new double[NeedAxis.Count]; starving[NeedAxis.Fear] = 1.0; starving[NeedAxis.Hunger] = 1.5;

            // The desperation edge (hunger ⊣ fear) grades fear's urgency down hard
            // for the starving agent, barely for the fed one.
            double fFed = OddSystem.DesperationFactor(fed, NeedAxis.Fear);
            double fStarv = OddSystem.DesperationFactor(starving, NeedAxis.Fear);
            Assert.True(fFed > 0.9, "a fed agent flees freely: " + fFed);
            Assert.True(fStarv < 0.4, "a starving agent's fear is graded down: " + fStarv);

            // Fed + afraid → Flee beats eating (little hunger). Starving + afraid →
            // eating beats the graded-down Flee: it can't afford to run (it pins).
            double fleeFed = OddScore.Compute(fed, ActivityCatalog.Flee.Delta, W, fFed, 0);
            double eatFed = OddScore.Compute(fed, ActivityCatalog.EatHome.Delta, W, 1.0, 0);
            Assert.True(fleeFed > eatFed, "fed + afraid should flee");

            double fleeStarv = OddScore.Compute(starving, ActivityCatalog.Flee.Delta, W, fStarv, 0);
            double eatStarv = OddScore.Compute(starving, ActivityCatalog.EatHome.Delta, W, 1.0, 0);
            Assert.True(eatStarv > fleeStarv, "starving + afraid can't afford to flee — it pins (the clamp)");
        }
    }
}
