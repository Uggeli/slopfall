using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public struct PathPoint
    {
        public float X, Z;
    }

    /// Hierarchical town pathfinding over TownGridData, on the baked component graph
    /// (BlockConnectivity):
    ///
    ///   - MACRO: BFS over connected components — "which block-components must I cross,
    ///     and where" — bounded to one settlement (inter-settlement buffers are
    ///     unwalkable, so they disconnect the graph). Unreachable → fail; there is NO
    ///     full-grid fallback.
    ///   - FINE: A*/Dijkstra confined to ONE 64×64 block at a time, from the entry
    ///     crossing to any exit crossing toward the next component (multi-goal). Scratch
    ///     is block-local (64×64), so a search never allocates or touches the combined
    ///     grid — the cost is bounded by a block, not the region.
    ///
    /// A clear straight shot skips both layers (a Bresenham line is cheaper still).
    public static class TownPathfinder
    {
        const int NearWalkableRadius = 10;
        const int B = TownGridData.CellsPerBlock;   // 64

        /// Reusable, block-local fine-search buffers (64×64), generation-stamped so a
        /// reset is O(1). One PER THREAD ([ThreadStatic]) — a parallel MovementSystem
        /// needs no pathfinder changes.
        sealed class PathScratch
        {
            public readonly int[] GScore = new int[B * B];
            public readonly int[] CameFrom = new int[B * B];
            public readonly int[] Stamp = new int[B * B];
            public int Gen;
            public readonly BinaryHeap Open = new BinaryHeap();
            public readonly List<int> CellPath = new List<int>();   // stitched global-cell path (the output)
            public readonly List<int> Recon = new List<int>();      // per-segment reconstruct temp
            public readonly HashSet<int> Goals = new HashSet<int>(); // this segment's exit cells (global)
        }

        [ThreadStatic] static PathScratch _scratch;
        static PathScratch Scratch => _scratch ?? (_scratch = new PathScratch());

        /// `approachTarget`: append the exact destination as a final waypoint even if it
        /// sits on a blocked cell (a building door). Pass false for targets with no
        /// business inside a building (wandering) so strollers stop on walkable ground.
        public static bool FindPath(TownGridData g, float fromX, float fromZ, float toX, float toZ,
            List<PathPoint> result, bool approachTarget = true)
        {
            result.Clear();
            if (g == null || g.Cost == null) return false;

            if (!NearestWalkable(g, g.CellX(fromX), g.CellY(fromZ), out int sx, out int sy)) return false;
            if (!NearestWalkable(g, g.CellX(toX), g.CellY(toZ), out int gx, out int gy)) return false;

            // Straight-shot short-circuit: if walkable start and goal already see each
            // other across walkable ground, skip the graph entirely (most journeys at
            // soak timescale are short hops). Same arrival, so determinism holds.
            if (g.LineClear(g.WorldX(sx), g.WorldZ(sy), g.WorldX(gx), g.WorldZ(gy)))
            {
                result.Add(new PathPoint { X = g.WorldX(gx), Z = g.WorldZ(gy) });
                if (approachTarget) result.Add(new PathPoint { X = toX, Z = toZ });
                return true;
            }

            // Connectivity is a pure function of the immutable grid geometry, baked at
            // load (see the loaders). Read it; only fall back to a local build (never
            // stored) if somehow unbaked — so the read phase never mutates shared state.
            var conn = g.Connectivity ?? BlockConnectivity.Build(g);
            int startCell = sy * g.Width + sx, goalCell = gy * g.Width + gx;
            int fromComp = conn.CompOfCell(g, sx, sy);
            int toComp = conn.CompOfCell(g, gx, gy);
            if (fromComp < 0 || toComp < 0) return false;

            var s = Scratch;
            var path = s.CellPath;
            path.Clear();
            path.Add(startCell);

            if (fromComp == toComp)
            {
                // Same block-component: one bounded fine search to the goal.
                s.Goals.Clear(); s.Goals.Add(goalCell);
                if (FineInBlock(g, startCell, s, path) < 0) return false;
            }
            else
            {
                var route = conn.MacroRoute(fromComp, toComp);
                if (route == null) return false;        // unreachable — no full-grid fallback

                int entry = startCell;
                for (int i = 0; i < route.Count; i++)
                {
                    var link = route[i];
                    s.Goals.Clear();
                    for (int e = 0; e < link.ExitCells.Length; e++) s.Goals.Add(link.ExitCells[e]);
                    int reached = FineInBlock(g, entry, s, path);   // bounded to entry's block
                    if (reached < 0) return false;                  // intra-block break (shouldn't happen on a real route)
                    entry = reached + link.Delta;                   // step across the border into the next block
                    path.Add(entry);
                }
                s.Goals.Clear(); s.Goals.Add(goalCell);             // last block → the goal
                if (FineInBlock(g, entry, s, path) < 0) return false;
            }

            // Thin collinear runs into waypoints, then approach the exact target.
            int prevDx = int.MinValue, prevDy = int.MinValue;
            for (int i = 1; i < path.Count; i++)
            {
                int x0 = path[i - 1] % g.Width, y0 = path[i - 1] / g.Width;
                int x1 = path[i] % g.Width, y1 = path[i] / g.Width;
                int dx = x1 - x0, dy = y1 - y0;
                if (dx != prevDx || dy != prevDy)
                {
                    result.Add(new PathPoint { X = g.WorldX(x0), Z = g.WorldZ(y0) });
                    prevDx = dx; prevDy = dy;
                }
            }
            int lx = path[path.Count - 1] % g.Width, ly = path[path.Count - 1] / g.Width;
            result.Add(new PathPoint { X = g.WorldX(lx), Z = g.WorldZ(ly) });
            if (approachTarget)
                result.Add(new PathPoint { X = toX, Z = toZ });
            return true;
        }

        /// Dijkstra confined to the block containing `startCell`, to the nearest cell in
        /// `s.Goals` (global cell ids). Appends the reconstructed cells (excluding
        /// startCell, which the caller already placed) to `path`. Block-local scratch,
        /// so it never touches the combined grid. Returns the reached goal cell, or −1.
        static int FineInBlock(TownGridData g, int startCell, PathScratch s, List<int> path)
        {
            int W = g.Width;
            int sx = startCell % W, sy = startCell / W;
            int bx0 = (sx / B) * B, by0 = (sy / B) * B;     // block origin (cells)

            int gen = ++s.Gen;
            if (gen == int.MaxValue) { Array.Clear(s.Stamp, 0, s.Stamp.Length); s.Gen = 1; gen = 1; }
            var gScore = s.GScore; var cameFrom = s.CameFrom; var stamp = s.Stamp; var open = s.Open;
            open.Clear();

            int startLocal = (sy - by0) * B + (sx - bx0);
            gScore[startLocal] = 0; cameFrom[startLocal] = startLocal; stamp[startLocal] = gen;
            open.Push(startLocal, 0);

            int reached = -1;
            while (open.Count > 0)
            {
                int curLocal = open.Pop();
                int cx = bx0 + curLocal % B, cy = by0 + curLocal / B;
                int curGlobal = cy * W + cx;
                if (s.Goals.Contains(curGlobal)) { reached = curLocal; break; }

                StepFine(g, s, gen, bx0, by0, W, cx + 1, cy, curLocal);
                StepFine(g, s, gen, bx0, by0, W, cx - 1, cy, curLocal);
                StepFine(g, s, gen, bx0, by0, W, cx, cy + 1, curLocal);
                StepFine(g, s, gen, bx0, by0, W, cx, cy - 1, curLocal);
            }
            if (reached < 0) return -1;

            // Reconstruct reached → start (local), then append start→reached (global),
            // skipping the start cell the caller already added.
            var recon = s.Recon; recon.Clear();
            int c = reached;
            while (c != startLocal) { recon.Add(c); c = cameFrom[c]; }
            for (int i = recon.Count - 1; i >= 0; i--)
            {
                int local = recon[i];
                path.Add((by0 + local / B) * W + (bx0 + local % B));
            }
            return (by0 + reached / B) * W + (bx0 + reached % B);
        }

        static void StepFine(TownGridData g, PathScratch s, int gen, int bx0, int by0, int W, int nx, int ny, int curLocal)
        {
            if (nx < bx0 || nx >= bx0 + B || ny < by0 || ny >= by0 + B) return;   // bounded to this block
            byte cost = g.Cost[ny * W + nx];
            if (cost == 0) return;
            int nl = (ny - by0) * B + (nx - bx0);
            int t = s.GScore[curLocal] + cost;
            int ns = s.Stamp[nl] == gen ? s.GScore[nl] : int.MaxValue;
            if (t >= ns) return;
            s.GScore[nl] = t; s.CameFrom[nl] = curLocal; s.Stamp[nl] = gen;
            s.Open.Push(nl, t);     // Dijkstra: priority = gScore
        }

        /// Spiral out from (cx, cy) to the closest walkable cell.
        public static bool NearestWalkable(TownGridData g, int cx, int cy, out int x, out int y)
        {
            for (int r = 0; r <= NearWalkableRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        if (g.Walkable(cx + dx, cy + dy)) { x = cx + dx; y = cy + dy; return true; }
                    }
                }
            }
            x = y = 0;
            return false;
        }

        /// Minimal binary min-heap of (cell, priority). Reused across searches (Clear),
        /// duplicate pushes allowed; stale pops are filtered by gScore monotonicity.
        sealed class BinaryHeap
        {
            int[] _items = new int[64];
            int[] _priorities = new int[64];
            int _count;

            public int Count => _count;
            public void Clear() => _count = 0;

            public void Push(int item, int priority)
            {
                if (_count == _items.Length)
                {
                    Array.Resize(ref _items, _count * 2);
                    Array.Resize(ref _priorities, _count * 2);
                }
                _items[_count] = item;
                _priorities[_count] = priority;
                int i = _count++;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_priorities[p] <= _priorities[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                int top = _items[0];
                _count--;
                _items[0] = _items[_count];
                _priorities[0] = _priorities[_count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < _count && _priorities[l] < _priorities[smallest]) smallest = l;
                    if (r < _count && _priorities[r] < _priorities[smallest]) smallest = r;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                int t = _items[a]; _items[a] = _items[b]; _items[b] = t;
                t = _priorities[a]; _priorities[a] = _priorities[b]; _priorities[b] = t;
            }
        }
    }
}
