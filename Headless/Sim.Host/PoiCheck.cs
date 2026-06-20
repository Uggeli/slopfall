using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Loads a whole region and reports the unified POI model: how many POIs of each
    /// role, how many have renderable exteriors, and a parity check that every settled
    /// POI links a SettlementData and the count matches the settlement registry.
    public static class PoiCheck
    {
        public static int Run(string region)
        {
            if (string.IsNullOrEmpty(region)) { Console.Error.WriteLine("usage: --poicheck <region>"); return 2; }

            var world = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, region, 600f, 12345);
            var pois = world.Pois.All;

            Console.WriteLine($"region {region}: {pois.Count} POIs");
            foreach (var g in pois.GroupBy(p => p.Role).OrderBy(g => g.Key.ToString()))
            {
                int withExt = g.Count(p => p.HasExterior);
                int settled = g.Count(p => p.Settlement != null);
                Console.WriteLine($"  {g.Key,-13} count={g.Count(),-4} exterior={withExt,-4} settlement={settled}");
            }

            int settledPois = pois.Count(p => p.Settlement != null);
            int settledRolePois = pois.Count(p => PoiRoles.IsSettled(p.Role) && p.HasExterior);
            int registrySettlements = world.Settlements.All.Count;
            bool ok = settledPois == registrySettlements && settledPois == settledRolePois;

            Console.WriteLine($"parity: settledPois={settledPois} registry={registrySettlements} settledRole+ext={settledRolePois} => {(ok ? "OK" : "MISMATCH")}");
            return ok ? 0 : 1;
        }
    }
}
