using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// RenderSnapshot is the server→client payload; these tests pin its
    /// contract: complete entity coverage, coherent world state, immutability
    /// across publishes.
    public class SnapshotTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void Snapshot_CarriesWorldStateAndAllEntities()
        {
            var h = new SimHarness();
            h.SpawnEntity("A");
            h.SpawnEntity("B");
            h.SeedClock(hour: 13, minute: 30);
            h.Step(2);

            var snap = SnapshotBuilder.Build(h.Ctx, 1.23);

            Assert.Equal(2, snap.Tick);
            Assert.Equal(13, snap.Hour);
            Assert.Equal(2, snap.Entities.Length);
            Assert.Equal(1.23, snap.WallClockSeconds, 5);
            Assert.False(snap.IsNight);
        }

        [Fact]
        public void Snapshot_OfLivingTown_ReflectsActivities()
        {
            if (!Available) return;
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            var town = TownLoader.Load(h.Ctx, maps.GetLocation("Daggerfall", "Gothway Garden"), blocks);
            h.SeedClock(hour: 5, minute: 30, timeScale: 60f);

            h.Step(120);    // 05:30 -> 07:30, breakfast hour

            var snap = SnapshotBuilder.Build(h.Ctx, 0);
            Assert.Equal(town.Civilians, snap.Entities.Length);

            int withActivity = 0;
            foreach (var e in snap.Entities)
            {
                Assert.Equal(EntityKind.CivilianNPC, e.Kind);
                if (e.Activity != ActivityKind.None) withActivity++;
            }
            Assert.Equal(town.Civilians, withActivity);
        }

        [Fact]
        public void PublishedSnapshots_AreDistinctObjects_PerTick()
        {
            var h = new SimHarness();
            h.SpawnEntity();
            h.SeedClock();
            h.Step();

            var first = SnapshotBuilder.Build(h.Ctx, 0);
            h.Step();
            var second = SnapshotBuilder.Build(h.Ctx, 0);

            Assert.NotSame(first, second);
            Assert.NotSame(first.Entities, second.Entities);
            Assert.Equal(first.Tick + 1, second.Tick);
        }
    }
}
