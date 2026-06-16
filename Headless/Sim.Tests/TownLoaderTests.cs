using System;
using System.IO;
using System.Linq;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// TownLoader against real ARENA2 data. Goldens captured via
    /// `Sim.Host --town`. Tests no-op silently when game data is absent.
    public class TownLoaderTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        static TownLoadResult LoadTown(SimulationContext ctx, string region, string location)
        {
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            var loc = maps.GetLocation(region, location);
            Assert.True(loc.Loaded);
            return TownLoader.Load(ctx, loc, blocks);
        }

        static SimulationContext NewContext(int seed = 12345) =>
            new SimulationContext(new EventBus(), new SimulationTime(0.1), new SimRandom(seed), new InputBus());

        [Fact]
        public void GothwayGarden_LoadsWithKnownPopulation()
        {
            if (!Available) return;
            var ctx = NewContext();
            var town = LoadTown(ctx, "Daggerfall", "Gothway Garden");

            Assert.Equal(4, town.BlocksWide);
            Assert.Equal(3, town.BlocksHigh);
            Assert.Equal(175, town.Buildings);              // loaded from block data
            Assert.Equal(337, town.Civilians);
            // Stage 5 synthesizes one Farm workplace per settlement, so the registry
            // holds one more building than were loaded; no new civilians (the farm
            // keeper is a promoted resident).
            Assert.Equal(176, ctx.Buildings.Count);
            Assert.Equal(337, ctx.Residency.Count);
            Assert.Equal(337, ctx.Identity.Count);
        }

        [Fact]
        public void EveryCivilian_HasVitalsPositionAndValidHome()
        {
            if (!Available) return;
            var ctx = NewContext();
            var town = LoadTown(ctx, "Daggerfall", "Gothway Garden");

            float maxX = town.BlocksWide * 4096f * TownLoader.GlobalScale;
            float maxZ = town.BlocksHigh * 4096f * TownLoader.GlobalScale;

            foreach (var kv in ctx.Residency.All)
            {
                Assert.True(ctx.Buildings.TryGet(kv.Value.BuildingIndex, out var home));

                Assert.True(ctx.Vitals.TryGet(kv.Key, out var vitals));
                Assert.True(vitals.CurrentHealth > 0);
                Assert.Equal(vitals.MaxHealth, vitals.CurrentHealth);

                Assert.True(ctx.Position.TryGet(kv.Key, out var pos));
                Assert.InRange(pos.X, 0f, maxX);
                Assert.InRange(pos.Z, 0f, maxZ);

                Assert.True(ctx.Stats.TryGet(kv.Key, out var stats));
                Assert.All(stats.Stats, v => Assert.InRange(v, 30, 60));

                Assert.True(ctx.Identity.TryGet(kv.Key, out var identity));
                Assert.Equal(EntityKind.CivilianNPC, identity.Kind);
            }
        }

        [Fact]
        public void KeepersStaffShops_ResidentsFillHouses_WallsStayEmpty()
        {
            if (!Available) return;
            var ctx = NewContext();
            LoadTown(ctx, "Daggerfall", "Gothway Garden");

            foreach (var kv in ctx.Residency.All)
            {
                ctx.Buildings.TryGet(kv.Value.BuildingIndex, out var home);
                switch (home.Kind)
                {
                    case BuildingKind.House1:
                    case BuildingKind.House2:
                    case BuildingKind.House3:
                    case BuildingKind.House4:
                    case BuildingKind.House5:
                    case BuildingKind.House6:
                        Assert.Equal(ResidentRole.Resident, kv.Value.Role);
                        break;
                    case BuildingKind.None:
                    case BuildingKind.Town23:
                        Assert.Fail("wall/none building " + kv.Value.BuildingIndex + " has population");
                        break;
                    default:
                        Assert.Equal(ResidentRole.Keeper, kv.Value.Role);
                        break;
                }
            }
        }

        [Fact]
        public void CityOfDaggerfall_LoadsAtScale()
        {
            if (!Available) return;
            var ctx = NewContext();
            var town = LoadTown(ctx, "Daggerfall", "Daggerfall");

            Assert.Equal(647, town.Buildings);
            Assert.Equal(1029, town.Civilians);
            // The capital is a real economy: taverns and guild halls present.
            Assert.Contains(ctx.Buildings.All, kv => kv.Value.Kind == BuildingKind.Tavern);
            Assert.Contains(ctx.Buildings.All, kv => kv.Value.Kind == BuildingKind.GuildHall);
            Assert.Contains(ctx.Buildings.All, kv => kv.Value.Kind == BuildingKind.Bank);
        }

        [Fact]
        public void SameSeed_SpawnsIdenticalPopulation()
        {
            if (!Available) return;
            var a = NewContext(seed: 777);
            var b = NewContext(seed: 777);
            LoadTown(a, "Daggerfall", "Gothway Garden");
            LoadTown(b, "Daggerfall", "Gothway Garden");

            Assert.Equal(a.Identity.Count, b.Identity.Count);

            var statsA = a.Stats.All.OrderBy(kv => kv.Key.Value).Select(kv => kv.Value).ToList();
            var statsB = b.Stats.All.OrderBy(kv => kv.Key.Value).Select(kv => kv.Value).ToList();
            for (int i = 0; i < statsA.Count; i++)
                Assert.Equal(statsA[i].Stats, statsB[i].Stats);
        }
    }
}
