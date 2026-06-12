using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// EntityDetail is the inspector payload every spectator client renders —
    /// pin its shape against a living town.
    public class InspectorTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void UnknownEntity_ReturnsNull()
        {
            var h = new SimHarness();
            Assert.Null(Inspector.Inspect(h.Ctx, new EntityId(9999)));
        }

        [Fact]
        public void LivingCivilian_HasFullDetail()
        {
            if (!Available) return;
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            TownLoader.Load(h.Ctx, maps.GetLocation("Daggerfall", "Gothway Garden"), blocks);
            h.SeedClock(hour: 5, minute: 30, timeScale: 60f);
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });

            h.Step(240);    // 05:30 -> 09:30, enough for breakfast crowds to mingle

            var detail = Inspector.Inspect(h.Ctx, new EntityId(1));
            Assert.NotNull(detail);
            Assert.Equal(1, detail.Id);
            Assert.False(string.IsNullOrEmpty(detail.Name));
            Assert.False(string.IsNullOrEmpty(detail.Role));
            Assert.False(string.IsNullOrEmpty(detail.Activity));
            Assert.True(detail.HomeBuilding >= 0);
            Assert.InRange(detail.Hunger, 0, 1.5);
            Assert.InRange(detail.Energy, 0, 1.5);

            // After a social morning the town must contain SOMEONE with
            // relations and memories; find one and check ordering/shape.
            EntityDetail social = null;
            foreach (var kv in h.Ctx.Relations.All)
            {
                social = Inspector.Inspect(h.Ctx, kv.Key);
                if (social != null && social.Relations.Count >= 2) break;
            }
            Assert.NotNull(social);
            Assert.True(social.Relations.Count >= 2);
            // Sorted by familiarity, descending.
            for (int i = 1; i < social.Relations.Count; i++)
                Assert.True(social.Relations[i - 1].Familiarity >= social.Relations[i].Familiarity);
            Assert.False(string.IsNullOrEmpty(social.Relations[0].OtherName));

            bool someoneRemembers = false;
            foreach (var kv in h.Ctx.Memory.All)
            {
                var d = Inspector.Inspect(h.Ctx, kv.Key);
                if (d != null && d.Memories.Count > 0)
                {
                    someoneRemembers = true;
                    Assert.False(string.IsNullOrEmpty(d.Memories[0].Kind));
                    Assert.False(string.IsNullOrEmpty(d.Memories[0].OtherName));
                    break;
                }
            }
            Assert.True(someoneRemembers);
        }
    }
}
