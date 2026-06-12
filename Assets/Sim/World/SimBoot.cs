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
        public TownLoadResult Town;
    }

    /// Standard town-sim bring-up shared by every host (console, TCP server,
    /// web spectator, future Unity/Godot drivers): full system stack, town
    /// loaded from ARENA2, clock seeded at dawn-ish, sunny weather.
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

            var events = new EventBus();
            var time = new SimulationTime(0.1);
            var random = new SimRandom(seed);
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
            loop.Register(new NeedsSystem());
            loop.Register(new OddSystem());
            loop.Register(new MovementSystem());
            loop.Register(new SocialSystem());
            var log = new EventLog();
            loop.Register(log);

            var town = TownLoader.Load(ctx, location, blocks);

            inputs.Enqueue(new SeedClockInput
            {
                Year = 405, Month = 0, Day = 3, Hour = 5, Minute = 30, Second = 0f,
                TimeScale = timeScale,
            });
            ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });

            return new SimBootResult { Ctx = ctx, Loop = loop, Log = log, Town = town };
        }
    }
}
