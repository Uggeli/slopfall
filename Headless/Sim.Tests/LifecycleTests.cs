using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// L2.1 — the mortality hazard shape (pure algorithm, no town needed).
    public class MortalityTests
    {
        [Fact]
        public void MortalityHazard_NegligibleYoung_RisesToCertain()
        {
            Assert.True(AgingSystem.MortalityHazard(20, 70) < 0.01, "young hazard too high");
            Assert.True(AgingSystem.MortalityHazard(70, 70) > 0.05, "at-lifespan hazard too low");
            Assert.True(AgingSystem.MortalityHazard(20, 70) < AgingSystem.MortalityHazard(70, 70), "not monotone");
            Assert.Equal(1.0, AgingSystem.MortalityHazard(200, 70), 6);   // well past lifespan → certain
            Assert.Equal(1.0, AgingSystem.MortalityHazard(50, 0), 6);     // degenerate lifespan → certain
        }
    }

    /// L2 gates in the full town (requires ARENA2 — skipped if absent, like the
    /// other town tests). Qualitative direction only: a civilian dies of age, is
    /// despawned (rows gone + off the roster), and money is conserved across the
    /// deaths via escheat. No tuned magnitudes.
    public class LifecycleTownTests
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
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
            return h;
        }

        static double Supply(SimulationContext ctx)
        {
            double s = 0;
            foreach (var kv in ctx.Coin.All) s += kv.Value;
            s += ctx.Treasury.Total;
            return s;
        }

        [Fact]
        public void AgingDeath_Despawns_Conserves_AndRepopulates()
        {
            if (!Available) return;
            var h = LoadTown();
            var despawned = h.Collect<DespawnedEvent>();

            // A doomed resident — hazard 1.0, certain death at the next year tick.
            var victim = h.Ctx.Settlements.All[0].Residents[0];
            Assert.True(h.Ctx.Identity.TryGet(victim, out _), "victim not present at load");
            Assert.True(h.Ctx.Residency.TryGet(victim, out var vres), "victim has no residency");
            int vBuilding = vres.BuildingIndex;
            var vRole = vres.Role;
            int popBefore = h.Ctx.Settlements.All[0].Residents.Count;
            h.Ctx.Life.Set(victim, new LifeData { AgeYears = 500, LifespanYears = 50 });

            // Seed the clock near year's end so a short run crosses 405 → 406.
            h.SeedClock(year: 405, month: 11, day: 27, hour: 0, minute: 0, timeScale: 600f);

            double supplyBefore = Supply(h.Ctx);
            var l0 = h.Ctx.Ledger.Current;
            double mint0 = l0.Minted, sunk0 = l0.Sunk;

            h.Step(700);   // ~4.8 game-days at 10 game-min/tick → crosses the year + the death→despawn pipeline

            Assert.True(h.Ctx.WorldClock.Current.Year >= 406, "year did not roll — test inconclusive");

            // Despawned: every per-entity row gone.
            Assert.False(h.Ctx.Identity.TryGet(victim, out _), "Identity not removed");
            Assert.False(h.Ctx.Residency.TryGet(victim, out _), "Residency not removed");
            Assert.False(h.Ctx.Life.TryGet(victim, out _), "Life not removed");
            Assert.False(h.Ctx.Coin.TryGet(victim, out _), "Coin not removed");
            Assert.False(h.Ctx.Needs.TryGet(victim, out _), "Needs not removed");

            // Off the settlement roster, and a DespawnedEvent fired for it.
            foreach (var s in h.Ctx.Settlements.All)
                Assert.DoesNotContain(victim, s.Residents);
            Assert.Contains(despawned, e => e.Entity == victim);

            // Conservation across all the deaths this crossing: supply moves only
            // by faucets/sinks — escheat (purse → treasury) is within-supply.
            double supplyAfter = Supply(h.Ctx);
            var l1 = h.Ctx.Ledger.Current;
            double predicted = supplyBefore + (l1.Minted - mint0) - (l1.Sunk - sunk0);
            Assert.True(Math.Abs(supplyAfter - predicted) < 0.01,
                "money not conserved across deaths: after " + supplyAfter.ToString("F4")
                + " vs predicted " + predicted.ToString("F4"));

            // L2.4: the vacated (building, role) slot is backfilled by an
            // immigrant (a different id), and headcount is held 1:1.
            bool restaffed = false;
            foreach (var kv in h.Ctx.Residency.All)
                if (kv.Key != victim && kv.Value.BuildingIndex == vBuilding && kv.Value.Role == vRole)
                { restaffed = true; break; }
            Assert.True(restaffed, "vacated slot not re-staffed");
            Assert.Equal(popBefore, h.Ctx.Settlements.All[0].Residents.Count);
        }
    }
}
