using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Stage 2 regional loader: a whole region loads into one context, and the
    /// load-time seeds stay settlement-local — no cross-settlement employment, no
    /// omniscient building knowledge. ARENA2-gated (silent pass without game data,
    /// like BehavioralHarness).
    public class RegionLoaderTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";
        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void Betony_LoadsAllSettlements_NoCrossSettlementBleed()
        {
            if (!Available) return;

            var boot = SimBoot.CreateRegion(Arena2, "Betony", 600f);
            var ctx = boot.Ctx;
            var settlements = ctx.Settlements;

            // Multiple settlements, and membership accounts for every civilian.
            Assert.True(settlements.Count > 1, "expected several settlements in Betony");
            int residentSum = 0;
            foreach (var s in settlements.All) residentSum += s.Residents.Count;
            Assert.Equal(boot.Region.Civilians, residentSum);

            // Map every resident to its settlement for the bleed checks.
            var settlementOf = new Dictionary<EntityId, int>();
            foreach (var s in settlements.All)
                foreach (var r in s.Residents)
                    settlementOf[r] = s.Id;

            foreach (var s in settlements.All)
            {
                var ownBuildings = new HashSet<int>(s.Buildings);

                foreach (var r in s.Residents)
                {
                    // (a) Knowledge is settlement-local: every place a resident knows
                    //     belongs to their own settlement.
                    foreach (var known in ctx.PlaceMemory.Known(r))
                        Assert.True(ownBuildings.Contains(known),
                            "resident " + r.Value + " in settlement " + s.Id + " knows a foreign building " + known);

                    // (b) Employment is settlement-local: nobody works across settlements.
                    if (ctx.Employment.TryGet(r, out var emp) && !emp.Employer.IsNone)
                        Assert.True(settlementOf.TryGetValue(emp.Employer, out var es) && es == s.Id,
                            "resident " + r.Value + " in settlement " + s.Id + " is employed across settlements");
                }
            }
        }

        [Fact]
        public void Betony_PerSettlementTreasuries_DistinctAndDeterministic()
        {
            if (!Available) return;

            var first = RunTaxedMonth(out double totalFirst, out bool taxedFirst);
            var second = RunTaxedMonth(out double totalSecond, out _);

            // Determinism: two identical regional runs agree on every settlement's
            // treasury balance and on the total money supply (F3 + per-settlement
            // finance produce a reproducible state).
            Assert.Equal(first.Count, second.Count);
            foreach (var kv in first)
                Assert.Equal(kv.Value, second[kv.Key], 9);
            Assert.Equal(totalFirst, totalSecond, 9);

            // The monthly tax actually flowed into local treasuries (Whitefort has
            // wealthy keepers above the exemption).
            Assert.True(taxedFirst, "no settlement collected any tax");
        }

        /// Load Betony, run a month rollover, return each settlement's treasury balance
        /// keyed by its OwnerId.Value, plus the total money supply.
        static Dictionary<int, double> RunTaxedMonth(out double moneySupply, out bool anyTaxed)
        {
            var boot = SimBoot.CreateRegion(Arena2, "Betony", 600f);
            var ctx = boot.Ctx;

            boot.Loop.Step();   // process the seeded clock input → Year set, economy runs
            boot.Loop.Step();
            ctx.Events.Emit(new NewMonthSimEvent { Month = 1, Year = 405 });  // tax man
            boot.Loop.Step();
            boot.Loop.Step();

            var balances = new Dictionary<int, double>();
            anyTaxed = false;
            foreach (var s in ctx.Settlements.All)
            {
                double bal = ctx.Treasury.Get(s.Treasury);
                // Treasury OwnerIds must be distinct per settlement.
                Assert.False(balances.ContainsKey(s.Treasury.Value),
                    "settlements share a treasury OwnerId " + s.Treasury.Value);
                balances[s.Treasury.Value] = bal;
            }
            anyTaxed = ctx.Ledger.Current.Taxes > 0;

            double coin = 0;
            foreach (var kv in ctx.Coin.All) coin += kv.Value;
            moneySupply = coin + ctx.Treasury.Total;
            return balances;
        }
    }
}
