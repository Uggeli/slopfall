using System.IO;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    // Integration: load a real town, step it, and confirm agent memory lives — every agent is
    // seeded, and perception writes THINGS records over the first game-minutes. (Category minting
    // needs a night sleep bout; that arc is observed in a full-day soak, asserted in isolation by
    // AgentMemoryRegistryTests.) ARENA2-gated.
    public class AgentMemoryLearningTests
    {
        static bool Arena2(out string path)
        {
            path = SimBoot.DefaultArena2Path;
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }

        [Fact]
        public void AgentsAreSeeded_AndPerceptionWritesRecords()
        {
            if (!Arena2(out var arena2)) return;

            var world = SimBoot.CreateTown(arena2, "Daggerfall", "Gothway Garden", 600f, 12345);
            world.ApplySeed();

            // Every civilian got an empty memory at spawn.
            Assert.True(world.AgentMemory.Count > 0, "expected agents to have seeded memory");

            // Step ~30 game-minutes of daytime; due agents perceive sensed neighbours -> records.
            for (int t = 0; t < 18000; t++) world.Step();

            long records = 0;
            int withRecords = 0;
            foreach (var kv in world.AgentMemory.All)
            {
                int c = kv.Value.Stores.Things.Count;
                records += c;
                if (c > 0) withRecords++;
            }
            Assert.True(records > 0, "perception should have written THINGS records");
            Assert.True(withRecords > 0, "at least one agent should have memory records");
        }
    }
}
