using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class SocialSystemTests
    {
        /// Entities with no Residency row are outside OddSystem's roster, so a
        /// manually planted Behavior row stays put — perfect for isolating the
        /// social machinery.
        static EntityId PlantSocializer(SimHarness h, int building)
        {
            var id = h.SpawnEntity();
            h.Ctx.Needs.Set(id, MakeNeeds(socialDef: 0.8));
            h.Ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = ActivityKind.Socialize,
                Phase = ActivityPhase.Doing,
                TargetBuilding = building,
                RemainingGameMinutes = 100000,
            });
            return id;
        }

        static NeedsData MakeNeeds(double hunger = 0, double energyDef = 0, double socialDef = 0)
        {
            var n = new NeedsData();
            n.V[NeedAxis.Hunger] = hunger;
            n.V[NeedAxis.EnergyDef] = energyDef;
            n.V[NeedAxis.SocialDef] = socialDef;
            return n;
        }

        [Fact]
        public void SharedSocialTime_GrowsFamiliarity_BothWays()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var a = PlantSocializer(h, building: 7);
            var b = PlantSocializer(h, building: 7);
            h.SeedClock(hour: 18, timeScale: 600f);     // 10 game-min per tick

            h.Step(12);                                  // ~2 shared game-hours

            Assert.True(h.Ctx.Relations.TryGet(a, out var ra));
            Assert.True(ra.Of.TryGetValue(b, out var rab));
            Assert.True(rab.Familiarity > 0.1, "familiarity " + rab.Familiarity);

            Assert.True(h.Ctx.Relations.TryGet(b, out var rb));
            Assert.True(rb.Of.TryGetValue(a, out var rba));
            Assert.Equal(rab.Familiarity, rba.Familiarity, 5);
        }

        [Fact]
        public void DifferentBuildings_NoRelation()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var a = PlantSocializer(h, building: 7);
            var b = PlantSocializer(h, building: 8);
            h.SeedClock(hour: 18, timeScale: 600f);

            h.Step(12);

            bool aKnowsB = h.Ctx.Relations.TryGet(a, out var ra) && ra.Of.ContainsKey(b);
            Assert.False(aKnowsB);
        }

        [Fact]
        public void Meeting_WritesMemory_AndEventFiresOnce()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var met = h.Collect<MetSimEvent>();
            var a = PlantSocializer(h, building: 7);
            var b = PlantSocializer(h, building: 7);
            h.SeedClock(hour: 18, timeScale: 600f);

            h.Step(30);                                  // plenty to cross Met and stay

            Assert.True(h.Ctx.Memory.TryGet(a, out var ma));
            Assert.Contains(ma.Entries, e => e.Kind == MemoryKind.Met && e.Other == b && e.Building == 7);

            int aMetB = met.FindAll(e => e.Who == a && e.Other == b).Count;
            Assert.Equal(1, aMetB);
        }

        [Fact]
        public void CompanyScalesSocialRelief_DrinkingAloneBarelyHelps()
        {
            // Alone at a tavern.
            var h1 = new SimHarness(tickIntervalSeconds: 1.0);
            var alone = PlantSocializer(h1, building: 7);
            h1.SeedClock(hour: 18, timeScale: 600f);
            h1.Step(6);
            h1.Ctx.Needs.TryGet(alone, out var soloNeeds);

            // Same setup with two companions.
            var h2 = new SimHarness(tickIntervalSeconds: 1.0);
            var social = PlantSocializer(h2, building: 7);
            PlantSocializer(h2, building: 7);
            PlantSocializer(h2, building: 7);
            h2.SeedClock(hour: 18, timeScale: 600f);
            h2.Step(6);
            h2.Ctx.Needs.TryGet(social, out var companyNeeds);

            double soloRelief = 0.8 - soloNeeds.V[NeedAxis.SocialDef];
            double companyRelief = 0.8 - companyNeeds.V[NeedAxis.SocialDef];
            Assert.True(companyRelief > soloRelief * 2,
                "solo " + soloRelief.ToString("F3") + " vs company " + companyRelief.ToString("F3"));
        }

        [Fact]
        public void Affinity_IsSymmetric_AndDeterministic()
        {
            var h = new SimHarness();
            var a = new EntityId(12);
            var b = new EntityId(345);
            // No personalities -> hash fallback path.
            Assert.Equal(SocialSystem.Affinity(h.Ctx, a, b), SocialSystem.Affinity(h.Ctx, b, a), 10);
            Assert.Equal(SocialSystem.Affinity(h.Ctx, a, b), SocialSystem.Affinity(h.Ctx, a, b), 10);
            Assert.InRange(SocialSystem.Affinity(h.Ctx, a, b), -1.0, 1.2);
        }

        [Fact]
        public void Affinity_WarmSimilarClick_ColdOppositesGrate()
        {
            var h = new SimHarness();
            var warm1 = new EntityId(1);
            var warm2 = new EntityId(2);
            var cold = new EntityId(3);

            double[] Traits(double all, double warmth)
            {
                var t = new double[TraitIndex.Count];
                for (int i = 0; i < t.Length; i++) t[i] = all;
                t[TraitIndex.Warmth] = warmth;
                return t;
            }

            h.Ctx.Personality.Set(warm1, PersonalityData.Derive(Traits(0.8, 0.9)));
            h.Ctx.Personality.Set(warm2, PersonalityData.Derive(Traits(0.75, 0.85)));
            h.Ctx.Personality.Set(cold, PersonalityData.Derive(Traits(0.1, 0.05)));

            double click = SocialSystem.Affinity(h.Ctx, warm1, warm2);
            double grate = SocialSystem.Affinity(h.Ctx, warm1, cold);
            Assert.True(click > 0.4, "warm similar pair only " + click);
            Assert.True(grate < 0, "warm/cold opposites still positive: " + grate);
            Assert.Equal(SocialSystem.Affinity(h.Ctx, cold, warm1), grate, 10);
        }

        [Fact]
        public void PrepotencyGate_GradedThenHardCull_WithHysteresis()
        {
            // At rest, leisure is ungated. (incumbentLeisure is moot when quiet.)
            var quiet = MakeNeeds();
            Assert.Equal(1.0, OddSystem.PrepotencyGate(quiet.V, incumbentLeisure: true), 5);

            // Graded in the mid-range, against the ACTIVE threshold. Already in
            // leisure → judged at Enter (0.7): hunger 0.35 → 1 − 0.35/0.7 = 0.5.
            var peckish = MakeNeeds(hunger: 0.35);
            Assert.Equal(0.5, OddSystem.PrepotencyGate(peckish.V, incumbentLeisure: true), 5);

            // Hysteresis: at hunger 0.65, an agent ALREADY in leisure keeps a
            // sliver (below Enter 0.7), but one NOT in leisure is fully culled
            // (at/above Exit 0.6) — the band that stops boundary flicker.
            var boundary = MakeNeeds(hunger: 0.65);
            Assert.True(OddSystem.PrepotencyGate(boundary.V, incumbentLeisure: true) > 0,
                "an agent mid-leisure shouldn't be culled until Enter");
            Assert.Equal(0.0, OddSystem.PrepotencyGate(boundary.V, incumbentLeisure: false), 5);

            // Hard cull once clearly deficient, either way.
            var starving = MakeNeeds(hunger: 0.85);
            Assert.Equal(0.0, OddSystem.PrepotencyGate(starving.V, incumbentLeisure: true), 5);

            var exhausted = MakeNeeds(energyDef: 0.9);
            Assert.Equal(0.0, OddSystem.PrepotencyGate(exhausted.V, incumbentLeisure: true), 5);
        }

        static double[] WarmTraits()
        {
            var t = new double[TraitIndex.Count];
            for (int i = 0; i < t.Length; i++) t[i] = 0.8;
            t[TraitIndex.Warmth] = 0.9;     // warm + similar -> strong positive affinity
            return t;
        }

        /// L1 gate (docs/living_world_L1_entropy.md): a bond decays toward
        /// neutral unless refreshed. Qualitative direction only — no tuned
        /// magnitudes. A neglected friendship cools below a contacted one and
        /// lapses; contact keeps a bond warm.
        [Fact]
        public void NeglectedBond_CoolsAndLapses_WhileContactStaysWarm()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var formed = h.Collect<FriendshipFormedEvent>();
            var lapsed = h.Collect<FriendshipLapsedEvent>();

            var c1 = PlantSocializer(h, building: 7);   // contacted pair: socialize throughout
            var c2 = PlantSocializer(h, building: 7);
            var i1 = PlantSocializer(h, building: 8);   // isolated pair: warm up, then pulled apart
            var i2 = PlantSocializer(h, building: 8);
            foreach (var id in new[] { c1, c2, i1, i2 })
                h.Ctx.Personality.Set(id, PersonalityData.Derive(WarmTraits()));

            h.SeedClock(hour: 18, timeScale: 600f);     // 10 game-min/tick

            h.Step(40);                                  // warm up: both pairs befriend

            Assert.Contains(formed, e => e.Who == i1 && e.Other == i2);
            Assert.True(h.Ctx.Relations.TryGet(i1, out var warm), "no relations for isolated pair");
            Assert.True(warm.Of.TryGetValue(i2, out var peakRel) && peakRel.FriendAnnounced,
                        "isolated pair never befriended");
            double isolatedPeak = peakRel.Familiarity;

            // Pull the isolated pair apart — idle, no longer co-located.
            foreach (var id in new[] { i1, i2 })
                h.Ctx.Behavior.Set(id, new BehaviorData
                {
                    Activity = ActivityKind.Idle, Phase = ActivityPhase.Doing,
                    TargetBuilding = -1, RemainingGameMinutes = 100000,
                });

            h.Step(700);                                 // long neglect; contact pair keeps socializing

            h.Ctx.Relations.TryGet(i1, out var iso); iso.Of.TryGetValue(i2, out var isoRel);
            h.Ctx.Relations.TryGet(c1, out var con); con.Of.TryGetValue(c2, out var conRel);

            Assert.True(isoRel.Familiarity < isolatedPeak, "neglected familiarity didn't drop");
            Assert.True(isoRel.Familiarity < conRel.Familiarity, "neglected pair not cooler than contacted pair");
            Assert.Contains(lapsed, e => e.Who == i1 && e.Other == i2);
        }
    }

    /// Social fabric over a real town day.
    public class SocialDayTests
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
        public void OneDay_ProducesAcquaintances_AndMemories()
        {
            if (!Available) return;
            var h = LoadTown();

            h.Step(1440);

            int acquaintances = 0;
            foreach (var kv in h.Ctx.Relations.All)
                foreach (var rel in kv.Value.Of)
                    if (rel.Value.Familiarity >= 0.05) acquaintances++;

            Assert.True(acquaintances > 100, "only " + acquaintances + " acquaintances after a day");
            // Friendships now take several game-days, not one: L1 regard decay plus
            // L4 begging (mass refusals sour regard in the poor town) slowed bonding
            // — a multi-day soak shows friend-edges climbing steadily from ~0 on day
            // 1. Acquaintances + memories are the day-one fabric signals; friendship
            // pacing is a whole-stack tuning concern (docs/living_world.md → Tuning).

            int memoryRows = 0;
            foreach (var kv in h.Ctx.Memory.All)
            {
                Assert.InRange(kv.Value.Entries.Count, 1, MemoryRegistry.MaxEntries);
                memoryRows += kv.Value.Entries.Count;
            }
            Assert.True(memoryRows > 100, "only " + memoryRows + " memory rows");
        }

        [Fact]
        public void GrowthDrive_VisitsHappen_ButNeverWhileStarving()
        {
            if (!Available) return;
            var h = LoadTown();

            int visitObservations = 0;
            for (int block = 0; block < 24; block++)
            {
                h.Step(60);
                foreach (var kv in h.Ctx.Behavior.All)
                {
                    if (kv.Value.Activity != ActivityKind.Visit) continue;
                    visitObservations++;
                    h.Ctx.Needs.TryGet(kv.Key, out var needs);
                    Assert.True(needs.V[NeedAxis.Hunger] < 0.95,
                        "starving civilian out sightseeing: hunger " + needs.V[NeedAxis.Hunger]);
                }
            }
            Assert.True(visitObservations > 50, "growth drive barely fired: " + visitObservations);
        }

        [Fact]
        public void SameSeed_SameSocialFabric()
        {
            if (!Available) return;
            var a = LoadTown();
            var b = LoadTown();
            a.Step(500);
            b.Step(500);

            int CountAcquaintances(SimHarness h)
            {
                int n = 0;
                foreach (var kv in h.Ctx.Relations.All)
                    foreach (var rel in kv.Value.Of)
                        if (rel.Value.Familiarity >= 0.05) n++;
                return n;
            }

            Assert.Equal(CountAcquaintances(a), CountAcquaintances(b));
        }
    }
}
