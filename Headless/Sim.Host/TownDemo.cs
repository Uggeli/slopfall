using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Loads a real town into the sim registries and ticks the world:
    /// the first time a Daggerfall location exists as a living simulation
    /// rather than a rendered set piece.
    public static class TownDemo
    {
        public static int Run(string regionName, string locationName, int ticks, float timeScale)
        {
            TownBoot.Boot boot;
            try { boot = TownBoot.Create(regionName, locationName, timeScale); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            var ctx = boot.Ctx;
            var loop = boot.Loop;
            var events = ctx.Events;
            var town = boot.Town;

            Console.WriteLine(town.RegionName + " / " + town.Name
                + " — " + town.BlocksWide + "x" + town.BlocksHigh + " blocks, "
                + town.Buildings + " structures, " + town.Civilians + " civilians");
            Console.WriteLine();

            Console.WriteLine("buildings by kind:");
            var byKind = new Dictionary<BuildingKind, int>();
            foreach (var kv in ctx.Buildings.All)
                byKind[kv.Value.Kind] = (byKind.TryGetValue(kv.Value.Kind, out var n) ? n : 0) + 1;
            foreach (var kv in byKind.OrderByDescending(kv => kv.Value))
                Console.WriteLine("  " + kv.Key.ToString().PadRight(15) + " " + kv.Value);
            Console.WriteLine();

            // Day-in-the-life tracing: one tavern keeper, one shop keeper, one
            // resident, transitions stamped with the game clock.
            var samples = PickSamples(ctx);
            var timelines = new Dictionary<EntityId, List<string>>();
            foreach (var s in samples) timelines[s] = new List<string>();
            events.Subscribe<ActivityStartedEvent>(e =>
            {
                if (!timelines.TryGetValue(e.Entity, out var line)) return;
                var c = ctx.WorldClock.Current;
                line.Add(c.Hour.ToString("00") + ":" + c.Minute.ToString("00") + " → " + e.Activity);
            });

            for (int i = 0; i < ticks; i++)
                loop.Step();

            var clock = ctx.WorldClock.Current;
            Console.WriteLine("after " + ticks + " ticks: "
                + clock.Hour.ToString("00") + ":" + clock.Minute.ToString("00")
                + ", sun=" + ctx.Lighting.Current.SunIntensity.ToString("F2"));
            Console.WriteLine();

            Console.WriteLine("a day in the life:");
            foreach (var s in samples)
            {
                ctx.Identity.TryGet(s, out var who);
                ctx.Residency.TryGet(s, out var res);
                Console.WriteLine("  #" + s.Value + " " + who.Name + " [" + res.Role + "]");
                Console.WriteLine("    " + string.Join(",  ", timelines[s]));
            }
            Console.WriteLine();

            Console.WriteLine("population right now:");
            var byActivity = new Dictionary<ActivityKind, int>();
            foreach (var kv in ctx.Behavior.All)
                byActivity[kv.Value.Activity] = (byActivity.TryGetValue(kv.Value.Activity, out var n) ? n : 0) + 1;
            foreach (var kv in byActivity.OrderByDescending(kv => kv.Value))
                Console.WriteLine("  " + kv.Key.ToString().PadRight(11) + " " + kv.Value);
            Console.WriteLine();

            var avg = new double[NeedAxis.Count];
            int count = 0;
            foreach (var kv in ctx.Needs.All)
            {
                for (int a = 0; a < NeedAxis.Count; a++) avg[a] += kv.Value.V[a];
                count++;
            }
            Console.WriteLine("average needs (deficits, 0=satisfied): hunger="
                + (avg[NeedAxis.Hunger] / count).ToString("F2")
                + " energy=" + (avg[NeedAxis.EnergyDef] / count).ToString("F2")
                + " social=" + (avg[NeedAxis.SocialDef] / count).ToString("F2")
                + " coin=" + (avg[NeedAxis.CoinDef] / count).ToString("F2"));
            Console.WriteLine();

            // Social fabric: how much society did one day produce?
            int acquaintances = 0, friendships = 0;
            EntityId bestA = EntityId.None, bestB = EntityId.None;
            double bestRegard = double.MinValue;
            foreach (var kv in ctx.Relations.All)
            {
                foreach (var rel in kv.Value.Of)
                {
                    if (rel.Value.Familiarity >= 0.05) acquaintances++;
                    if (rel.Value.FriendAnnounced) friendships++;
                    if (rel.Value.Regard > bestRegard)
                    {
                        bestRegard = rel.Value.Regard;
                        bestA = kv.Key; bestB = rel.Key;
                    }
                }
            }
            Console.WriteLine("social fabric: " + acquaintances + " acquaintances, "
                + friendships + " friendships (directed)");
            if (!bestA.IsNone)
            {
                ctx.Identity.TryGet(bestA, out var ia);
                ctx.Identity.TryGet(bestB, out var ib);
                Console.WriteLine("warmest regard: #" + bestA.Value + " " + (ia != null ? ia.Name : "?")
                    + " → #" + bestB.Value + " " + (ib != null ? ib.Name : "?")
                    + " (" + bestRegard.ToString("F2") + ")");
            }

            return 0;
        }

        static List<EntityId> PickSamples(SimulationContext ctx)
        {
            var samples = new List<EntityId>();
            EntityId tavernKeeper = EntityId.None, shopKeeper = EntityId.None, resident = EntityId.None;
            foreach (var kv in ctx.Residency.All)
            {
                ctx.Buildings.TryGet(kv.Value.BuildingIndex, out var b);
                if (kv.Value.Role == ResidentRole.Keeper && b != null && b.Kind == BuildingKind.Tavern)
                { if (tavernKeeper.IsNone) tavernKeeper = kv.Key; }
                else if (kv.Value.Role == ResidentRole.Keeper)
                { if (shopKeeper.IsNone) shopKeeper = kv.Key; }
                else
                { if (resident.IsNone) resident = kv.Key; }
            }
            if (!tavernKeeper.IsNone) samples.Add(tavernKeeper);
            if (!shopKeeper.IsNone) samples.Add(shopKeeper);
            if (!resident.IsNone) samples.Add(resident);
            return samples;
        }
    }
}
