using System;

namespace DaggerfallWorkshop.Sim
{
    /// Deterministic, seeded RNG owned by the sim. System.Random under the hood,
    /// not thread-safe — give each parallel system its own instance if you split
    /// Update across threads later.
    public sealed class SimRandom
    {
        readonly Random _rng;

        public int Seed { get; }

        public SimRandom(int seed)
        {
            Seed = seed;
            _rng = new Random(seed);
        }

        public int NextInt() => _rng.Next();
        public int NextInt(int maxExclusive) => _rng.Next(maxExclusive);
        public int NextInt(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
        public double NextDouble() => _rng.NextDouble();
        public float NextFloat() => (float)_rng.NextDouble();
        public bool NextBool() => _rng.Next(2) == 1;
    }
}
