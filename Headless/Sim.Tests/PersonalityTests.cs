using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class PersonalityTests
    {
        static double[] Centered()
        {
            var t = new double[TraitIndex.Count];
            for (int i = 0; i < t.Length; i++) t[i] = 0.5;
            return t;
        }

        [Fact]
        public void CenteredTraits_ReproduceGlobalConstants()
        {
            var p = PersonalityData.Derive(Centered());
            for (int axis = 0; axis < NeedAxis.Count; axis++)
            {
                Assert.Equal(ActivityCatalog.Weights[axis], p.Weights[axis], 5);
                Assert.Equal(1.0, p.DriftScale[axis], 5);
            }
            Assert.Equal("unremarkable", p.Describe());
        }

        [Fact]
        public void Extremes_ReadAsCharacter()
        {
            var t = Centered();
            t[TraitIndex.Sociability] = 0.9;
            t[TraitIndex.Chronotype] = 0.1;
            var p = PersonalityData.Derive(t);
            Assert.Contains("gregarious", p.Describe());
            Assert.Contains("early riser", p.Describe());
        }

        [Fact]
        public void Chronotype_ShiftsTheNightWindow()
        {
            // 21:30: center and owls awake at the edge differently.
            Assert.True(OddSystem.IsNightFor(21, 0.5));     // center: night from 21
            Assert.False(OddSystem.IsNightFor(21, 1.0));    // owl: still evening
            Assert.True(OddSystem.IsNightFor(19, 0.0));     // lark: already night at 19:30
            Assert.False(OddSystem.IsNightFor(19, 0.5));

            // Early morning mirror: lark is up at 05:30, owl still asleep at 06:30.
            Assert.False(OddSystem.IsNightFor(5, 0.0));
            Assert.True(OddSystem.IsNightFor(5, 0.5));
            Assert.True(OddSystem.IsNightFor(6, 1.0));
            Assert.False(OddSystem.IsNightFor(6, 0.5));
        }

        [Fact]
        public void Warmth_DecidesCharity()
        {
            SimHarness Make(double warmth, out EntityId pauper, out EntityId mark)
            {
                var h = new SimHarness(tickIntervalSeconds: 1.0);
                pauper = h.SpawnEntity("Pauper");
                mark = h.SpawnEntity("Mark");
                var needs = new NeedsData();
                needs.V[NeedAxis.CoinDef] = 1.0;
                h.Ctx.Needs.Set(pauper, needs);
                h.Ctx.Coin.Set(pauper, 0.0);
                h.Ctx.Needs.Set(mark, new NeedsData());
                h.Ctx.Coin.Set(mark, 1.0);
                h.Ctx.Residency.Set(mark, new ResidencyData { BuildingIndex = 3, Role = ResidentRole.Keeper });
                h.Ctx.Position.Set(pauper, 0f, 0f, 0f, 0f);
                h.Ctx.Position.Set(mark, 5f, 0f, 0f, 0f);
                h.Ctx.Behavior.Set(pauper, new BehaviorData
                {
                    Activity = ActivityKind.Beg, Phase = ActivityPhase.Doing,
                    TargetBuilding = -1, RemainingGameMinutes = 100000,
                });
                h.Ctx.Behavior.Set(mark, new BehaviorData
                {
                    Activity = ActivityKind.Idle, Phase = ActivityPhase.Doing,
                    TargetBuilding = -1, RemainingGameMinutes = 100000,
                });

                var traits = new double[TraitIndex.Count];
                for (int i = 0; i < traits.Length; i++) traits[i] = 0.5;
                traits[TraitIndex.Warmth] = warmth;
                h.Ctx.Personality.Set(mark, PersonalityData.Derive(traits));

                h.SeedClock(hour: 12, timeScale: 60f);
                return h;
            }

            var warm = Make(0.9, out var p1, out var m1);
            var granted = warm.Collect<HelpGrantedEvent>();
            warm.Step(12);
            Assert.Single(granted);

            var cold = Make(0.05, out var p2, out var m2);
            var refused = cold.Collect<HelpRefusedEvent>();
            cold.Step(12);
            Assert.Single(refused);
        }
    }

    public class PersonalityDayTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        static SimHarness LoadTown()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            TownLoader.Load(h.Ctx, maps.GetLocation("Daggerfall", "Gothway Garden"), blocks);
            h.SeedClock(hour: 5, minute: 30, timeScale: 60f);
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
            return h;
        }

        [Fact]
        public void EveryCivilian_HasAPersonality()
        {
            if (!Available) return;
            var h = LoadTown();
            foreach (var kv in h.Ctx.Residency.All)
            {
                Assert.True(h.Ctx.Personality.TryGet(kv.Key, out var p));
                for (int t = 0; t < TraitIndex.Count; t++)
                    Assert.InRange(p.Traits[t], 0.0, 1.0);
            }
        }

        [Fact]
        public void Wander_IsBackFromExtinction_AndRefusalsExist()
        {
            if (!Available) return;
            var h = LoadTown();
            var seen = new HashSet<ActivityKind>();
            int wanders = 0, refusals = 0;
            h.Ctx.Events.Subscribe<ActivityStartedEvent>(e =>
            {
                seen.Add(e.Activity);
                if (e.Activity == ActivityKind.Wander) wanders++;
            });
            h.Ctx.Events.Subscribe<HelpRefusedEvent>(e => refusals++);

            h.Step(1440);

            Assert.True(wanders > 20, "restless souls produced only " + wanders + " wanders");
            Assert.True(refusals > 0, "nobody was ever refused");
        }

        [Fact]
        public void Chronotypes_StaggerBedtimes()
        {
            if (!Available) return;
            var h = LoadTown();
            h.Step(990);    // 05:30 -> 22:00

            int asleep = 0, total = 0;
            foreach (var kv in h.Ctx.Behavior.All)
            {
                total++;
                if (kv.Value.Activity == ActivityKind.Sleep) asleep++;
            }
            // Pre-personality the whole town slept by ~21:30. Now larks are
            // long gone while owls keep the taverns open.
            Assert.True(asleep >= total / 4 && asleep <= total - 20,
                "asleep=" + asleep + "/" + total + " at 22:00 (want a spread, not all/none)");
        }
    }
}
