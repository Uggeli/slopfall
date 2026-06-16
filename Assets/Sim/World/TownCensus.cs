using System.Collections.Generic;
using System.Text;

namespace DaggerfallWorkshop.Sim
{
    /// Read-only aggregate view over the sim's registries — one place that
    /// turns "what is the whole town doing right now" into numbers. The soak
    /// reports it, the behavioral tests assert against it, the spectator could
    /// draw it: everyone measures the town the same way, so a behavioral
    /// expectation in a test means exactly what the soak's daily line means.
    ///
    /// Instantaneous only (derived from current registry state). Run-level
    /// accumulators — decisions, alms, deaths — are counted from events by
    /// whoever owns the run, not here.
    public struct CensusSnapshot
    {
        public int Population;                 // acting civilians (Needs rows)

        // Activity mix — count of civilians in each activity, Doing phase.
        public int[] Doing;                    // indexed by (int)ActivityKind
        public int Keepers, KeepersWorking;

        // Coin
        public double CoinTotal, CoinMax, CoinMean, Gini;
        public int Broke;                      // coin < 0.05
        public double Minted, Sunk, Imports, SalesRevenue, ServiceRevenue, Wholesale, Taxes, GuardPay;   // LedgerRegistry
        public double Exports, CrownSubsidy;   // faucets: off-map export income / crown remittance (G6)
        public double Treasury;                // public coin held by the Town (E3)

        /// Total money supply = private purses + the treasury; conservation holds
        /// on THIS (entity coin alone leaks into the treasury via tax).
        public double MoneySupply => CoinTotal + Treasury;

        // Goods on shelves (total stock across all buildings, by good)
        public double StockProvisions, StockDrink, StockWares, StockOre;

        // Needs (population-average deficit per axis)
        public double Hunger, Energy, Social, Poverty;
        public int Starving;                   // any axis ≥ 1.4 (near VMax 1.5)

        // Social fabric
        public long Edges, Acquaintances, FriendEdges;
        public double MeanRegard, MeanFamiliarity;
        public long PosRegard, NegRegard, SaturatedRegard;   // |regard| ≥ 0.95

        // Memory
        public double MeanMemory;
        public int MemoryFull;                 // rings at MaxEntries

        public double FractionDoing(ActivityKind kind)
            => Population > 0 ? Doing[(int)kind] / (double)Population : 0;

        public double TavernFraction
            => Population > 0 ? (Doing[(int)ActivityKind.EatTavern] + Doing[(int)ActivityKind.Socialize]) / (double)Population : 0;

        public double KeeperWorkFraction
            => Keepers > 0 ? KeepersWorking / (double)Keepers : 0;
    }

    public static class TownCensus
    {
        public static CensusSnapshot Capture(SimulationContext ctx)
        {
            var s = new CensusSnapshot { Doing = new int[(int)ActivityKind.Beg + 1] };

            // --- Activity mix ---
            foreach (var kv in ctx.Behavior.All)
                if (kv.Value.Phase == ActivityPhase.Doing)
                    s.Doing[(int)kv.Value.Activity]++;

            foreach (var kv in ctx.Residency.All)
            {
                if (kv.Value.Role != ResidentRole.Keeper) continue;
                s.Keepers++;
                if (ctx.Behavior.TryGet(kv.Key, out var b)
                    && b.Phase == ActivityPhase.Doing && b.Activity == ActivityKind.Work)
                    s.KeepersWorking++;
            }

            // --- Coin ---
            int coinCount = 0;
            var coins = new List<double>();
            foreach (var kv in ctx.Coin.All)
            {
                double c = kv.Value;
                s.CoinTotal += c;
                if (c > s.CoinMax) s.CoinMax = c;
                if (c < 0.05) s.Broke++;
                coins.Add(c);
                coinCount++;
            }
            if (coinCount > 0) s.CoinMean = s.CoinTotal / coinCount;
            s.Gini = GiniOf(coins);

            var ledger = ctx.Ledger.Current;
            s.Minted = ledger.Minted;
            s.Sunk = ledger.Sunk;
            s.Imports = ledger.Imports;
            s.SalesRevenue = ledger.SalesRevenue;
            s.ServiceRevenue = ledger.ServiceRevenue;
            s.Wholesale = ledger.Wholesale;
            s.Taxes = ledger.Taxes;
            s.GuardPay = ledger.GuardPay;
            s.Exports = ledger.Exports;
            s.CrownSubsidy = ledger.CrownSubsidy;
            s.Treasury = ctx.Treasury.Total;

            // --- Goods on shelves ---
            foreach (var kv in ctx.Stock.All)
            {
                var g = kv.Value;
                s.StockProvisions += g[(int)Good.Provisions];
                s.StockDrink += g[(int)Good.Drink];
                s.StockWares += g[(int)Good.Wares];
                s.StockOre += g[(int)Good.Ore];
            }

            // --- Needs ---
            int n = 0;
            foreach (var kv in ctx.Needs.All)
            {
                var v = kv.Value.V;
                s.Hunger += v[NeedAxis.Hunger]; s.Energy += v[NeedAxis.EnergyDef];
                s.Social += v[NeedAxis.SocialDef]; s.Poverty += v[NeedAxis.CoinDef];
                for (int a = 0; a < NeedAxis.Count; a++) if (v[a] >= 1.4) { s.Starving++; break; }
                n++;
            }
            if (n > 0) { s.Hunger /= n; s.Energy /= n; s.Social /= n; s.Poverty /= n; }
            s.Population = n;

            // --- Social fabric ---
            double regardSum = 0, famSum = 0;
            foreach (var kv in ctx.Relations.All)
            {
                foreach (var rel in kv.Value.Of.Values)
                {
                    s.Edges++;
                    regardSum += rel.Regard; famSum += rel.Familiarity;
                    if (rel.Familiarity >= 0.05) s.Acquaintances++;
                    if (rel.FriendAnnounced) s.FriendEdges++;
                    if (rel.Regard > 0.01) s.PosRegard++;
                    else if (rel.Regard < -0.01) s.NegRegard++;
                    if (System.Math.Abs(rel.Regard) >= 0.95) s.SaturatedRegard++;
                }
            }
            if (s.Edges > 0) { s.MeanRegard = regardSum / s.Edges; s.MeanFamiliarity = famSum / s.Edges; }

            // --- Memory ---
            int m = 0; double memSum = 0;
            foreach (var kv in ctx.Memory.All)
            {
                memSum += kv.Value.Entries.Count;
                if (kv.Value.Entries.Count >= MemoryRegistry.MaxEntries) s.MemoryFull++;
                m++;
            }
            if (m > 0) s.MeanMemory = memSum / m;

            return s;
        }

        static readonly string[] AxisNames = { "hunger", "energy", "social", "coin", "goods" };

        /// One agent's motivations in a line — so a surprising metric can be
        /// judged ("40% awake at 3am" → look: they're hungry, foraging, makes
        /// sense) instead of just failing a threshold. The decision *rationale*
        /// (the ads considered and their scores) joins this once
        /// DeliberationSystem retains it; for now it's drives + current act.
        public static string Explain(SimulationContext ctx, EntityId id)
        {
            var sb = new StringBuilder();
            sb.Append('#').Append(id.Value);
            if (ctx.Identity.TryGet(id, out var ident) && ident != null) sb.Append(' ').Append(ident.Name);
            if (ctx.Residency.TryGet(id, out var res) && res != null) sb.Append(" [").Append(res.Role).Append(']');

            if (ctx.Behavior.TryGet(id, out var b))
                sb.Append(" doing ").Append(b.Activity).Append('/').Append(b.Phase);

            if (ctx.Needs.TryGet(id, out var needs))
            {
                var v = needs.V;
                int loudest = 0;
                for (int a = 1; a < NeedAxis.Count; a++) if (v[a] > v[loudest]) loudest = a;
                sb.Append("; needs h=").Append(v[NeedAxis.Hunger].ToString("F2"))
                  .Append(" e=").Append(v[NeedAxis.EnergyDef].ToString("F2"))
                  .Append(" s=").Append(v[NeedAxis.SocialDef].ToString("F2"))
                  .Append(" $=").Append(v[NeedAxis.CoinDef].ToString("F2"))
                  .Append(" (loudest: ").Append(AxisNames[loudest]).Append(')');
            }

            sb.Append("; coin=").Append(ctx.Coin.Get(id).ToString("F2"));
            return sb.ToString();
        }

        /// Standard Gini coefficient over non-negative values: 0 = everyone
        /// equal, →1 = one entity holds everything.
        public static double GiniOf(List<double> values)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            if (sum <= 0) return 0;
            double weighted = 0;
            for (int i = 0; i < values.Count; i++) weighted += (i + 1) * values[i];
            int nn = values.Count;
            return (2.0 * weighted) / (nn * sum) - (nn + 1.0) / nn;
        }
    }
}
