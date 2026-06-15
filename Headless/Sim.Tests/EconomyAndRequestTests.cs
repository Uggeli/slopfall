using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class EconomyTests
    {
        [Fact]
        public void TavernBill_FlowsToTheKeeper()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var keeper = h.SpawnEntity("Keeper");
            var patron = h.SpawnEntity("Patron");
            int tavern = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern });
            h.Ctx.Stock.Set(tavern, Good.Provisions, 100);   // meals need provisions on the shelf (G3)
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = tavern, Role = ResidentRole.Keeper });
            h.Ctx.Coin.Set(keeper, 0.5);
            h.Ctx.Coin.Set(patron, 0.5);
            h.Ctx.Needs.Set(patron, new NeedsData());
            h.Ctx.Behavior.Set(patron, new BehaviorData
            {
                Activity = ActivityKind.EatTavern,
                Phase = ActivityPhase.Doing,
                TargetBuilding = tavern,
                RemainingGameMinutes = 100000,
            });
            h.SeedClock(hour: 18, timeScale: 600f);     // 10 game-min per tick

            h.Step(6);                                   // a solid hour of eating

            double patronCoin = h.Ctx.Coin.Get(patron);
            double keeperCoin = h.Ctx.Coin.Get(keeper);
            Assert.True(patronCoin < 0.5, "patron paid nothing");
            Assert.True(keeperCoin > 0.5, "keeper earned nothing");
            // Conservation: the pair's total only loses cost of living.
            double total = patronCoin + keeperCoin;
            Assert.InRange(total, 0.99 - 0.01, 1.0);
        }

        [Fact]
        public void CoinDef_DerivesFromMoney()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var id = h.SpawnEntity();
            h.Ctx.Needs.Set(id, new NeedsData());
            h.Ctx.Coin.Set(id, 0.3);
            h.SeedClock(hour: 12, timeScale: 60f);

            h.Step(2);

            Assert.True(h.Ctx.Needs.TryGet(id, out var needs));
            Assert.Equal(0.7, needs.V[NeedAxis.CoinDef], 2);
        }

        [Fact]
        public void Transfer_CannotOverdraw()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var a = h.SpawnEntity("A");
            var b = h.SpawnEntity("B");
            h.Ctx.Coin.Set(a, 0.1);
            h.Ctx.Coin.Set(b, 0.0);
            h.SeedClock();

            h.Ctx.Events.Emit(new CoinTransferEvent { From = a, To = b, Amount = 0.5 });
            h.Step(2);

            Assert.Equal(0.0, h.Ctx.Coin.Get(a), 3);
            Assert.Equal(0.1, h.Ctx.Coin.Get(b), 3);    // got what existed, not the ask
        }
    }

    public class RequestTests
    {
        static SimHarness Harness(out EntityId pauper, out EntityId mark, bool markIsKeeper, double markRegardForPauper)
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

            // Embodied asking: the pauper walks to the mark, so both need
            // somewhere to stand.
            h.Ctx.Position.Set(pauper, 0f, 0f, 0f, 0f);
            h.Ctx.Position.Set(mark, 10f, 0f, 0f, 0f);

            if (markIsKeeper)
                h.Ctx.Residency.Set(mark, new ResidencyData { BuildingIndex = 3, Role = ResidentRole.Keeper });

            // The pauper knows the mark; the mark's regard decides the outcome.
            var pauperRel = new RelationsData();
            pauperRel.Of[mark] = new RelationData { Familiarity = 0.4, Regard = 0.2 };
            h.Ctx.Relations.Set(pauper, pauperRel);
            var markRel = new RelationsData();
            markRel.Of[pauper] = new RelationData { Familiarity = 0.4, Regard = markRegardForPauper };
            h.Ctx.Relations.Set(mark, markRel);

            h.SeedClock(hour: 12, timeScale: 60f);
            return h;
        }

        [Fact]
        public void PoorAsksLikedFriend_GetsAlms_BothRemember()
        {
            var h = Harness(out var pauper, out var mark, markIsKeeper: false, markRegardForPauper: 0.5);
            var granted = h.Collect<HelpGrantedEvent>();

            h.Step(10);         // journey decided → walk over → ask → impulses land

            Assert.Single(granted);
            Assert.Equal(pauper, granted[0].Asker);
            Assert.Equal(mark, granted[0].Giver);
            Assert.True(h.Ctx.Coin.Get(pauper) > 0.2, "alms never arrived");
            Assert.True(h.Ctx.Coin.Get(mark) < 0.8, "giver paid nothing");

            Assert.True(h.Ctx.Memory.TryGet(pauper, out var pm));
            Assert.Contains(pm.Entries, e => e.Kind == MemoryKind.ReceivedHelp && e.Other == mark);
            Assert.True(h.Ctx.Memory.TryGet(mark, out var mm));
            Assert.Contains(mm.Entries, e => e.Kind == MemoryKind.GaveHelp && e.Other == pauper);

            // Gratitude: the pauper's regard for the giver jumped.
            Assert.True(h.Ctx.Relations.TryGet(pauper, out var rel));
            Assert.True(rel.Of[mark].Regard > 0.4);
        }

        [Fact]
        public void PoorAsksSomeoneWhoDislikesThem_GetsRefused_GrudgeForms()
        {
            var h = Harness(out var pauper, out var mark, markIsKeeper: false, markRegardForPauper: -0.5);
            var refused = h.Collect<HelpRefusedEvent>();

            h.Step(10);

            Assert.Single(refused);
            Assert.Equal(0.0, h.Ctx.Coin.Get(pauper), 3);

            Assert.True(h.Ctx.Memory.TryGet(pauper, out var pm));
            Assert.Contains(pm.Entries, e => e.Kind == MemoryKind.WasRefused && e.Other == mark);

            // Resentment: regard for the refuser dropped from 0.2.
            Assert.True(h.Ctx.Relations.TryGet(pauper, out var rel));
            Assert.True(rel.Of[mark].Regard < 0.05, "no resentment after refusal: " + rel.Of[mark].Regard);
        }

        [Fact]
        public void KeepersExtendCharity_ToStrangers()
        {
            // No relations at all: the pauper falls back to the richest
            // keeper, who tolerates strangers.
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var pauper = h.SpawnEntity("Pauper");
            var keeper = h.SpawnEntity("Keeper");
            var needs = new NeedsData();
            needs.V[NeedAxis.CoinDef] = 1.0;
            h.Ctx.Needs.Set(pauper, needs);
            h.Ctx.Coin.Set(pauper, 0.0);
            h.Ctx.Needs.Set(keeper, new NeedsData());
            h.Ctx.Coin.Set(keeper, 1.0);
            h.Ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = 3, Role = ResidentRole.Keeper });
            h.Ctx.Position.Set(pauper, 0f, 0f, 0f, 0f);
            h.Ctx.Position.Set(keeper, 10f, 0f, 0f, 0f);
            h.SeedClock(hour: 12, timeScale: 60f);
            var granted = h.Collect<HelpGrantedEvent>();

            h.Step(10);

            Assert.Single(granted);
            Assert.Equal(keeper, granted[0].Giver);
        }
    }

    public class GossipTests
    {
        [Fact]
        public void OpinionsPropagate_ThroughSharedTavernTime()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);

            EntityId Plant(string name)
            {
                var id = h.SpawnEntity(name);
                h.Ctx.Needs.Set(id, new NeedsData());
                h.Ctx.Behavior.Set(id, new BehaviorData
                {
                    Activity = ActivityKind.Socialize,
                    Phase = ActivityPhase.Doing,
                    TargetBuilding = 7,
                    RemainingGameMinutes = 100000,
                });
                return id;
            }

            var teller = Plant("Teller");
            var listener = Plant("Listener");
            var subject = h.SpawnEntity("Subject");     // not present, only talked about

            // The teller adores the subject; the listener has never heard of them.
            var tellerRel = new RelationsData();
            tellerRel.Of[subject] = new RelationData { Familiarity = 0.6, Regard = 0.9 };
            h.Ctx.Relations.Set(teller, tellerRel);

            h.SeedClock(hour: 18, timeScale: 600f);
            h.Step(30);     // a long evening of talk

            Assert.True(h.Ctx.Relations.TryGet(listener, out var rel));
            Assert.True(rel.Of.ContainsKey(subject), "listener never heard of the subject");
            Assert.True(rel.Of[subject].Regard > 0.05,
                "gossip didn't transfer regard: " + rel.Of[subject].Regard);
            Assert.True(rel.Of[subject].Familiarity > 0, "knows OF them now");
        }
    }

    public class CharityDayTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void OneTownDay_EconomyConserves()
        {
            if (!Available) return;
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            TownLoader.Load(h.Ctx, maps.GetLocation("Daggerfall", "Gothway Garden"), blocks);
            h.SeedClock(hour: 5, minute: 30, timeScale: 60f);
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });

            double moneyBefore = 0;
            foreach (var kv in h.Ctx.Coin.All) moneyBefore += kv.Value;
            moneyBefore += h.Ctx.Treasury.Total;        // treasury (E3) is part of the money supply

            h.Step(1440);

            // Since E1 gave residents jobs, charity barely flows (people earn
            // instead of begging) and the destitute legitimately gain wealth — so
            // the old "alms > 20 / total within 15%" assumptions are gone. The
            // invariant that always holds is conservation: every coin is accounted
            // — including the treasury that tax fills and guard salaries drain.
            var ledger = h.Ctx.Ledger.Current;
            double moneyAfter = 0;
            foreach (var kv in h.Ctx.Coin.All) moneyAfter += kv.Value;
            moneyAfter += h.Ctx.Treasury.Total;
            Assert.Equal(moneyBefore + ledger.Minted - ledger.Sunk, moneyAfter, 6);
        }
    }
}
