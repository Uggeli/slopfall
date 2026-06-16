using System;
using System.Collections.Generic;
using System.Linq;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Multi-day stability soak. Loads a real town and fast-forwards a whole
    /// week (or N days) at 1 game-minute per tick, capturing a snapshot of the
    /// sim's aggregate state at every midnight-to-dawn boundary. Prints a
    /// day-by-day table and a verdict against the questions behavior.md raised:
    /// does coin concentrate, do friendships saturate, does the social fabric
    /// reach equilibrium or keep climbing (the no-decay signature), does the
    /// economy stay solvent, do the civilians keep deciding?
    ///
    /// Everything in the sim is game-time driven (needs drift and decisions are
    /// gated on game-minutes, not ticks), so one day is exactly 1440 ticks at
    /// timescale 600 and the sample phase is identical every day.
    public static class Soak
    {
        const int TicksPerDay = 1440;          // 1 game-min/tick × 24 × 60
        const float SoakTimeScale = 600f;      // 0.1s tick × 600 = 60 game-s = 1 game-min/tick

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

            Console.WriteLine(title + ", soaking "
                + days + " game-days (" + (days * TicksPerDay) + " ticks @ 1 game-min/tick)");
            Console.WriteLine();

            var series = new List<Snapshot>();
            series.Add(Capture(ctx, 0, decisions, almsGranted, almsRefused, friendships, mets, deaths));

            for (int d = 1; d <= days; d++)
            {
                for (int t = 0; t < TicksPerDay; t++)
                    loop.Step();
                series.Add(Capture(ctx, d, decisions, almsGranted, almsRefused, friendships, mets, deaths));
            }

            PrintTable(series);
            Console.WriteLine();
            if (perSettlement) PrintSettlements(ctx);
            return Verdict(series) ? 0 : 1;
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
            public double StockProvisions, StockDrink, StockWares;
            // needs (population average deficit per axis)
            public double Hunger, Energy, Social, Poverty;
            public int Starving;                    // any axis ≥ 1.4 (near VMax 1.5)
            // social fabric
            public long Edges, Acquaintances, FriendEdges;
            public double MeanRegard, MeanFamiliarity;
            public long PosRegard, NegRegard, SaturatedRegard;   // |regard| ≥ 0.95
            // memory
            public double MeanMemory; public int MemoryFull;     // at MaxEntries
            // cumulative activity counters (diffed for per-day rates)
            public long Decisions, AlmsGranted, AlmsRefused, Friendships, Mets;
        }

        static Snapshot Capture(SimulationContext ctx, int day,
            long decisions, long almsGranted, long almsRefused, long friendships, long mets, long deaths)
        {
            // Same measurement layer the behavioral tests assert against — so a
            // soak line and a test expectation mean exactly the same thing.
            var c = TownCensus.Capture(ctx);
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
                StockProvisions = c.StockProvisions, StockDrink = c.StockDrink, StockWares = c.StockWares,
                Hunger = c.Hunger, Energy = c.Energy, Social = c.Social, Poverty = c.Poverty, Starving = c.Starving,
                Edges = c.Edges, Acquaintances = c.Acquaintances, FriendEdges = c.FriendEdges,
                MeanRegard = c.MeanRegard, MeanFamiliarity = c.MeanFamiliarity,
                PosRegard = c.PosRegard, NegRegard = c.NegRegard, SaturatedRegard = c.SaturatedRegard,
                MeanMemory = c.MeanMemory, MemoryFull = c.MemoryFull,
            };
        }

        static void PrintTable(List<Snapshot> series)
        {
            Console.WriteLine("day-by-day (per-day rates in the right block are deltas from the prior day):");
            Console.WriteLine(
                "day  pop  coinTot coinMax  gini broke | mint  sunk   imp | prov drnk ware | hung engy soc  pov starv | "
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
                    + s.StockWares.ToString("F0").PadLeft(4) + " | "
                    + s.Hunger.ToString("F2") + " " + s.Energy.ToString("F2") + " "
                    + s.Social.ToString("F2") + " " + s.Poverty.ToString("F2") + " "
                    + s.Starving.ToString().PadLeft(4) + " | "
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

            // 3. No mass death / starvation.
            if (last.Deaths > 0) { healthy = false; Console.WriteLine("  [FAIL] " + last.Deaths + " deaths"); }
            else if (last.Starving > 0) Console.WriteLine("  [warn] " + last.Starving + " civilians with a need pegged near VMax at last sample");
            else Console.WriteLine("  [ok]   no deaths, no pegged needs at sample");

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
                + ", drink " + last.StockDrink.ToString("F0") + ", wares " + last.StockWares.ToString("F0"));

            // 5. Coin concentration (shape, not fail).
            Console.WriteLine("  [obs]  wealth: Gini " + first.Gini.ToString("F2") + " → " + last.Gini.ToString("F2")
                + ", richest/mean " + (last.CoinMean > 0 ? (last.CoinMax / last.CoinMean) : 0).ToString("F1") + "×, "
                + last.Broke + " broke");

            // 6. Social saturation (shape) — is the fabric still climbing at the end?
            //    No decay term on regard/familiarity means we expect monotonic growth → no equilibrium.
            var mid = series[series.Count / 2];
            double regardLateGrowth = last.MeanRegard - mid.MeanRegard;
            long friendsLate = last.FriendEdges - mid.FriendEdges;
            Console.WriteLine("  [obs]  social: " + last.FriendEdges + " friend-edges, mean regard "
                + last.MeanRegard.ToString("F2") + ", " + last.SaturatedRegard + " saturated (|regard|≥0.95)");
            Console.WriteLine("         second-half growth: regard +" + regardLateGrowth.ToString("F3")
                + ", friend-edges +" + friendsLate
                + (Math.Abs(regardLateGrowth) > 0.01 || friendsLate > 0
                    ? "  → still climbing, no equilibrium (no decay term — Atoms: add a satisfaction-model)"
                    : "  → plateaued"));

            Console.WriteLine();
            Console.WriteLine(healthy ? "SOAK PASSED (structural invariants held)" : "SOAK FAILED (a structural invariant broke)");
            return healthy;
        }
    }
}
