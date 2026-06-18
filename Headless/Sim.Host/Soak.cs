using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Multi-day stability soak. Loads a real town and fast-forwards N game-days,
    /// capturing a snapshot of the sim's aggregate state each day. Prints a
    /// day-by-day table and a verdict against the questions behavior.md raised:
    /// does coin concentrate, do friendships saturate, does the social fabric
    /// reach equilibrium or keep climbing (the no-decay signature), does the
    /// economy stay solvent, do the civilians keep deciding?
    ///
    /// Everything is game-time driven (needs drift + decisions gate on game-minutes),
    /// and a tick advances a FIXED game-step (SimulationTime.TickIntervalSeconds) — so
    /// a game-day is SecondsPerDay / step ticks, DERIVED from the step, never a
    /// hardcoded count (speed comes from tick RATE, not a bigger step). At the live
    /// 0.1s step that's 864k ticks/day, so multi-day runs are tick-heavy — prefer few
    /// days for iteration.
    public static class Soak
    {
        const float SoakTimeScale = 600f;      // headless fires in a tight loop; this only needs to be > 0 (unpaused)

        public static int Run(string regionName, string locationName, int days)
        {
            SimBootResult boot;
            try { boot = SimBoot.CreateTown(DataProbe.Arena2Path, regionName, locationName, SoakTimeScale); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            var t = boot.Town;
            string title = t.RegionName + " / " + t.Name
                + " — " + t.Buildings + " structures, " + t.Civilians + " civilians";
            return RunBoot(boot, title, days, perSettlement: false);
        }

        /// Soak a whole region: every settled location in one context, reported with a
        /// regional table + verdict and a per-settlement breakdown (Stage 4).
        public static int RunRegion(string regionName, int days)
        {
            SimBootResult boot;
            try { boot = SimBoot.CreateRegion(DataProbe.Arena2Path, regionName, SoakTimeScale); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            var r = boot.Region;
            string title = r.RegionName + " — " + r.Settlements + " settlements, "
                + r.Buildings + " structures, " + r.Civilians + " civilians";
            return RunBoot(boot, title, days, perSettlement: true);
        }

        /// Run a soak on an already-booted sim (town or whole region) and report it.
        /// The day-by-day table + verdict measure the WHOLE context via TownCensus, so
        /// a region boot reports its regional aggregates; perSettlement adds the
        /// per-settlement breakdown (Stage 4: observe the model across all settlements).
        public static int RunBoot(SimBootResult boot, string title, int days, bool perSettlement)
        {
            var ctx = boot.Ctx;
            var loop = boot.Loop;
            var events = ctx.Events;

            // Cumulative counters, snapshotted and diffed per day.
            long decisions = 0, almsGranted = 0, almsRefused = 0, friendships = 0, mets = 0, deaths = 0;
            events.Subscribe<ActivityStartedEvent>(e => decisions++);
            events.Subscribe<HelpGrantedEvent>(e => almsGranted++);
            events.Subscribe<HelpRefusedEvent>(e => almsRefused++);
            events.Subscribe<FriendshipFormedEvent>(e => friendships++);
            events.Subscribe<MetSimEvent>(e => mets++);
            events.Subscribe<DeathSimEvent>(e => deaths++);

            // Ticks per game-day = SecondsPerDay / the fixed game-step. Derived, not
            // assumed — the step is invariant, so this self-corrects to whatever the
            // step is (864k/day at the live 0.1s step).
            long ticksPerDay = (long)Math.Round(DaggerfallDateTime.SecondsPerDay / ctx.Time.TickIntervalSeconds);
            long sampleEvery = (long)Math.Round(30.0 * 60.0 / ctx.Time.TickIntervalSeconds);   // hist sample: every 30 game-min
            Console.WriteLine(title + ", soaking " + days + " game-days ("
                + ((long)days * ticksPerDay) + " ticks @ " + ctx.Time.TickIntervalSeconds
                + "s fixed step → " + ticksPerDay + " ticks/day)");
            Console.WriteLine();

            var series = new List<Snapshot>();
            series.Add(Capture(ctx, 0, decisions, almsGranted, almsRefused, friendships, mets, deaths));

            // The daily snapshot lands at one fixed hour (1440 ticks = 24h), so it
            // only ever sees that slice of the day. Histogram the activity mix
            // ACROSS the final day (every 30 game-min) for the time-of-day-neutral
            // ground truth of what people do.
            int kinds = System.Enum.GetValues(typeof(ActivityKind)).Length;
            var dayHist = new long[kinds + 2];   // +Moving, +None(no behavior)
            int movingBucket = kinds, idleNoneBucket = kinds + 1;
            long histSamples = 0;

            for (int d = 1; d <= days; d++)
            {
                bool lastDay = d == days;
                if (lastDay) OddSystem.SnapshotEnabled = true;   // capture each agent's final-day ODD tree
                for (long t = 0; t < ticksPerDay; t++)
                {
                    loop.Step();
                    if (lastDay && t % sampleEvery == 0)
                    {
                        foreach (var kv in ctx.Needs.All)   // civilians only (have a Needs row)
                        {
                            if (!ctx.Behavior.TryGet(kv.Key, out var b)) { dayHist[idleNoneBucket]++; continue; }
                            if (b.Phase == ActivityPhase.Moving) dayHist[movingBucket]++;
                            else dayHist[(int)b.Activity]++;
                        }
                        histSamples++;
                    }
                }
                series.Add(Capture(ctx, d, decisions, almsGranted, almsRefused, friendships, mets, deaths));
            }

            PrintTable(series);
            PrintDayMix(dayHist, histSamples, series[series.Count - 1].Population, movingBucket, idleNoneBucket);
            Console.WriteLine();
            PrintEconomyDetail(ctx, series);
            PrintOddSnapshots(ctx);
            if (perSettlement) PrintSettlements(ctx);
            bool ok = Verdict(series);
            Console.WriteLine();
            Console.WriteLine(loop.Profile());     // where the tick spends its time (parallelization target)
            foreach (var sys in loop.Systems)
                if (sys is MovementSystem m)
                    Console.WriteLine("movement: " + m.PathfindCalls + " pathfinds / " + m.MovingAgentTicks
                        + " moving-agent-ticks = " + (m.MovingAgentTicks > 0 ? (100.0 * m.PathfindCalls / m.MovingAgentTicks).ToString("F1") : "0") + "% replan rate");
            return ok ? 0 : 1;
        }

        /// What civilians spend the FINAL DAY doing — averaged over the day (every
        /// 30 game-min), so it's not biased by the single fixed-hour daily sample.
        static void PrintDayMix(long[] hist, long samples, int pop, int movingBucket, int idleNoneBucket)
        {
            if (samples == 0) return;
            var rows = new List<(string name, double avg)>();
            for (int k = 0; k < hist.Length; k++)
            {
                if (hist[k] == 0) continue;
                string name = k == movingBucket ? "Moving"
                            : k == idleNoneBucket ? "(no behavior)"
                            : ((ActivityKind)k).ToString();
                rows.Add((name, hist[k] / (double)samples));
            }
            rows.Sort((a, b) => b.avg.CompareTo(a.avg));
            Console.WriteLine("final-day activity mix (avg headcount over the day, " + pop + " civilians):");
            var sb = new System.Text.StringBuilder("  ");
            for (int i = 0; i < rows.Count; i++)
            {
                double pct = pop > 0 ? 100.0 * rows[i].avg / pop : 0;
                sb.Append(rows[i].name).Append(' ').Append(rows[i].avg.ToString("F0"))
                  .Append(" (").Append(pct.ToString("F0")).Append("%)   ");
            }
            Console.WriteLine(sb.ToString());
            Console.WriteLine();
        }

        /// Final per-settlement economy breakdown — does each settlement behave like
        /// its solo soak (a producer-less hamlet deflates, etc.)?
        static void PrintSettlements(SimulationContext ctx)
        {
            Console.WriteLine("per-settlement (final):");
            Console.WriteLine("  kind     name                          pop  coinTot coinMean broke  treasury  pov  hung");
            foreach (var s in ctx.Settlements.All)
            {
                int pop = 0, broke = 0, needN = 0;
                double coinTot = 0, pov = 0, hung = 0;
                for (int i = 0; i < s.Residents.Count; i++)
                {
                    var id = s.Residents[i];
                    double c = ctx.Coin.Get(id);
                    coinTot += c; pop++;
                    if (c < 0.05) broke++;
                    if (ctx.Needs.TryGet(id, out var nd))
                    {
                        pov += nd.V[NeedAxis.CoinDef]; hung += nd.V[NeedAxis.Hunger]; needN++;
                    }
                }
                double mean = pop > 0 ? coinTot / pop : 0;
                if (needN > 0) { pov /= needN; hung /= needN; }
                Console.WriteLine("  "
                    + s.Kind.ToString().PadRight(8) + " "
                    + Trunc(s.Name, 28).PadRight(28) + " "
                    + pop.ToString().PadLeft(4) + " "
                    + coinTot.ToString("F1").PadLeft(7) + " "
                    + mean.ToString("F2").PadLeft(7) + " "
                    + broke.ToString().PadLeft(5) + " "
                    + ctx.Treasury.Get(s.Treasury).ToString("F1").PadLeft(8) + "  "
                    + pov.ToString("F2") + " " + hung.ToString("F2"));
            }
            Console.WriteLine();
        }

        static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        static double[] CloneOrEmpty(double[] a)
            => a == null ? new double[GoodsCatalog.Count] : (double[])a.Clone();

        /// What the economy physically MOVED over the run (goods produced / imported /
        /// exported / consumed, in units), the town's off-map wealth balance, and the
        /// profession roster — so "does this town make its food or import it?" is
        /// answerable at a glance.
        static void PrintEconomyDetail(SimulationContext ctx, List<Snapshot> series)
        {
            var last = series[series.Count - 1];

            Console.WriteLine("goods over the run (cumulative units):");
            Console.WriteLine("  good         produced  imported  exported  consumed");
            for (int g = 0; g < GoodsCatalog.Count; g++)
                Console.WriteLine("  " + ((Good)g).ToString().PadRight(11)
                    + Cell(last.Produced, g) + Cell(last.Imported, g)
                    + Cell(last.Exported, g) + Cell(last.Consumed, g));
            Console.WriteLine("  wealth: exports " + last.Exports.ToString("F2") + " coin in / imports "
                + last.Imports.ToString("F2") + " out / crown " + last.CrownSubsidy.ToString("F2")
                + " → net off-map " + (last.Exports + last.CrownSubsidy - last.Imports).ToString("F2") + " coin");
            Console.WriteLine();

            var prof = new Dictionary<string, int>();
            foreach (var kv in ctx.Residency.All)
            {
                string p = ProfessionOf(ctx, kv.Key, kv.Value);
                prof.TryGetValue(p, out var n); prof[p] = n + 1;
            }
            Console.WriteLine("professions (" + ctx.Residency.Count + " residents):");
            foreach (var e in prof.OrderByDescending(e => e.Value))
                Console.WriteLine("  " + e.Value.ToString().PadLeft(4) + "  " + e.Key);
            Console.WriteLine();
        }

        /// The "do we have snapshots of the odd trees" view: a few agents' final-day
        /// decision trees. Indented rows are steps reached only via an enabler (Buy
        /// under Work/Labor) — their discounted value (prop=) is folded up onto the
        /// parent, which is the lookahead the depth-1 decider lacked. Prefers trees
        /// that actually exercise a chain so the propagation is visible.
        static void PrintOddSnapshots(SimulationContext ctx, int max = 6)
        {
            var snaps = OddSystem.Snapshots;
            if (snaps.IsEmpty) return;
            string Prof(EntityId id) => ctx.Residency.TryGet(id, out var r) ? ProfessionOf(ctx, id, r) : "?";
            var chained = new List<KeyValuePair<EntityId, string>>();
            var flat = new List<KeyValuePair<EntityId, string>>();
            foreach (var kv in snaps)
                (kv.Value.Contains("\n  ") ? chained : flat).Add(kv);
            Console.WriteLine("ODD decision trees (final-day snapshots; " + chained.Count
                + " of " + snaps.Count + " agents had a chain to traverse):");
            int shown = 0;
            // Hands first — they're the workforce the chain is meant to mobilise (the
            // keepers chain trivially at their own shop; the question is the laborers).
            foreach (var kv in chained.OrderBy(k => Prof(k.Key).Contains("hand") ? 0 : 1).ThenBy(k => k.Key.Value)
                                      .Concat(flat.OrderBy(k => k.Key.Value)))
            {
                if (shown++ >= max) break;
                Console.WriteLine("  [" + kv.Key.Value + "] " + Prof(kv.Key) + ":");
                foreach (var line in kv.Value.TrimEnd('\n').Split('\n'))
                    Console.WriteLine("      " + line);
            }
            Console.WriteLine();
        }

        static string Cell(double[] a, int g)
            => (a != null && g < a.Length ? a[g] : 0).ToString("F1").PadLeft(10);

        static string ProfessionOf(SimulationContext ctx, EntityId id, ResidencyData res)
        {
            if (res.Role == ResidentRole.Keeper) return KindOf(ctx, res.BuildingIndex) + " keeper";
            if (ctx.Employment.TryGet(id, out var emp) && emp != null)
            {
                if (!emp.PublicOwner.IsNone) return "guard";
                if (!emp.Employer.IsNone && ctx.Residency.TryGet(emp.Employer, out var er))
                    return KindOf(ctx, er.BuildingIndex) + " hand";
            }
            return "idle resident";
        }

        static string KindOf(SimulationContext ctx, int building)
            => ctx.Buildings.TryGet(building, out var b) && b != null ? b.Kind.ToString() : "?";

        struct Snapshot
        {
            public int Day;
            public int Population;
            public long Deaths;
            // coin
            public double CoinTotal, CoinMax, CoinMean, Gini;
            public int Broke;                       // coin < 0.05
            public double Minted, Sunk, Imports, SalesRevenue, ServiceRevenue, Wholesale;   // faucet/sink/imports/B2C/services/B2B
            public double Taxes, GuardPay, Treasury;   // E3: tax collected / guard payroll / treasury balance
            public double Exports, CrownSubsidy;        // G6 faucets: export income / crown remittance
            public double MoneySupply;                 // private purses + treasury (conservation holds on this)
            // goods on shelves (instantaneous total stock by good)
            public double StockProvisions, StockDrink, StockWares, StockOre;
            public double LarderProvisions;         // food in household larders (subsistence buffer)
            public int LarderHouseholds;            // home larders that exist
            public int LarderEmpty;                 // home larders out of food (can't eat in)
            // needs (population average deficit per axis)
            public double Hunger, Energy, Social, Poverty;
            public int Starving;                    // any axis ≥ 1.4 (near VMax 1.5)
            public int Hungry;                      // hunger ≥ 1.0 — the "can they eat?" headline
            public int Creatures;                   // V2a: hostiles roaming
            public int Fleeing, Fighting;           // V2b: Doing Flee / Attack
            public int[] Doing;                     // full activity mix at this sample (by ActivityKind)
            // social fabric
            public long Edges, Acquaintances, FriendEdges;
            public double MeanRegard, MeanFamiliarity;
            public long PosRegard, NegRegard, SaturatedRegard;   // |regard| ≥ 0.95
            // memory
            public double MeanMemory; public int MemoryFull;     // at MaxEntries
            // cumulative activity counters (diffed for per-day rates)
            public long Decisions, AlmsGranted, AlmsRefused, Friendships, Mets;
            // cumulative goods-flow audit (units, per Good) — what the economy moved
            public double[] Produced, Imported, Exported, Consumed;
        }

        static Snapshot Capture(SimulationContext ctx, int day,
            long decisions, long almsGranted, long almsRefused, long friendships, long mets, long deaths)
        {
            // Same measurement layer the behavioral tests assert against — so a
            // soak line and a test expectation mean exactly the same thing.
            var c = TownCensus.Capture(ctx);
            var led = ctx.Ledger.Current;
            return new Snapshot
            {
                Day = day, Deaths = deaths,
                Decisions = decisions, AlmsGranted = almsGranted, AlmsRefused = almsRefused,
                Friendships = friendships, Mets = mets,
                Population = c.Population,
                CoinTotal = c.CoinTotal, CoinMax = c.CoinMax, CoinMean = c.CoinMean, Gini = c.Gini, Broke = c.Broke,
                Minted = c.Minted, Sunk = c.Sunk, Imports = c.Imports,
                SalesRevenue = c.SalesRevenue, ServiceRevenue = c.ServiceRevenue, Wholesale = c.Wholesale,
                Taxes = c.Taxes, GuardPay = c.GuardPay, Treasury = c.Treasury, MoneySupply = c.MoneySupply,
                Exports = c.Exports, CrownSubsidy = c.CrownSubsidy,
                StockProvisions = c.StockProvisions, StockDrink = c.StockDrink, StockWares = c.StockWares, StockOre = c.StockOre,
                LarderProvisions = c.LarderProvisions, LarderHouseholds = c.LarderHouseholds, LarderEmpty = c.LarderEmpty,
                Hunger = c.Hunger, Energy = c.Energy, Social = c.Social, Poverty = c.Poverty,
                Starving = c.Starving, Hungry = c.Hungry,
                Creatures = c.Creatures, Fleeing = c.Fleeing, Fighting = c.Fighting, Doing = c.Doing,
                Edges = c.Edges, Acquaintances = c.Acquaintances, FriendEdges = c.FriendEdges,
                MeanRegard = c.MeanRegard, MeanFamiliarity = c.MeanFamiliarity,
                PosRegard = c.PosRegard, NegRegard = c.NegRegard, SaturatedRegard = c.SaturatedRegard,
                MeanMemory = c.MeanMemory, MemoryFull = c.MemoryFull,
                Produced = CloneOrEmpty(led.Produced), Imported = CloneOrEmpty(led.Imported),
                Exported = CloneOrEmpty(led.Exported), Consumed = CloneOrEmpty(led.Consumed),
            };
        }

        static void PrintTable(List<Snapshot> series)
        {
            Console.WriteLine("day-by-day (per-day rates in the right block are deltas from the prior day):");
            Console.WriteLine(
                "day  pop  coinTot coinMax  gini broke | mint  sunk   imp | prov drnk ware lard✗ | hung engy soc  pov starv hungry | "
                + "edges  acq  friends mReg mFam sat | decis alms+ alms- newfr | mem");
            for (int i = 0; i < series.Count; i++)
            {
                var s = series[i];
                var p = i > 0 ? series[i - 1] : s;
                Console.WriteLine(
                    s.Day.ToString().PadLeft(3) + "  "
                    + s.Population.ToString().PadLeft(3) + "  "
                    + s.CoinTotal.ToString("F1").PadLeft(7) + " "
                    + s.CoinMax.ToString("F2").PadLeft(6) + "  "
                    + s.Gini.ToString("F2") + " "
                    + s.Broke.ToString().PadLeft(4) + " | "
                    + (s.Minted - p.Minted).ToString("F2").PadLeft(5) + " "
                    + (s.Sunk - p.Sunk).ToString("F2").PadLeft(5) + " "
                    + (s.Imports - p.Imports).ToString("F2").PadLeft(5) + " | "
                    + s.StockProvisions.ToString("F0").PadLeft(4) + " "
                    + s.StockDrink.ToString("F0").PadLeft(4) + " "
                    + s.StockWares.ToString("F0").PadLeft(4) + " "
                    + s.LarderEmpty.ToString().PadLeft(4) + " | "
                    + s.Hunger.ToString("F2") + " " + s.Energy.ToString("F2") + " "
                    + s.Social.ToString("F2") + " " + s.Poverty.ToString("F2") + " "
                    + s.Starving.ToString().PadLeft(4) + " "
                    + s.Hungry.ToString().PadLeft(6) + " | "
                    + s.Edges.ToString().PadLeft(5) + " "
                    + s.Acquaintances.ToString().PadLeft(5) + " "
                    + s.FriendEdges.ToString().PadLeft(6) + " "
                    + s.MeanRegard.ToString("F2").PadLeft(5) + " "
                    + s.MeanFamiliarity.ToString("F2") + " "
                    + s.SaturatedRegard.ToString().PadLeft(4) + " | "
                    + (s.Decisions - p.Decisions).ToString().PadLeft(5) + " "
                    + (s.AlmsGranted - p.AlmsGranted).ToString().PadLeft(4) + " "
                    + (s.AlmsRefused - p.AlmsRefused).ToString().PadLeft(5) + " "
                    + (s.Friendships - p.Friendships).ToString().PadLeft(5) + " | "
                    + s.MeanMemory.ToString("F1"));
            }
        }

        /// Structural invariants fail the run (return false); "shape"
        /// observations (concentration, saturation) only narrate — some are the
        /// very gaps the soak exists to surface.
        static bool Verdict(List<Snapshot> series)
        {
            var first = series[0];
            var last = series[series.Count - 1];
            bool healthy = true;

            Console.WriteLine("verdict:");

            // 1. Economy stabilizes. A one-time wealth change is legitimate (the
            //    underclass earning its way up off destitution); what must NOT
            //    happen is unbounded drift. So check the SECOND HALF is steady,
            //    not that the total stayed near its (destitute) starting point.
            var half = series[series.Count / 2];
            double lateDrift = half.MoneySupply > 0 ? System.Math.Abs(last.MoneySupply - half.MoneySupply) / half.MoneySupply : 0;
            double overall = first.MoneySupply > 0 ? last.MoneySupply / first.MoneySupply : 0;
            if (lateDrift > 0.25)
            { healthy = false; Console.WriteLine("  [FAIL] money supply still drifting " + (lateDrift * 100).ToString("F0")
                + "% in the 2nd half (" + half.MoneySupply.ToString("F1") + " → " + last.MoneySupply.ToString("F1") + ") — not stabilizing"); }
            else Console.WriteLine("  [ok]   economy stabilizes: 2nd-half drift " + (lateDrift * 100).ToString("F0")
                + "% (overall " + overall.ToString("F2") + "× from start)");

            // 2. Liveness — civilians must keep deciding every day.
            long minDay = long.MaxValue;
            for (int i = 1; i < series.Count; i++) minDay = Math.Min(minDay, series[i].Decisions - series[i - 1].Decisions);
            if (minDay <= 0) { healthy = false; Console.WriteLine("  [FAIL] a day with 0 decisions — sim reached a fixed point / deadlock"); }
            else Console.WriteLine("  [ok]   liveness: ≥ " + minDay + " decisions on the quietest day");

            // 3. Turnover, not mass death. Natural death of old age is expected
            //    now (L2 lifecycles), so the invariant is that the population
            //    doesn't COLLAPSE — repopulation should hold it. A large drop
            //    means a mortality runaway or a broken backfill, not normal aging.
            if (first.Population > 0 && last.Population < first.Population / 2)
            {
                healthy = false;
                Console.WriteLine("  [FAIL] population collapsed " + first.Population + " → " + last.Population
                    + " over " + last.Deaths + " deaths — repopulation not holding");
            }
            else
                Console.WriteLine("  [obs]  turnover: " + last.Deaths + " deaths, population "
                    + first.Population + " → " + last.Population);

            if (last.Starving > 0)
                Console.WriteLine("  [warn] " + last.Starving + " civilians with a need pegged near VMax at last sample");
            else
                Console.WriteLine("  [ok]   no pegged needs at sample");

            // Can they eat? The subsistence headline that replaces the meaningless
            // broke%. Hunger is now backed by real provisions (larder), so a famine
            // is the EXPECTED signal of the valve flip — surfaced, never failed
            // (tuning of wages/yields/prices is a separate pass; see subsistence.md).
            {
                double hungryPct = last.Population > 0 ? 100.0 * last.Hungry / last.Population : 0;
                double emptyPct = last.LarderHouseholds > 0 ? 100.0 * last.LarderEmpty / last.LarderHouseholds : 0;
                Console.WriteLine("  [obs]  can-they-eat: " + last.Hungry + "/" + last.Population
                    + " hungry (h≥1.0, " + hungryPct.ToString("F0") + "%), "
                    + last.LarderEmpty + "/" + last.LarderHouseholds + " larders empty ("
                    + emptyPct.ToString("F0") + "%), mean hunger " + last.Hunger.ToString("F2"));
            }

            Console.WriteLine("  [obs]  threat layer: " + last.Creatures + " hostile(s) roaming, "
                + last.Deaths + " deaths total; " + last.Fleeing + " fleeing / " + last.Fighting
                + " fighting at last sample (fear → fight-or-flight, V2b)");

            // What is everyone actually DOING right now? The activity mix at the
            // last sample, busiest first — the ground truth behind the aggregates.
            if (last.Doing != null)
            {
                var order = new List<int>();
                for (int k = 0; k < last.Doing.Length; k++) if (last.Doing[k] > 0) order.Add(k);
                order.Sort((a, b) => last.Doing[b].CompareTo(last.Doing[a]));
                var sb = new System.Text.StringBuilder("  [obs]  doing now:");
                for (int i = 0; i < order.Count; i++)
                {
                    int k = order[i];
                    double pct = last.Population > 0 ? 100.0 * last.Doing[k] / last.Population : 0;
                    sb.Append(' ').Append((ActivityKind)k).Append('=').Append(last.Doing[k])
                      .Append('(').Append(pct.ToString("F0")).Append("%)");
                }
                Console.WriteLine(sb.ToString());
            }

            // 4. NaN guard.
            if (double.IsNaN(last.CoinTotal) || double.IsNaN(last.MeanRegard) || double.IsNaN(last.Hunger))
            { healthy = false; Console.WriteLine("  [FAIL] NaN in an aggregate"); }

            // 4b. Money supply audit — conservation must hold, and the faucet
            //     vs sink balance explains the coin-total drift.
            double accounted = first.MoneySupply + (last.Minted - first.Minted) - (last.Sunk - first.Sunk);
            if (System.Math.Abs(accounted - last.MoneySupply) > 1e-6)
            { healthy = false; Console.WriteLine("  [FAIL] ledger doesn't conserve: money " + last.MoneySupply.ToString("F3")
                + " vs accounted " + accounted.ToString("F3")); }
            else
            {
                int days = last.Day - first.Day;
                double mintPerDay = days > 0 ? (last.Minted - first.Minted) / days : 0;
                double sunkPerDay = days > 0 ? (last.Sunk - first.Sunk) / days : 0;
                double importPerDay = days > 0 ? (last.Imports - first.Imports) / days : 0;
                double salesPerDay = days > 0 ? (last.SalesRevenue - first.SalesRevenue) / days : 0;
                double exportPerDay = days > 0 ? (last.Exports - first.Exports) / days : 0;
                double crownPerDay = days > 0 ? (last.CrownSubsidy - first.CrownSubsidy) / days : 0;
                Console.WriteLine("  [ok]   ledger conserves; faucet " + mintPerDay.ToString("F2")
                    + "/day (exports " + exportPerDay.ToString("F2") + " + crown " + crownPerDay.ToString("F2")
                    + ") vs sink " + sunkPerDay.ToString("F2") + "/day (imports off-map "
                    + importPerDay.ToString("F2") + ") → net " + (mintPerDay - sunkPerDay).ToString("F2") + "/day");
                double b2bPerDay = days > 0 ? (last.Wholesale - first.Wholesale) / days : 0;
                double svcPerDay = days > 0 ? (last.ServiceRevenue - first.ServiceRevenue) / days : 0;
                double taxPerDay = days > 0 ? (last.Taxes - first.Taxes) / days : 0;
                double guardPerDay = days > 0 ? (last.GuardPay - first.GuardPay) / days : 0;
                Console.WriteLine("         B2C goods " + salesPerDay.ToString("F2") + "/day, services (temple/guild/bank) "
                    + svcPerDay.ToString("F2") + "/day → keepers; B2B wholesale " + b2bPerDay.ToString("F2") + "/day");
                Console.WriteLine("         public sector: tax " + taxPerDay.ToString("F2") + "/day → treasury, guard payroll "
                    + guardPerDay.ToString("F2") + "/day → guards; treasury now " + last.Treasury.ToString("F1"));
            }

            // 4c. Goods supply (shape) — is the chain producing/importing stock,
            //     and is consumption drawing it back down?
            Console.WriteLine("  [obs]  goods on shelves: provisions " + last.StockProvisions.ToString("F0")
                + ", drink " + last.StockDrink.ToString("F0") + ", wares " + last.StockWares.ToString("F0")
                + ", ore " + last.StockOre.ToString("F0"));
            Console.WriteLine("  [obs]  food in larders: provisions " + last.LarderProvisions.ToString("F0")
                + " across " + last.LarderHouseholds + " households");

            // 5. Coin concentration (shape, not fail).
            Console.WriteLine("  [obs]  wealth: Gini " + first.Gini.ToString("F2") + " → " + last.Gini.ToString("F2")
                + ", richest/mean " + (last.CoinMean > 0 ? (last.CoinMax / last.CoinMean) : 0).ToString("F1") + "×, "
                + last.Broke + " broke");

            // 6. Social fabric shape — climbing, plateaued, or cooling? L1 added
            //    the DecayTowardBaseline satisfaction-model (SocialSystem.DecayRelations),
            //    so the fabric reaches a dynamic equilibrium rather than saturating;
            //    second-half change can now be negative, so classify by sign (not |·|).
            var mid = series[series.Count / 2];
            double regardLateGrowth = last.MeanRegard - mid.MeanRegard;
            long friendsLate = last.FriendEdges - mid.FriendEdges;
            string shape;
            if (regardLateGrowth > 0.01 || friendsLate > 0)
                shape = "  → still climbing";
            else if (regardLateGrowth < -0.01 || friendsLate < 0)
                shape = "  → cooling toward baseline (decay outpacing contact in the 2nd half)";
            else
                shape = "  → plateaued (dynamic equilibrium)";
            Console.WriteLine("  [obs]  social: " + last.FriendEdges + " friend-edges, mean regard "
                + last.MeanRegard.ToString("F2") + ", " + last.SaturatedRegard + " saturated (|regard|≥0.95)");
            Console.WriteLine("         second-half growth: regard " + regardLateGrowth.ToString("+0.000;-0.000")
                + ", friend-edges " + friendsLate.ToString("+0;-0") + shape);

            Console.WriteLine();
            Console.WriteLine(healthy ? "SOAK PASSED (structural invariants held)" : "SOAK FAILED (a structural invariant broke)");
            return healthy;
        }
    }
}
