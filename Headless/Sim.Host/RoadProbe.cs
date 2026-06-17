using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Invents the road network Daggerfall never had. Stock DF has no wilderness
    /// roads — only location positions on the region map (longitude/latitude) and a
    /// per-pixel heightmap (WOODS.WLD) + terrain class (CLIMATE via MAPS.BSA). So a
    /// road network has to be DERIVED, not loaded: settlements are the nodes, and an
    /// edge is the least-cost path between two of them over the real terrain —
    /// following valleys, avoiding mountains, never crossing open sea.
    ///
    /// The cheapest terrain path IS the natural trade route, which is the whole point:
    /// the economy already reads climate/elevation off these same maps to decide what a
    /// settlement produces (RegionIndustry), so roads come out of the same substrate the
    /// trade does. This probe is the eyeball test — does the invented network look sane?
    /// It prints the settlement table, the minimum spanning tree (trunk roads), the
    /// candidate shortcuts it didn't take, and an ASCII map of the region.
    ///
    /// Deterministic: pure function of the map data. No sim, no RNG.
    public static class RoadProbe
    {
        // --- cost model (per map-pixel step) -------------------------------------
        const double LandCost = 1.0;        // ordinary ground
        const double MountainCost = 6.0;    // mountain/mountain-woods: a pass, not a wall
        const double SlopeK = 0.30;         // cost per unit of |elevation change| climbed
        const int Pad = 4;                  // map-pixel margin around the settlement bbox

        public static int Run(string regionName)
        {
            SimBootResult boot;
            try { boot = SimBoot.CreateRegion(DataProbe.Arena2Path, regionName, 600f); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

            var settlements = boot.Ctx.Settlements.All;
            if (settlements.Count == 0) { Console.Error.WriteLine("no settlements in " + regionName); return 1; }

            string arena2 = DataProbe.Arena2Path;
            var maps = new MapsFile(Path.Combine(arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var woods = new WoodsFile(Path.Combine(arena2, "WOODS.WLD"), FileUsage.UseMemory, true);

            // --- region pixel rectangle (bbox of settlements, padded) ------------
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var s in settlements)
            {
                if (s.MapPixelX < minX) minX = s.MapPixelX;
                if (s.MapPixelX > maxX) maxX = s.MapPixelX;
                if (s.MapPixelY < minY) minY = s.MapPixelY;
                if (s.MapPixelY > maxY) maxY = s.MapPixelY;
            }
            int x0 = Math.Max(MapsFile.MinMapPixelX, minX - Pad);
            int y0 = Math.Max(MapsFile.MinMapPixelY, minY - Pad);
            int x1 = Math.Min(MapsFile.MaxMapPixelX - 1, maxX + Pad);
            int y1 = Math.Min(MapsFile.MaxMapPixelY - 1, maxY + Pad);
            int W = x1 - x0 + 1, H = y1 - y0 + 1, N = W * H;

            // --- sample terrain into flat arrays ---------------------------------
            var elev = new int[N];
            var passable = new bool[N];
            var mountain = new bool[N];
            for (int gy = 0; gy < H; gy++)
                for (int gx = 0; gx < W; gx++)
                {
                    int px = x0 + gx, py = y0 + gy, idx = gy * W + gx;
                    elev[idx] = woods.GetHeightMapValue(px, py);
                    int clim = maps.GetClimateIndex(px, py);
                    bool ocean = clim == (int)MapsFile.Climates.Ocean;
                    mountain[idx] = clim == (int)MapsFile.Climates.Mountain
                                 || clim == (int)MapsFile.Climates.MountainWoods;
                    passable[idx] = !ocean;
                }

            // Settlement cells are always passable + ground-cost: a coastal town can
            // sit on a pixel the climate map calls sea, but you can still stand in it.
            int Cell(int px, int py) => (py - y0) * W + (px - x0);
            var nodeCell = new int[settlements.Count];
            for (int i = 0; i < settlements.Count; i++)
            {
                int c = Cell(settlements[i].MapPixelX, settlements[i].MapPixelY);
                nodeCell[i] = c;
                passable[c] = true;
                mountain[c] = false;
            }

            // --- Dijkstra from every settlement (cache dist + prev) --------------
            int n = settlements.Count;
            var dist = new double[n][];
            var prev = new int[n][];
            for (int i = 0; i < n; i++)
                (dist[i], prev[i]) = Dijkstra(nodeCell[i], W, H, elev, passable, mountain);

            // Pairwise least-cost (symmetrise: terrain cost is direction-agnostic, but
            // discretisation isn't, so take the cheaper of the two runs).
            double D(int i, int j) => Math.Min(dist[i][nodeCell[j]], dist[j][nodeCell[i]]);

            // --- Prim MST over settlements = trunk roads -------------------------
            var inTree = new bool[n];
            var best = new double[n];
            var from = new int[n];
            for (int i = 0; i < n; i++) { best[i] = double.PositiveInfinity; from[i] = -1; }
            best[0] = 0;
            var treeEdges = new List<(int a, int b, double cost)>();
            for (int it = 0; it < n; it++)
            {
                int u = -1; double bu = double.PositiveInfinity;
                for (int v = 0; v < n; v++) if (!inTree[v] && best[v] < bu) { bu = best[v]; u = v; }
                if (u < 0) break;          // remaining nodes unreachable (shouldn't happen on land)
                inTree[u] = true;
                if (from[u] >= 0) treeEdges.Add((from[u], u, best[u]));
                for (int v = 0; v < n; v++)
                {
                    if (inTree[v]) continue;
                    double d = D(u, v);
                    if (d < best[v]) { best[v] = d; from[v] = u; }
                }
            }

            // --- paint trunk roads onto the grid (reconstruct each edge's path) --
            var road = new bool[N];
            int roadCells = 0;
            foreach (var (a, b, _) in treeEdges)
            {
                // walk prev[a] from b's cell back to a's cell
                int cur = nodeCell[b], guard = N + 1;
                while (cur != nodeCell[a] && cur >= 0 && guard-- > 0)
                {
                    if (!road[cur]) { road[cur] = true; roadCells++; }
                    cur = prev[a][cur];
                }
            }

            // --- report ----------------------------------------------------------
            // Edge labels: letters only when they fit the symbol set, else #index.
            string Lab(int i) => n <= 62 ? Sym(i).ToString() : "#" + i;
            Console.WriteLine(regionName + " — " + n + " settlements, region pixels ["
                + x0 + ".." + x1 + "] x [" + y0 + ".." + y1 + "]  (" + W + "x" + H + ")");
            Console.WriteLine();
            Console.WriteLine("  sym  kind     name                          pixel       elev  climate");
            int tableRows = Math.Min(n, 40);
            for (int i = 0; i < tableRows; i++)
            {
                var s = settlements[i];
                Console.WriteLine("   " + Sym(i) + "   "
                    + s.Kind.ToString().PadRight(7) + "  "
                    + Trunc(s.Name, 28).PadRight(28) + "  "
                    + ("(" + s.MapPixelX + "," + s.MapPixelY + ")").PadRight(11) + " "
                    + s.Elevation.ToString().PadLeft(4) + "  "
                    + ClimateName(maps.GetClimateIndex(s.MapPixelX, s.MapPixelY)));
            }
            if (n > tableRows) Console.WriteLine("   ... (" + (n - tableRows) + " more settlements)");

            double trunkTotal = 0;
            foreach (var e in treeEdges) trunkTotal += e.cost;
            Console.WriteLine();
            Console.WriteLine("trunk roads (MST, " + treeEdges.Count + " edges, total cost "
                + trunkTotal.ToString("F1") + ", " + roadCells + " painted cells)"
                + (treeEdges.Count > 20 ? "; 20 most expensive:" : ":"));
            treeEdges.Sort((p, q) => q.cost.CompareTo(p.cost));   // costliest first (the long stitches)
            for (int k = 0; k < Math.Min(20, treeEdges.Count); k++)
            {
                var (a, b, cost) = treeEdges[k];
                Console.WriteLine("   " + Lab(a) + "—" + Lab(b) + "  cost " + cost.ToString("F1").PadLeft(7)
                    + "   " + Trunc(settlements[a].Name, 20) + " ↔ " + Trunc(settlements[b].Name, 20));
            }

            // Candidate shortcuts the MST skipped — what redundancy would add.
            var treeSet = new HashSet<(int, int)>();
            foreach (var e in treeEdges) treeSet.Add(e.a < e.b ? (e.a, e.b) : (e.b, e.a));
            var extras = new List<(int a, int b, double cost)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (!treeSet.Contains((i, j))) extras.Add((i, j, D(i, j)));
            extras.Sort((p, q) => p.cost.CompareTo(q.cost));
            Console.WriteLine();
            Console.WriteLine("nearest non-trunk pairs (candidate secondary roads):");
            for (int k = 0; k < Math.Min(5, extras.Count); k++)
                Console.WriteLine("   " + Lab(extras[k].a) + "—" + Lab(extras[k].b)
                    + "  cost " + extras[k].cost.ToString("F1").PadLeft(7));

            // --- ASCII map (downsampled to fit a terminal for big regions) -------
            const int MaxW = 120, MaxH = 64;
            int scale = Math.Max(1, Math.Max((W + MaxW - 1) / MaxW, (H + MaxH - 1) / MaxH));
            bool label = n <= 62;        // letters only when they fit the symbol set
            var settlementAt = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) settlementAt[nodeCell[i]] = i;

            Console.WriteLine();
            Console.WriteLine("legend:  ~ sea   . land   ^ mountain   + road   "
                + (label ? "A.. settlement" : "o settlement")
                + (scale > 1 ? "   (1 char = " + scale + "x" + scale + " pixels)" : ""));
            Console.WriteLine();
            var sb = new StringBuilder();
            for (int oy = 0; oy * scale < H; oy++)
            {
                sb.Clear();
                for (int ox = 0; ox * scale < W; ox++)
                {
                    // Summarise the scale×scale source block by priority:
                    // settlement > road > mountain > land > sea.
                    int siHit = -1; bool anyRoad = false, anyMtn = false, anyLand = false;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int gx = ox * scale + sx, gy = oy * scale + sy;
                            if (gx >= W || gy >= H) continue;
                            int idx = gy * W + gx;
                            if (settlementAt.TryGetValue(idx, out int si)) siHit = si;
                            if (road[idx]) anyRoad = true;
                            if (mountain[idx]) anyMtn = true;
                            else if (passable[idx]) anyLand = true;
                        }
                    if (siHit >= 0) sb.Append(label ? Sym(siHit) : 'o');
                    else if (anyRoad) sb.Append('+');
                    else if (anyMtn) sb.Append('^');
                    else if (anyLand) sb.Append('.');
                    else sb.Append('~');
                }
                Console.WriteLine("  " + sb);
            }
            return 0;
        }

        /// 8-neighbour Dijkstra over the pixel grid. Step cost = direction multiplier ×
        /// the destination's terrain base, plus a slope penalty on elevation climbed.
        static (double[] dist, int[] prev) Dijkstra(int src, int W, int H, int[] elev,
            bool[] passable, bool[] mountain)
        {
            int N = W * H;
            var dist = new double[N];
            var prev = new int[N];
            for (int i = 0; i < N; i++) { dist[i] = double.PositiveInfinity; prev[i] = -1; }
            dist[src] = 0;
            var pq = new PriorityQueue<int, double>();
            pq.Enqueue(src, 0);
            // 8 directions; diagonals cost √2 in distance.
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            double[] dm = { 1, 1, 1, 1, 1.41421356, 1.41421356, 1.41421356, 1.41421356 };
            while (pq.TryDequeue(out int cur, out double cd))
            {
                if (cd > dist[cur]) continue;
                int cx = cur % W, cy = cur / W;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + dx[k], ny = cy + dy[k];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int nb = ny * W + nx;
                    if (!passable[nb]) continue;
                    double baseCost = mountain[nb] ? MountainCost : LandCost;
                    double step = dm[k] * baseCost + SlopeK * Math.Abs(elev[nb] - elev[cur]);
                    double nd = cd + step;
                    if (nd < dist[nb]) { dist[nb] = nd; prev[nb] = cur; pq.Enqueue(nb, nd); }
                }
            }
            return (dist, prev);
        }

        static char Sym(int i)
        {
            if (i < 26) return (char)('A' + i);
            if (i < 52) return (char)('a' + (i - 26));
            return (char)('0' + (i - 52) % 10);
        }

        static string ClimateName(int clim) => clim switch
        {
            (int)MapsFile.Climates.Ocean => "Ocean",
            (int)MapsFile.Climates.Desert => "Desert",
            (int)MapsFile.Climates.Desert2 => "Desert",
            (int)MapsFile.Climates.Mountain => "Mountain",
            (int)MapsFile.Climates.Rainforest => "Rainforest",
            (int)MapsFile.Climates.Swamp => "Swamp",
            (int)MapsFile.Climates.Subtropical => "Subtropical",
            (int)MapsFile.Climates.MountainWoods => "MountainWoods",
            (int)MapsFile.Climates.Woodlands => "Woodlands",
            (int)MapsFile.Climates.HauntedWoodlands => "HauntedWoods",
            _ => "clim" + clim,
        };

        static string Trunc(string s, int nmax) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= nmax ? s : s.Substring(0, nmax));
    }
}
