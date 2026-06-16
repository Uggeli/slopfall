using System;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Loads a whole region into one context and prints per-settlement counts — the
    /// Stage 2 smoke for the regional loader (no soak yet). Verifies settlements
    /// load, membership is tagged, and the combined grid is sized sanely.
    public static class RegionProbe
    {
        public static int Run(string regionName)
        {
            SimBootResult boot;
            try { boot = SimBoot.CreateRegion(DataProbe.Arena2Path, regionName, 600f); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

            var ctx = boot.Ctx;
            var r = boot.Region;
            Console.WriteLine(r.RegionName + " — " + r.Settlements + " settlements, "
                + r.Buildings + " buildings, " + r.Civilians + " civilians; combined grid "
                + r.BlocksWide + "x" + r.BlocksHigh + " blocks");
            Console.WriteLine();
            Console.WriteLine("  id  kind     name                          bldgs   civ  keep  labor guard");
            foreach (var s in ctx.Settlements.All)
            {
                int keepers = 0, laborers = 0, guards = 0;
                foreach (var rid in s.Residents)
                {
                    if (ctx.Residency.TryGet(rid, out var res) && res.Role == ResidentRole.Keeper) { keepers++; continue; }
                    if (ctx.Employment.TryGet(rid, out var emp))
                    {
                        if (!emp.PublicOwner.IsNone) guards++;
                        else if (!emp.Employer.IsNone) laborers++;
                    }
                }
                Console.WriteLine("  "
                    + s.Id.ToString().PadLeft(2) + "  "
                    + s.Kind.ToString().PadRight(7) + "  "
                    + Trunc(s.Name, 28).PadRight(28) + "  "
                    + s.Buildings.Count.ToString().PadLeft(5) + "  "
                    + s.Residents.Count.ToString().PadLeft(4) + "  "
                    + keepers.ToString().PadLeft(4) + "  "
                    + laborers.ToString().PadLeft(5) + " "
                    + guards.ToString().PadLeft(5));
            }
            Console.WriteLine();
            Console.WriteLine("businesses by settlement (keeper-bearing buildings):");
            foreach (var s in ctx.Settlements.All)
            {
                var hist = new System.Collections.Generic.SortedDictionary<string, int>();
                int houses = 0;
                foreach (var bi in s.Buildings)
                {
                    if (!ctx.Buildings.TryGet(bi, out var b)) continue;
                    if (IsHouse(b.Kind)) { houses++; continue; }
                    if (!IsBusiness(b.Kind)) continue;
                    string k = b.Kind.ToString();
                    hist.TryGetValue(k, out var c);
                    hist[k] = c + 1;
                }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in hist) { if (sb.Length > 0) sb.Append(", "); sb.Append(kv.Key).Append('x').Append(kv.Value); }
                Console.WriteLine("  " + Trunc(s.Name, 28).PadRight(28) + " houses=" + houses.ToString().PadLeft(3)
                    + "  | " + (sb.Length > 0 ? sb.ToString() : "(no businesses)"));
            }
            return 0;
        }

        static bool IsHouse(BuildingKind k) =>
            k == BuildingKind.House1 || k == BuildingKind.House2 || k == BuildingKind.House3 ||
            k == BuildingKind.House4 || k == BuildingKind.House5 || k == BuildingKind.House6;

        static bool IsBusiness(BuildingKind k) =>
            k == BuildingKind.Alchemist || k == BuildingKind.Armorer || k == BuildingKind.Bank ||
            k == BuildingKind.Bookseller || k == BuildingKind.ClothingStore || k == BuildingKind.FurnitureStore ||
            k == BuildingKind.GemStore || k == BuildingKind.GeneralStore || k == BuildingKind.Library ||
            k == BuildingKind.GuildHall || k == BuildingKind.PawnShop || k == BuildingKind.WeaponSmith ||
            k == BuildingKind.Temple || k == BuildingKind.Tavern;

        static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
    }
}
