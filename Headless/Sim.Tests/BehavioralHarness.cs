using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Sim.Tests
{
    /// Behavioral testing — we assert what the TOWN does over a real run, not
    /// how the mechanisms compute. And we leave room to evaluate: most checks
    /// are SOFT observations (wide bands, printed for judgement), because an
    /// agent may do something we didn't predict that its motivations explain
    /// perfectly ("40% awake at 3am" → look: they're starving, foraging — makes
    /// sense). Only true invariants and clear-bug floors fail the build; the
    /// soft report (with the motivations of the unexpected) is for us to read.
    ///
    /// This is the acceptance spec for the E0b decision refactor: the rewritten
    /// town must keep satisfying these — re-tuned against them, not against
    /// pinned mechanism numbers.
    public class BehavioralHarness
    {
        readonly ITestOutputHelper _out;
        public BehavioralHarness(ITestOutputHelper o) { _out = o; }

        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        const int TicksPerHour = 60;   // 1 game-min/tick at timescale 600

        [Fact]
        public void TownAgentsKnowTheirWholeTown()
        {
            if (!Available) return;

            var boot = SimBoot.CreateTown(Arena2, "Daggerfall", "Gothway Garden", 600f);
            var ctx = boot.Ctx;

            // Count real (non-wall) buildings the way TownLoader seeds them.
            int realBuildings = 0;
            foreach (var kv in ctx.Buildings.All)
                if (kv.Value.Kind != BuildingKind.None) realBuildings++;

            // A resident isn't blind to the town — they know every building in it.
            foreach (var res in ctx.Residency.All)
            {
                Assert.Equal(realBuildings, ctx.PlaceMemory.CountFor(res.Key));
                break;   // one sample is enough; the seed is uniform
            }
            Assert.True(realBuildings > 20, "town implausibly small: " + realBuildings);

            // G1: the town opens stocked — shops and taverns start with goods on
            // hand so the supply chain has something to move. At least one tavern
            // holds provisions to serve.
            int stocked = 0;
            bool tavernHasProvisions = false;
            foreach (var kv in ctx.Buildings.All)
            {
                if (GoodsCatalog.Stocks(kv.Value.Kind).Length == 0) continue;
                if (ctx.Stock.Get(kv.Key, Good.Provisions) > 0 || ctx.Stock.Get(kv.Key, Good.Wares) > 0) stocked++;
                if (kv.Value.Kind == BuildingKind.Tavern && ctx.Stock.Get(kv.Key, Good.Provisions) > 0)
                    tavernHasProvisions = true;
            }
            Assert.True(stocked > 0, "no building was stocked at load");
            Assert.True(tavernHasProvisions, "no tavern holds provisions to serve");
        }

        [Fact]
        public void TownBehavesAsExpected()
        {
            if (!Available) return;     // no game data → silently pass (matches CharityDayTests)

            var boot = SimBoot.CreateTown(Arena2, "Daggerfall", "Gothway Garden", 600f);
            var ctx = boot.Ctx;
            long decisions = 0, deaths = 0;
            ctx.Events.Subscribe<ActivityStartedEvent>(e => decisions++);
            ctx.Events.Subscribe<DeathSimEvent>(e => deaths++);

            double initialMoney = TownCensus.Capture(ctx).MoneySupply;

            var byHour = new Dictionary<int, List<CensusSnapshot>>();
            var notable = new List<string>();   // motivations of the unexpected, for evaluation

            for (int h = 0; h < 3 * 24; h++)
            {
                for (int t = 0; t < TicksPerHour; t++) boot.Loop.Step();
                int hour = ctx.WorldClock.Current.Hour;
                var snap = TownCensus.Capture(ctx);
                if (!byHour.TryGetValue(hour, out var list)) { list = new List<CensusSnapshot>(); byHour[hour] = list; }
                list.Add(snap);

                // Capture the motivations of agents doing the *surprising* thing,
                // so an off-band metric is something we can judge, not just a red
                // bar: who's up at 3am, and who's asleep at 1pm — and why?
                if (hour == 3 && snap.FractionDoing(ActivityKind.Sleep) < 0.85)
                    CaptureSome(ctx, notable, "awake @ 03:00",
                        b => b.Phase == ActivityPhase.Doing && b.Activity != ActivityKind.Sleep);
                if (hour == 13 && snap.FractionDoing(ActivityKind.Sleep) > 0.2)
                    CaptureSome(ctx, notable, "asleep @ 13:00",
                        b => b.Phase == ActivityPhase.Doing && b.Activity == ActivityKind.Sleep);
            }

            double AvgAt(int hour, Func<CensusSnapshot, double> sel)
            {
                if (!byHour.TryGetValue(hour, out var l) || l.Count == 0) return double.NaN;
                double s = 0; foreach (var x in l) s += sel(x); return s / l.Count;
            }

            var ledger = ctx.Ledger.Current;
            var final = TownCensus.Capture(ctx);

            // ---- SOFT observations: printed and flagged, never fail the build ----
            _out.WriteLine("=== behavioral report — Gothway Garden, 3-day run ===");
            Observe("asleep @ 03:00",       AvgAt(3,  s => s.FractionDoing(ActivityKind.Sleep)), 0.60, 1.00);
            Observe("asleep @ 13:00",       AvgAt(13, s => s.FractionDoing(ActivityKind.Sleep)), 0.00, 0.15);
            Observe("at tavern @ 10:00",    AvgAt(10, s => s.TavernFraction),                    0.00, 0.10);
            Observe("at tavern @ 20:00",    AvgAt(20, s => s.TavernFraction),                    0.02, 1.00);
            Observe("keepers working @ 12", AvgAt(12, s => s.KeeperWorkFraction),                0.20, 1.00);
            Observe("keepers working @ 02", AvgAt(2,  s => s.KeeperWorkFraction),                0.00, 0.10);
            Observe("friend-edges (final)", final.FriendEdges,                                   1,    double.MaxValue);
            Observe("mean familiarity",     final.MeanFamiliarity,                               0.00, 0.90);
            Observe("tavern evening>morning", AvgAt(20, s => s.TavernFraction) - AvgAt(10, s => s.TavernFraction), 0.0, double.MaxValue);

            if (notable.Count > 0)
            {
                _out.WriteLine("-- unexpected-but-maybe-sensible (judge the motivations) --");
                foreach (var s in notable) _out.WriteLine("   " + s);
            }

            // ---- HARD invariants + clear-bug floors: these fail the build ----
            Assert.Equal(initialMoney + ledger.Minted - ledger.Sunk, final.MoneySupply, 6);   // conservation (incl. treasury)
            Assert.True(decisions > 1000, "town stopped deciding — deadlock? (" + decisions + ")");
            Assert.Equal(0, (int)deaths);
            Assert.True(AvgAt(3, s => s.FractionDoing(ActivityKind.Sleep)) > 0.2,
                "almost nobody sleeps at night — almost certainly a bug");
            Assert.True(AvgAt(20, s => s.TavernFraction) > 0,
                "taverns completely empty all evening — almost certainly a bug");
        }

        // Grab a few exemplars matching a predicate and record their motivations
        // (capped, and only the first time we hit the situation — one cohort is
        // enough to judge "sensible or bug?").
        static void CaptureSome(SimulationContext ctx, List<string> sink, string label, Func<BehaviorData, bool> pred)
        {
            foreach (var kv in ctx.Behavior.All)
            {
                if (CountTagged(sink, label) >= 4) break;
                if (pred(kv.Value)) sink.Add(label + " — " + TownCensus.Explain(ctx, kv.Key));
            }
        }

        static int CountTagged(List<string> sink, string label)
        {
            int n = 0;
            foreach (var s in sink) if (s.StartsWith(label)) n++;
            return n;
        }

        void Observe(string name, double value, double lo, double hi)
        {
            string flag = value >= lo && value <= hi ? "ok" : "??";
            string hiStr = hi == double.MaxValue ? "∞" : hi.ToString("F2");
            _out.WriteLine($"   [{flag}] {name,-26} {value,7:F3}   (expect {lo:F2}–{hiStr})");
        }
    }
}
