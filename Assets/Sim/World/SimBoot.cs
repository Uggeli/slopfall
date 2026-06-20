using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim
{
    /// Sim bring-up: build the CQRS SimWorld, load the world from ARENA2 via the
    /// loaders, seed clock + weather, and return it ready to tick. CreateTown loads
    /// one location; CreateRegion loads every settled location of a region.
    public static class SimBoot
    {
        public static string DefaultArena2Path =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        public static SimWorld CreateTown(string arena2Path, string regionName, string locationName,
            float timeScale, int seed = 12345)
        {
            var maps = new MapsFile(System.IO.Path.Combine(arena2Path, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(System.IO.Path.Combine(arena2Path, "BLOCKS.BSA"), FileUsage.UseMemory, true);

            var location = maps.GetLocation(regionName, locationName);
            if (!location.Loaded)
                throw new ArgumentException("location not found: " + regionName + "/" + locationName);

            var woods = new WoodsFile(System.IO.Path.Combine(arena2Path, "WOODS.WLD"), FileUsage.UseMemory, true);

            var world = new SimWorld(seed);
            var rng = new SimRandom(seed);
            TownLoader.Load(world, rng, location, blocks, maps, woods);
            SeedStart(world, timeScale);
            return world;
        }

        public static SimWorld CreateRegion(string arena2Path, string regionName,
            float timeScale, int seed = 12345,
            System.Func<string, int, int, float> tileFloor = null, float maxTerrainHeight = 0f)
        {
            var maps = new MapsFile(System.IO.Path.Combine(arena2Path, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(System.IO.Path.Combine(arena2Path, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            var woods = new WoodsFile(System.IO.Path.Combine(arena2Path, "WOODS.WLD"), FileUsage.UseMemory, true);

            var world = new SimWorld(seed);
            var rng = new SimRandom(seed);
            RegionLoader.LoadRegion(world, rng, maps, blocks, regionName, woods, tileFloor, maxTerrainHeight);
            SeedStart(world, timeScale);
            return world;
        }

        /// Seed the clock + weather as registry intents (loaders wrote entity data
        /// directly), then fold them in — so tick 0 reads a seeded, ticking world.
        static void SeedStart(SimWorld world, float timeScale)
        {
            world.Events.Publish(new WorldClockSetIntent
            {
                Year = 405, Month = 0, Day = 3, Hour = 5, Minute = 30, Second = 0f,
                TimeScale = timeScale, DeltaGameSeconds = 0.1,
            });
            world.Events.Publish(new WeatherSetIntent { Kind = WeatherKind.Sunny });
            world.ApplySeed();
        }
    }
}
