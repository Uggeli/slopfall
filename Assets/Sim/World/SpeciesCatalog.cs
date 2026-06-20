using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class SpeciesEntry
    {
        public string Name;
        public FloraCategory Category;
        public ResourceKind Resource;
    }

    /// Hand-authored (archive,record) -> species classification, authored by visual
    /// inspection of the flat atlases (see --flatsheet). Winter archives inherit their
    /// summer counterpart by record index. Unmapped -> Unknown (id 0).
    public static class SpeciesCatalog
    {
        // id 0 is always Unknown.
        static readonly List<SpeciesEntry> _entries = new()
        {
            new SpeciesEntry { Name = "Unknown",            Category = FloraCategory.Unknown,  Resource = ResourceKind.None   }, // 0
            new SpeciesEntry { Name = "Rainforest Palm",    Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 1
            new SpeciesEntry { Name = "Rainforest Tree",    Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 2
            new SpeciesEntry { Name = "Tree Fern",          Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 3
            new SpeciesEntry { Name = "Jungle Vine",        Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 4
            new SpeciesEntry { Name = "Flowering Plant",    Category = FloraCategory.Plant,    Resource = ResourceKind.Herb   }, // 5
            new SpeciesEntry { Name = "Jungle Boulder",     Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 6
            new SpeciesEntry { Name = "Subtropical Palm",   Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 7
            new SpeciesEntry { Name = "Cycad",              Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 8
            new SpeciesEntry { Name = "Succulent",          Category = FloraCategory.Plant,    Resource = ResourceKind.Herb   }, // 9
            new SpeciesEntry { Name = "Subtropical Boulder",Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 10
            new SpeciesEntry { Name = "Flowering Shrub",    Category = FloraCategory.Bush,     Resource = ResourceKind.Forage }, // 11
            new SpeciesEntry { Name = "Swamp Mangrove",     Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 12
            new SpeciesEntry { Name = "Weeping Willow",     Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 13
            new SpeciesEntry { Name = "Swamp Reed",         Category = FloraCategory.Plant,    Resource = ResourceKind.Reed   }, // 14
            new SpeciesEntry { Name = "Mossy Stone",        Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 15
            new SpeciesEntry { Name = "Swamp Snag",         Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 16
            new SpeciesEntry { Name = "Swamp Fern",         Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 17
            new SpeciesEntry { Name = "Saguaro Cactus",     Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 18
            new SpeciesEntry { Name = "Barrel Cactus",      Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 19
            new SpeciesEntry { Name = "Desert Palm",        Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 20
            new SpeciesEntry { Name = "Desert Snag",        Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 21
            new SpeciesEntry { Name = "Yucca",              Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 22
            new SpeciesEntry { Name = "Sand Rock",          Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 23
            new SpeciesEntry { Name = "Dry Shrub",          Category = FloraCategory.Bush,     Resource = ResourceKind.Forage }, // 24
            new SpeciesEntry { Name = "Oak",                Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 25
            new SpeciesEntry { Name = "Pine",               Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 26
            new SpeciesEntry { Name = "Woodland Bush",      Category = FloraCategory.Bush,     Resource = ResourceKind.Forage }, // 27
            new SpeciesEntry { Name = "Woodland Boulder",   Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 28
            new SpeciesEntry { Name = "Dead Tree",          Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 29
            new SpeciesEntry { Name = "Woodland Fern",      Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 30
            new SpeciesEntry { Name = "Tree Stump",         Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 31
            new SpeciesEntry { Name = "Hill Pine",          Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 32
            new SpeciesEntry { Name = "Hill Oak",           Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 33
            new SpeciesEntry { Name = "Rocky Outcrop",      Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 34
            new SpeciesEntry { Name = "Hill Shrub",         Category = FloraCategory.Bush,     Resource = ResourceKind.Forage }, // 35
            new SpeciesEntry { Name = "Gnarled Tree",       Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 36
            new SpeciesEntry { Name = "Haunted Snag",       Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 37
            new SpeciesEntry { Name = "Mushroom",           Category = FloraCategory.Plant,    Resource = ResourceKind.Herb   }, // 38
            new SpeciesEntry { Name = "Haunted Rock",       Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 39
            new SpeciesEntry { Name = "Twisted Root",       Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 40
            new SpeciesEntry { Name = "Mountain Pine",      Category = FloraCategory.Tree,     Resource = ResourceKind.Wood   }, // 41
            new SpeciesEntry { Name = "Rock Spire",         Category = FloraCategory.Rock,     Resource = ResourceKind.Stone  }, // 42
            new SpeciesEntry { Name = "Mountain Snag",      Category = FloraCategory.Deadwood, Resource = ResourceKind.Wood   }, // 43
            new SpeciesEntry { Name = "Mountain Grass",     Category = FloraCategory.Plant,    Resource = ResourceKind.Forage }, // 44
        };

        // (archive*1000 + record) -> species id. Authored from the contact sheets.
        static readonly Dictionary<int, int> _map = new()
        {
            // 500 RainForest (500r)
            { 500*1000 +  1,  5 }, { 500*1000 +  2,  4 }, { 500*1000 +  3,  2 }, { 500*1000 +  4,  6 },
            { 500*1000 +  5,  5 }, { 500*1000 +  6,  4 }, { 500*1000 +  7,  3 }, { 500*1000 +  9,  3 },
            { 500*1000 + 10,  2 }, { 500*1000 + 11,  1 }, { 500*1000 + 12,  1 }, { 500*1000 + 14,  1 },
            { 500*1000 + 16,  2 }, { 500*1000 + 17,  6 }, { 500*1000 + 18,  2 }, { 500*1000 + 19,  4 },
            { 500*1000 + 20,  5 }, { 500*1000 + 22,  5 }, { 500*1000 + 24,  5 }, { 500*1000 + 25,  2 },
            { 500*1000 + 26,  4 }, { 500*1000 + 27,  2 }, { 500*1000 + 28,  6 },

            // 501 SubTropical (501r)
            { 501*1000 +  1,  5 }, { 501*1000 +  2,  9 }, { 501*1000 +  3, 10 }, { 501*1000 +  4, 10 },
            { 501*1000 +  5,  7 }, { 501*1000 +  6, 10 }, { 501*1000 +  7,  8 }, { 501*1000 +  9,  7 },
            { 501*1000 + 10,  8 }, { 501*1000 + 11,  9 }, { 501*1000 + 12,  7 }, { 501*1000 + 13,  7 },
            { 501*1000 + 14,  8 }, { 501*1000 + 16,  8 }, { 501*1000 + 17,  8 }, { 501*1000 + 18,  8 },
            { 501*1000 + 20, 10 }, { 501*1000 + 22,  7 }, { 501*1000 + 25, 11 }, { 501*1000 + 28,  7 },

            // 502 Swamp (502r)
            { 502*1000 +  1,  5 }, { 502*1000 +  2,  9 }, { 502*1000 +  3, 15 }, { 502*1000 +  4, 15 },
            { 502*1000 +  5, 15 }, { 502*1000 +  6, 15 }, { 502*1000 +  8, 17 }, { 502*1000 +  9, 14 },
            { 502*1000 + 10, 15 }, { 502*1000 + 11,  6 }, { 502*1000 + 12, 13 }, { 502*1000 + 13, 12 },
            { 502*1000 + 16, 12 }, { 502*1000 + 17, 12 }, { 502*1000 + 18, 13 }, { 502*1000 + 19, 15 },
            { 502*1000 + 20, 14 }, { 502*1000 + 21, 14 }, { 502*1000 + 22, 17 }, { 502*1000 + 24, 14 },
            { 502*1000 + 25, 12 }, { 502*1000 + 27, 16 },

            // 503 Desert (503r)
            { 503*1000 +  1, 22 }, { 503*1000 +  3, 23 }, { 503*1000 +  4, 23 }, { 503*1000 +  5, 20 },
            { 503*1000 +  6, 21 }, { 503*1000 +  7, 24 }, { 503*1000 +  9, 18 }, { 503*1000 + 10, 24 },
            { 503*1000 + 11, 19 }, { 503*1000 + 13, 18 }, { 503*1000 + 14, 21 }, { 503*1000 + 16, 18 },
            { 503*1000 + 17, 22 }, { 503*1000 + 18, 23 }, { 503*1000 + 19, 23 }, { 503*1000 + 22, 24 },
            { 503*1000 + 25, 22 }, { 503*1000 + 27, 24 }, { 503*1000 + 28, 21 }, { 503*1000 + 30, 21 },

            // 504 TemperateWoodland (504c — authoritative)
            { 504*1000 +  1, 27 }, { 504*1000 +  3, 28 }, { 504*1000 +  4, 28 }, { 504*1000 +  5, 28 },
            { 504*1000 +  6, 28 }, { 504*1000 +  7, 30 }, { 504*1000 +  8, 30 }, { 504*1000 +  9, 30 },
            { 504*1000 + 10, 27 }, { 504*1000 + 11, 25 }, { 504*1000 + 12, 25 }, { 504*1000 + 13, 26 },
            { 504*1000 + 14, 25 }, { 504*1000 + 15, 25 }, { 504*1000 + 16, 25 }, { 504*1000 + 17, 25 },
            { 504*1000 + 18, 25 }, { 504*1000 + 19, 31 }, { 504*1000 + 20, 31 }, { 504*1000 + 21, 30 },
            { 504*1000 + 24, 29 }, { 504*1000 + 25, 26 }, { 504*1000 + 26, 30 }, { 504*1000 + 27, 27 },
            { 504*1000 + 28, 30 }, { 504*1000 + 30, 29 }, { 504*1000 + 31, 29 },

            // 506 WoodlandHills (506r)
            { 506*1000 +  1, 34 }, { 506*1000 +  3, 34 }, { 506*1000 +  4, 34 }, { 506*1000 +  6, 32 },
            { 506*1000 +  7, 34 }, { 506*1000 +  9, 30 }, { 506*1000 + 10, 35 }, { 506*1000 + 11, 33 },
            { 506*1000 + 12, 32 }, { 506*1000 + 13, 33 }, { 506*1000 + 14, 33 }, { 506*1000 + 16, 33 },
            { 506*1000 + 17, 34 }, { 506*1000 + 18, 34 }, { 506*1000 + 19, 31 }, { 506*1000 + 20, 31 },
            { 506*1000 + 24, 29 }, { 506*1000 + 25, 32 }, { 506*1000 + 30, 35 }, { 506*1000 + 31, 32 },

            // 508 HauntedWoodlands (508r)
            { 508*1000 +  1, 39 }, { 508*1000 +  2, 38 }, { 508*1000 +  4, 39 }, { 508*1000 +  5, 39 },
            { 508*1000 +  6, 39 }, { 508*1000 +  7, 39 }, { 508*1000 +  9, 30 }, { 508*1000 + 10, 36 },
            { 508*1000 + 11, 37 }, { 508*1000 + 12, 36 }, { 508*1000 + 14, 33 }, { 508*1000 + 16, 29 },
            { 508*1000 + 17, 40 }, { 508*1000 + 18, 36 }, { 508*1000 + 19, 36 }, { 508*1000 + 20, 36 },
            { 508*1000 + 22, 38 }, { 508*1000 + 23, 31 }, { 508*1000 + 24, 31 }, { 508*1000 + 25, 29 },
            { 508*1000 + 26, 40 }, { 508*1000 + 28, 31 }, { 508*1000 + 29, 36 }, { 508*1000 + 30, 29 },
            { 508*1000 + 31, 40 },

            // 510 Mountains (510r)
            { 510*1000 +  1, 42 }, { 510*1000 +  3, 42 }, { 510*1000 +  4, 42 }, { 510*1000 +  5, 41 },
            { 510*1000 +  6, 42 }, { 510*1000 +  9, 30 }, { 510*1000 + 11, 43 }, { 510*1000 + 12, 41 },
            { 510*1000 + 13, 41 }, { 510*1000 + 14, 41 }, { 510*1000 + 15, 42 }, { 510*1000 + 16, 33 },
            { 510*1000 + 17, 42 }, { 510*1000 + 18, 42 }, { 510*1000 + 24, 40 }, { 510*1000 + 25, 42 },
            { 510*1000 + 26, 42 }, { 510*1000 + 27, 43 }, { 510*1000 + 28, 36 }, { 510*1000 + 29, 33 },
            { 510*1000 + 30, 41 }, { 510*1000 + 31, 43 },
        };

        // Winter archive -> summer archive (same plants, snow variants).
        static readonly Dictionary<int, int> _winterToSummer = new()
        {
            { 505, 504 }, { 507, 506 }, { 509, 508 }, { 511, 510 },
        };

        static int Key(int archive, int record)
        {
            if (_winterToSummer.TryGetValue(archive, out var summer)) archive = summer;
            return archive * 1000 + record;
        }

        public static int SpeciesIdOf(int archive, int record)
            => _map.TryGetValue(Key(archive, record), out var id) ? id : 0;

        public static SpeciesEntry Lookup(int archive, int record)
            => _entries[SpeciesIdOf(archive, record)];
    }
}
