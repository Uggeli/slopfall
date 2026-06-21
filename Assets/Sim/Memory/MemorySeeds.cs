using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The species seed: a small set of INNATE category nodes the dictionary ships with, so tick
    /// one works — threat recognition exists before any learning and surprise has predictions to
    /// violate. Learning accretes beside the seed in the same store. The atom-type ids and
    /// valences here are scaffolding; the real Daggerfall percept vocabulary and per-species
    /// seeds are Phase-B content.
    /// </summary>
    public static class MemorySeeds
    {
        public enum SeedAtom
        {
            Predator = 1,
            Food = 2,
            Water = 3,
            Conspecific = 4,
        }

        /// <summary>Number of default seed nodes Install adds.</summary>
        public const int Count = 4;

        /// <summary>Install the default innate nodes into a store. Asserts the cap exceeds the
        /// seed size (the deferred A2 invariant) — INNATE nodes must always fit.</summary>
        public static void Install(MeaningsStore store)
        {
            if (store.Capacity < Count)
                throw new InvalidOperationException(
                    "MeaningsStore capacity " + store.Capacity + " < seed count " + Count);

            store.AddNode(Proto(SeedAtom.Predator), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), true);
            store.AddNode(Proto(SeedAtom.Food), Fixed.FromDouble(1.0), Fixed.FromDouble(0.9), true);
            store.AddNode(Proto(SeedAtom.Water), Fixed.FromDouble(0.6), Fixed.FromDouble(0.9), true);
            store.AddNode(Proto(SeedAtom.Conspecific), Fixed.FromDouble(0.1), Fixed.FromDouble(0.9), true);
        }

        static AtomBag Proto(SeedAtom atom)
            => AtomBag.Create(new[] { new Atom(new AtomTypeId((int)atom), Fixed.One) });
    }
}
