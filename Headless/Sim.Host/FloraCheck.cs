using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Loads a whole region and reports the flora registry: instance count, per-category
    /// and per-resource histograms, per-species counts, and the Unknown fraction (a high
    /// Unknown share means SpeciesCatalog gaps).
    public static class FloraCheck
    {
        public static int Run(string region)
        {
            if (string.IsNullOrEmpty(region)) { Console.Error.WriteLine("usage: --floracheck <region>"); return 2; }
            var world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, 600f, 12345);
            var flora = world.Flora.All;
            Console.WriteLine($"region {region}: {flora.Count} flora instances");

            Console.WriteLine("by category:");
            foreach (var g in flora.GroupBy(f => f.Category).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-10} {g.Count()}");
            Console.WriteLine("by resource:");
            foreach (var g in flora.GroupBy(f => f.Resource).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-10} {g.Count()}");

            int unknown = flora.Count(f => f.SpeciesId == 0);
            double pct = flora.Count == 0 ? 0 : 100.0 * unknown / flora.Count;
            Console.WriteLine($"unknown: {unknown}/{flora.Count} ({pct:0.0}%)");
            return flora.Count > 0 ? 0 : 1;
        }
    }
}
