using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class OddScoreTests
    {
        static readonly double[] W = ActivityCatalog.Weights;

        [Fact]
        public void HungryAgent_PrefersEating_OverIdle()
        {
            var v = new double[NeedAxis.Count];
            v[NeedAxis.Hunger] = 0.6;

            double eat = OddScore.Compute(v, ActivityCatalog.EatHome.Delta, W, 1.0, 0);
            double idle = OddScore.Compute(v, ActivityCatalog.Idle.Delta, W, 1.0, ActivityCatalog.Idle.BaseUtility);
            Assert.True(eat > idle);
        }

        [Fact]
        public void SatisfiedAgent_GapIsZero_OnlyBaseUtilityRemains()
        {
            var v = new double[NeedAxis.Count];   // all deficits zero
            double eat = OddScore.Compute(v, ActivityCatalog.EatHome.Delta, W, 1.0, 0);
            Assert.Equal(0, eat);

            double wander = OddScore.Compute(v, ActivityCatalog.Wander.Delta, W, 1.0, ActivityCatalog.Wander.BaseUtility);
            Assert.Equal(ActivityCatalog.Wander.BaseUtility, wander, 5);
        }

        [Fact]
        public void Overshoot_DoesNotScoreBeyondActualDeficit()
        {
            // Sleep promises -1.0 EnergyDef; from a deficit of only 0.2 the
            // post-state clamps at 0, so the gap must equal the 0.2 actually
            // recoverable — not the full promised magnitude.
            var v = new double[NeedAxis.Count];
            v[NeedAxis.EnergyDef] = 0.2;

            double s = OddScore.Compute(v, ActivityCatalog.Sleep.Delta, W, 1.0, 0);
            double expectedGap = Math.Sqrt(W[NeedAxis.EnergyDef] * 0.2 * 0.2);
            Assert.Equal(expectedGap, s, 5);
        }

        [Fact]
        public void TimeGate_ScalesGapButNotBaseUtility()
        {
            var v = new double[NeedAxis.Count];
            v[NeedAxis.EnergyDef] = 0.5;

            double gated = OddScore.Compute(v, ActivityCatalog.Sleep.Delta, W, 0.5, 0.06);
            double open = OddScore.Compute(v, ActivityCatalog.Sleep.Delta, W, 1.0, 0.06);
            Assert.True(open > gated);
            Assert.Equal(0.06, gated - (open - 0.06) * 0.5, 5);
        }
    }

    public class NeedsSystemTests
    {
        static EntityId SpawnWithNeeds(SimHarness h, double hunger = 0, double energyDef = 0)
        {
            var id = h.SpawnEntity();
            var needs = new NeedsData();
            needs.V[NeedAxis.Hunger] = hunger;
            needs.V[NeedAxis.EnergyDef] = energyDef;
            h.Ctx.Needs.Set(id, needs);
            return id;
        }

        [Fact]
        public void Drift_RaisesDeficits_OverGameTime()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var id = SpawnWithNeeds(h);
            h.SeedClock(hour: 10, timeScale: 3600f);    // 1 game-hour per tick

            h.Step(3);
            Assert.True(h.Ctx.Needs.TryGet(id, out var needs));
            Assert.Equal(ActivityCatalog.DriftPerHour[NeedAxis.Hunger] * 3, needs.V[NeedAxis.Hunger], 3);
        }

        [Fact]
        public void DoingAnActivity_AppliesItsDeltas()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var id = SpawnWithNeeds(h, hunger: 0.6);
            // A home meal is no longer free (Subsistence): it draws on the household
            // larder and its relief is gated on it. Give this eater a stocked pantry
            // so the meal is backed and its hunger delta lands.
            int home = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1 });
            h.Ctx.Residency.Set(id, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            h.Ctx.Larder.Set(home, 10);
            h.Ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = ActivityKind.EatHome,
                Phase = ActivityPhase.Doing,
                TargetBuilding = home,
                RemainingGameMinutes = 30,
            });
            h.SeedClock(hour: 12, timeScale: 600f);     // 10 game-minutes per tick

            h.Step(2);                                   // 20 game-minutes of eating
            Assert.True(h.Ctx.Needs.TryGet(id, out var needs));
            // -0.5 over 30 min -> about -0.33 in 20 min, plus a sliver of drift.
            Assert.InRange(needs.V[NeedAxis.Hunger], 0.2, 0.35);
            // The meal ate from the larder (started at 10).
            Assert.True(h.Ctx.Larder.Get(home) < 10);
        }

        [Fact]
        public void Sleeping_SuppressesEnergyDrift_AndRestores()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var id = SpawnWithNeeds(h, energyDef: 0.8);
            h.Ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = ActivityKind.Sleep,
                Phase = ActivityPhase.Doing,
                TargetBuilding = -1,
                RemainingGameMinutes = 480,
            });
            h.SeedClock(hour: 23, timeScale: 3600f);    // 1 game-hour per tick

            h.Step(4);                                   // 4 hours asleep
            Assert.True(h.Ctx.Needs.TryGet(id, out var needs));
            // Restores 1.0/480min = 0.125/h with drift suppressed: 0.8 - 0.5 = 0.3.
            Assert.InRange(needs.V[NeedAxis.EnergyDef], 0.25, 0.35);
        }
    }

    /// Full-day behavioral integration over real ARENA2 data. No-ops without
    /// game data, like the other Arena2-backed suites.
    public class OddWedgeDayTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        static SimHarness LoadTownAtDawn(out TownLoadResult town)
        {
            // 1s ticks at scale 60 -> 1 game-minute per tick, 1440 ticks/day.
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            var loc = maps.GetLocation("Daggerfall", "Gothway Garden");
            town = TownLoader.Load(h.Ctx, loc, blocks);
            h.SeedClock(hour: 5, minute: 30, timeScale: 60f);
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
            return h;
        }

        [Fact]
        public void MidMorning_KeepersAreMostlyAtWork()
        {
            if (!Available) return;
            var h = LoadTownAtDawn(out var town);

            h.Step(270);    // 05:30 -> 10:00

            int keepers = 0, working = 0;
            foreach (var kv in h.Ctx.Residency.All)
            {
                if (kv.Value.Role != ResidentRole.Keeper) continue;
                keepers++;
                if (h.Ctx.Behavior.TryGet(kv.Key, out var b) && b.Activity == ActivityKind.Work)
                    working++;
            }
            Assert.True(keepers > 0);
            // Some keepers are eating or socializing — that's the point of a
            // utility sim — but the workday should clearly dominate.
            Assert.True(working >= keepers * 0.4,
                "only " + working + "/" + keepers + " keepers working at 10:00");
        }

        [Fact]
        public void DeepNight_TownIsAsleep()
        {
            if (!Available) return;
            var h = LoadTownAtDawn(out var town);

            h.Step(1290);   // 05:30 -> 03:00 next day

            int asleep = 0, total = 0;
            foreach (var kv in h.Ctx.Behavior.All)
            {
                total++;
                if (kv.Value.Activity == ActivityKind.Sleep) asleep++;
            }
            Assert.Equal(town.Civilians, total);
            Assert.True(asleep >= total * 0.8, "only " + asleep + "/" + total + " asleep at 03:00");
        }

        [Fact]
        public void AFullDay_ProducesVariedLife_AndBoundedNeeds()
        {
            if (!Available) return;
            var h = LoadTownAtDawn(out var town);

            var seen = new HashSet<ActivityKind>();
            h.Ctx.Events.Subscribe<ActivityStartedEvent>(e => seen.Add(e.Activity));

            h.Step(1440);   // full game day

            Assert.True(seen.Count >= 5, "only " + seen.Count + " distinct activities all day");
            Assert.Contains(ActivityKind.Sleep, seen);
            Assert.Contains(ActivityKind.Work, seen);
            Assert.Contains(ActivityKind.Socialize, seen);

            // Needs must not run away: civilians keep themselves fed/rested.
            double maxHunger = 0, maxEnergy = 0;
            foreach (var kv in h.Ctx.Needs.All)
            {
                if (kv.Value.V[NeedAxis.Hunger] > maxHunger) maxHunger = kv.Value.V[NeedAxis.Hunger];
                if (kv.Value.V[NeedAxis.EnergyDef] > maxEnergy) maxEnergy = kv.Value.V[NeedAxis.EnergyDef];
            }
            Assert.True(maxHunger < 1.0, "worst hunger " + maxHunger);
            Assert.True(maxEnergy < 1.0, "worst tiredness " + maxEnergy);
        }

        [Fact]
        public void SameSeed_SameDay()
        {
            if (!Available) return;
            var a = LoadTownAtDawn(out var townA);
            var b = LoadTownAtDawn(out var townB);

            a.Step(400);
            b.Step(400);

            foreach (var kv in a.Needs())
            {
                Assert.True(b.Ctx.Needs.TryGet(kv.Key, out var other));
                for (int axis = 0; axis < NeedAxis.Count; axis++)
                    Assert.Equal(kv.Value.V[axis], other.V[axis], 10);
            }
        }
    }

    static class HarnessNeedsExt
    {
        public static IEnumerable<KeyValuePair<EntityId, NeedsData>> Needs(this SimHarness h) => h.Ctx.Needs.All;
    }
}
