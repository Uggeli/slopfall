using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace DaggerfallWorkshop.Sim
{
    public sealed class SimBootResult
    {
        public SimulationContext Ctx;
        public TickLoop Loop;
        public EventLog Log;
        public TownLoadResult Town;     // set by CreateTown (single location)
        public RegionLoadResult Region; // set by CreateRegion (whole region)
    }

    /// Standard sim bring-up shared by every host (console, TCP server, web
    /// spectator, future Unity/Godot drivers): full system stack, world loaded from
    /// ARENA2, clock seeded at dawn-ish, sunny weather. CreateTown loads one location;
    /// CreateRegion loads every settled location of a region into one context.
    public static class SimBoot
    {
        public static string DefaultArena2Path =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        public static SimBootResult CreateTown(string arena2Path, string regionName, string locationName,
            float timeScale, int seed = 12345)
        {
            var maps = new MapsFile(System.IO.Path.Combine(arena2Path, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(System.IO.Path.Combine(arena2Path, "BLOCKS.BSA"), FileUsage.UseMemory, true);

            var location = maps.GetLocation(regionName, locationName);
            if (!location.Loaded)
                throw new ArgumentException("location not found: " + regionName + "/" + locationName);

            var woods = new WoodsFile(System.IO.Path.Combine(arena2Path, "WOODS.WLD"), FileUsage.UseMemory, true);
            var boot = NewSim(seed);
            var town = TownLoader.Load(boot.Ctx, location, blocks, maps, woods);   // maps+woods → climate/coast/elevation detection
            SeedStart(boot.Ctx, timeScale);
            boot.Town = town;
            return boot;
        }

        public static SimBootResult CreateRegion(string arena2Path, string regionName,
            float timeScale, int seed = 12345)
        {
            var maps = new MapsFile(System.IO.Path.Combine(arena2Path, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(System.IO.Path.Combine(arena2Path, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            var woods = new WoodsFile(System.IO.Path.Combine(arena2Path, "WOODS.WLD"), FileUsage.UseMemory, true);

            var boot = NewSim(seed);
            boot.Region = RegionLoader.LoadRegion(boot.Ctx, maps, blocks, regionName, woods);
            SeedStart(boot.Ctx, timeScale);
            return boot;
        }

        /// Build the context + the full system stack (same order for every host).
        static SimBootResult NewSim(int seed)
        {
            var events = new EventBus();
            var time = new SimulationTime(0.1);
            var random = new SimRandom(seed);
            var inputs = new InputBus();
            var ctx = new SimulationContext(events, time, random, inputs);

            var loop = new TickLoop(ctx);
            loop.Register(new TimeSystem());
            loop.Register(new AgingSystem());        // L2: ages agents yearly; fatal hit past lifespan
            loop.Register(new WeatherSystem());
            loop.Register(new SunlightSystem());
            loop.Register(new HealthSystem());
            loop.Register(new EffectLifecycleSystem());
            loop.Register(new EffectTickSystem());
            loop.Register(new EffectAggregateSystem());
            loop.Register(new StatusFlagDeriveSystem());
            loop.Register(new SkillAdvancementSystem());
            loop.Register(new ProgressionSystem());
            loop.Register(new WeatherDriverSystem()); // headless weather authority
            loop.Register(new HolidaySystem());
            loop.Register(new EconomySystem());     // coin moves before needs derive from it
            loop.Register(new NeedsSystem());
            loop.Register(new OddSystem());          // decides → writes Intent
            loop.Register(new ExecutionSystem());    // reifies Intent → Behavior (sole writer)
            loop.Register(new MovementSystem());
            loop.Register(new CreatureSystem());      // V2a: mobile hostiles — spawn, wander, bite (combat emitter)
            loop.Register(new SenseSystem());        // raw senses: who's near (grid LOS)
            loop.Register(new SubjectiveSystem());    // membrane: senses → SubjectiveView + percepts (S1)
            loop.Register(new AffectsSystem());        // emotion: interactions → directed affects + regard (S2)
            loop.Register(new MeaningsSystem());        // semantic memory: fold interactions into learned categories (S3)
            loop.Register(new SocialSystem());
            loop.Register(new RequestSystem());     // reads relations after SocialSystem's tick
            loop.Register(new LifecycleSystem());    // L2: despawns the dead (last, so every system saw them live)
            loop.Register(new RepopulationSystem()); // L2.4: backfills vacated residency slots
            var log = new EventLog();
            loop.Register(log);

            return new SimBootResult { Ctx = ctx, Loop = loop, Log = log };
        }

        static void SeedStart(SimulationContext ctx, float timeScale)
        {
            ctx.Inputs.Enqueue(new SeedClockInput
            {
                Year = 405, Month = 0, Day = 3, Hour = 5, Minute = 30, Second = 0f,
                TimeScale = timeScale,
            });
            ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
        }
    }
}
