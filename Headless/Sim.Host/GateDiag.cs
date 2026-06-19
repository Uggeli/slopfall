using System;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Inspects the baked gate geometry of one town: which blocks are WALL*,
    /// how many walkable cells each holds (the gate openings), and (in later
    /// tasks) the derived gate posts, the day/night path test, and guard posting.
    public static class GateDiag
    {
        public static int Run(string region, string location)
        {
            region ??= "Daggerfall";
            location ??= "Gallotale";
            Console.WriteLine($"gatecheck {region}/{location}…");
            var w = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, 600f, 12345);
            var g = w.TownGrid.Current;
            if (g == null) { Console.WriteLine("no town grid"); return 1; }

            int wallBlocks = 0, gateOpeningBlocks = 0;
            const int cells = TownGridData.CellsPerBlock;
            for (int by = 0; by < g.BlocksHigh; by++)
                for (int bx = 0; bx < g.BlocksWide; bx++)
                {
                    if (g.GateBlock == null || !g.GateBlock[by * g.BlocksWide + bx]) continue;
                    wallBlocks++;
                    int walk = 0;
                    for (int row = 0; row < cells; row++)
                        for (int col = 0; col < cells; col++)
                            if (g.Cost[(by * cells + row) * g.Width + (bx * cells + col)] > 0) walk++;
                    if (walk > 0) gateOpeningBlocks++;
                    Console.WriteLine($"  WALL block ({bx},{by}) walkable cells={walk}");
                }
            Console.WriteLine($"grid {g.Width}x{g.Height} cells, {g.BlocksWide}x{g.BlocksHigh} blocks; " +
                              $"WALL blocks={wallBlocks}, with gate openings={gateOpeningBlocks}");

            foreach (var st in w.Settlements.All)
            {
                Console.WriteLine($"settlement '{st.Name}' gate posts={st.GatePosts.Count}");
                for (int i = 0; i < st.GatePosts.Count; i++)
                    Console.WriteLine($"  gate {i}: ({st.GatePosts[i].X:F1}, {st.GatePosts[i].Z:F1})");

                if (st.GatePosts.Count > 0)
                {
                    var gate = st.GatePosts[0];
                    // an "outside" start: a walkable cell on the grid's edge nearest origin
                    float outX = g.WorldX(0), outZ = g.WorldZ(0);
                    var path = new System.Collections.Generic.List<PathPoint>();
                    bool dayOk = TownPathfinder.FindPath(g, outX, outZ, gate.X, gate.Z, path, true, blockGates: false);
                    int dayLen = path.Count;
                    bool nightOk = TownPathfinder.FindPath(g, outX, outZ, gate.X, gate.Z, path, true, blockGates: true);
                    int nightLen = path.Count;
                    Console.WriteLine($"  path to gate 0: day found={dayOk} len={dayLen}; curfew found={nightOk} len={nightLen}");
                }
            }
            int guards = 0;
            foreach (var kv in w.Employment.All)
            {
                var e = kv.Value;
                if (e == null || e.PublicOwner.IsNone) continue;
                guards++;
                Console.WriteLine($"  guard {kv.Key.Value}: gate={e.GateIndex} shift={(e.NightShift ? "night" : "day")} at ({e.GateX:F1},{e.GateZ:F1})");
            }
            Console.WriteLine($"total guards={guards}");

            return 0;
        }
    }
}
