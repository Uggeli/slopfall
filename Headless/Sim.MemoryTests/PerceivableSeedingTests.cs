using System.IO;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    // Integration: load a real town and confirm civilians get their identity atoms at spawn.
    // ARENA2-gated — skipped (passes vacuously) when DAGGERFALL_ARENA2 / its data is absent.
    public class PerceivableSeedingTests
    {
        static bool Arena2Available(out string path)
        {
            path = SimBoot.DefaultArena2Path;
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }

        [Fact]
        public void SeededCivilians_HaveIdentityAtoms()
        {
            if (!Arena2Available(out var arena2)) return;   // skip without data

            var world = SimBoot.CreateTown(arena2, "Daggerfall", "Gothway Garden", 600f, 12345);
            world.ApplySeed();

            // Sample the first civilian: kind + role atoms are always seeded.
            EntityId sample = default;
            bool found = false;
            foreach (var kv in world.Identity.All)
                if (kv.Value.Kind == EntityKind.CivilianNPC) { sample = kv.Key; found = true; break; }
            Assert.True(found, "expected at least one civilian NPC in the seeded town");

            var bag = world.Perceivable.Bag(sample);
            Assert.True(bag.Contains(PerceivableAtoms.Kind(EntityKind.CivilianNPC)), "civilian must carry a Kind atom");
            Assert.Contains(bag.Atoms, a => AtomCatalog.For(a.Type).Category == AtomCategory.Role);   // a role atom

            // Across the town, race atoms get seeded too (race >= 0).
            bool anyRace = false;
            foreach (var kv in world.Identity.All)
            {
                if (kv.Value.Kind != EntityKind.CivilianNPC) continue;
                if (world.Perceivable.Bag(kv.Key).Atoms.Any(a => AtomCatalog.For(a.Type).Category == AtomCategory.Race))
                { anyRace = true; break; }
            }
            Assert.True(anyRace, "at least one civilian should carry a Race atom");
        }
    }
}
