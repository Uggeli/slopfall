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
            string arena2 = DataProbe.Arena2Path;
            var maps = new MapsFile(System.IO.Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(System.IO.Path.Combine(arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);

            var location = maps.GetLocation(regionName, locationName);
            if (!location.Loaded)
            {
                Console.Error.WriteLine("location not found: " + regionName + "/" + locationName);
                return 1;
            }

            var events = new EventBus();
            var time = new SimulationTime(0.1);
            var random = new SimRandom(12345);
            var inputs = new InputBus();
            var ctx = new SimulationContext(events, time, random, inputs);

            var loop = new TickLoop(ctx);
            loop.Register(new TimeSystem());
            loop.Register(new WeatherSystem());
            loop.Register(new SunlightSystem());
            loop.Register(new HealthSystem());
            loop.Register(new EffectLifecycleSystem());
            loop.Register(new EffectTickSystem());
            loop.Register(new EffectAggregateSystem());
            loop.Register(new StatusFlagDeriveSystem());
            loop.Register(new SkillAdvancementSystem());
            loop.Register(new ProgressionSystem());
            var log = new EventLog();
            loop.Register(log);

            var town = TownLoader.Load(ctx, location, blocks);

            inputs.Enqueue(new SeedClockInput
            {
                Year = 405, Month = 0, Day = 3, Hour = 5, Minute = 30, Second = 0f,
                TimeScale = timeScale,
            });
            ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });

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

            for (int i = 0; i < ticks; i++)
                loop.Step();

            var clock = ctx.WorldClock.Current;
            Console.WriteLine("after " + ticks + " ticks: "
                + clock.Hour.ToString("00") + ":" + clock.Minute.ToString("00")
                + ", sun=" + ctx.Lighting.Current.SunIntensity.ToString("F2"));
            Console.WriteLine();

            Console.WriteLine("sample civilians:");
            int shown = 0;
            foreach (var kv in ctx.Residency.All)
            {
                if (shown++ >= 6) break;
                ctx.Identity.TryGet(kv.Key, out var who);
                ctx.Position.TryGet(kv.Key, out var pos);
                ctx.Buildings.TryGet(kv.Value.BuildingIndex, out var home);
                Console.WriteLine("  #" + kv.Key.Value + " " + who.Name
                    + "  at " + pos + "  " + kv.Value.Role + " of building " + kv.Value.BuildingIndex
                    + " (" + (home != null ? home.Kind.ToString() : "?") + ", quality " + (home != null ? home.Quality : 0) + ")");
            }
            Console.WriteLine();

            Console.WriteLine("event log (newest first):");
            foreach (var entry in log.Snapshot().Take(12))
                Console.WriteLine("  [t" + entry.Tick.ToString("0000") + "] " + entry.Summary);

            return 0;
        }
    }
}
