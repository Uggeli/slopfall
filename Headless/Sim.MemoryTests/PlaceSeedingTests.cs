using System.IO;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    // Integration: load a real town; each civilian's PLACES memory is seeded with the
    // KIND atom of every building it knows (the innate "what kind of place is that" layer
    // the learned facts accrete on top of). ARENA2-gated — passes vacuously without data.
    public class PlaceSeedingTests
    {
        static bool Arena2Available(out string path)
        {
            path = SimBoot.DefaultArena2Path;
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }

        [Fact]
        public void SeededCivilians_HavePlaceKindAtoms()
        {
            if (!Arena2Available(out var arena2)) return;   // skip without data

            var world = SimBoot.CreateTown(arena2, "Daggerfall", "Gothway Garden", 600f, 12345);
            world.ApplySeed();

            bool anyPlaces = false, anyKindAtom = false;
            foreach (var kv in world.Identity.All)
            {
                if (kv.Value.Kind != EntityKind.CivilianNPC) continue;
                if (!world.AgentMemory.TryGet(kv.Key, out var mem)) continue;
                var places = mem.Stores.Places;
                if (places.Count == 0) continue;
                anyPlaces = true;

                // A seeded record carries a building-kind atom (KindBase..Provisions).
                for (int i = 0; i < places.Count && !anyKindAtom; i++)
                    if (places[i].DeltaBag.Atoms.Any(a => a.Type.Value >= PlaceAtoms.KindBase
                                                       && a.Type.Value < PlaceAtoms.Provisions.Value))
                        anyKindAtom = true;
                if (anyKindAtom) break;
            }

            Assert.True(anyPlaces, "expected at least one civilian with seeded PLACES memory");
            Assert.True(anyKindAtom, "a seeded place record must carry a building-kind atom (>= KindBase)");
        }
    }
}
