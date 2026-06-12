using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public struct PathPoint
    {
        public float X, Z;
    }

    /// Hierarchical town pathfinding over TownGridData.
    ///
    /// Coarse layer: A* across the block graph using precomputed BlockGates —
    /// "can this block be crossed from edge A to edge B" — so the search space
    /// is blocks, not cells. The same flags work at province scale later.
    /// Fine layer: A* over 1.6 m cells, restricted to the corridor of blocks
    /// the coarse path chose (falls back to the full grid if the corridor
    /// approximation fails, then gives up and lets the caller walk straight).
    public static class TownPathfinder
    {
        const int NearWalkableRadius = 10;

        public static bool FindPath(TownGridData g, float fromX, float fromZ, float toX, float toZ,
            List<PathPoint> result)
        {
            result.Clear();
            if (g == null || g.Cost == null) return false;

            if (!NearestWalkable(g, g.CellX(fromX), g.CellY(fromZ), out int sx, out int sy)) return false;
            if (!NearestWalkable(g, g.CellX(toX), g.CellY(toZ), out int gx, out int gy)) return false;

            List<int> cellPath = null;
            var corridor = CoarseCorridor(g, sx / TownGridData.CellsPerBlock, sy / TownGridData.CellsPerBlock,
                gx / TownGridData.CellsPerBlock, gy / TownGridData.CellsPerBlock);
            if (corridor != null)
                cellPath = FineAStar(g, sx, sy, gx, gy, corridor);
            if (cellPath == null)
                cellPath = FineAStar(g, sx, sy, gx, gy, null);      // corridor lied; search everything
            if (cellPath == null) return false;

            // Thin collinear runs into waypoints, then approach the exact
            // target (typically a building door on a blocked cell).
            int prevDx = int.MinValue, prevDy = int.MinValue;
            for (int i = 1; i < cellPath.Count; i++)
            {
                int x0 = cellPath[i - 1] % g.Width, y0 = cellPath[i - 1] / g.Width;
                int x1 = cellPath[i] % g.Width, y1 = cellPath[i] / g.Width;
                int dx = x1 - x0, dy = y1 - y0;
                if (dx != prevDx || dy != prevDy)
                {
                    result.Add(new PathPoint { X = g.WorldX(x0), Z = g.WorldZ(y0) });
                    prevDx = dx; prevDy = dy;
                }
            }
            int lx = cellPath[cellPath.Count - 1] % g.Width, ly = cellPath[cellPath.Count - 1] / g.Width;
            result.Add(new PathPoint { X = g.WorldX(lx), Z = g.WorldZ(ly) });
            result.Add(new PathPoint { X = toX, Z = toZ });
            return true;
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
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                        if (g.Walkable(cx + dx, cy + dy)) { x = cx + dx; y = cy + dy; return true; }
                    }
                }
            }
            x = y = 0;
            return false;
        }

        // ---- Coarse layer ----

        // Side indices: 0=N (y-), 1=S (y+), 2=E (x+), 3=W (x-).
        static readonly int[] SideDx = { 0, 0, 1, -1 };
        static readonly int[] SideDy = { -1, 1, 0, 0 };
        static readonly int[] Opposite = { 1, 0, 3, 2 };

        static bool GateConnects(BlockGates gates, int sideA, int sideB)
        {
            if (sideA > sideB) { int t = sideA; sideA = sideB; sideB = t; }
            if (sideA == 0 && sideB == 1) return (gates & BlockGates.NS) != 0;
            if (sideA == 0 && sideB == 2) return (gates & BlockGates.NE) != 0;
            if (sideA == 0 && sideB == 3) return (gates & BlockGates.NW) != 0;
            if (sideA == 1 && sideB == 2) return (gates & BlockGates.SE) != 0;
            if (sideA == 1 && sideB == 3) return (gates & BlockGates.SW) != 0;
            return (gates & BlockGates.EW) != 0;                    // E-W
        }

        static bool TouchesSide(BlockGates gates, int side)
        {
            switch (side)
            {
                case 0: return (gates & (BlockGates.NS | BlockGates.NE | BlockGates.NW)) != 0;
                case 1: return (gates & (BlockGates.NS | BlockGates.SE | BlockGates.SW)) != 0;
                case 2: return (gates & (BlockGates.NE | BlockGates.SE | BlockGates.EW)) != 0;
                default: return (gates & (BlockGates.NW | BlockGates.SW | BlockGates.EW)) != 0;
            }
        }

        /// BFS over (block, entry-side) states; returns the set of block
        /// indices on a shortest coarse route, or null if blocks disconnect.
        static HashSet<int> CoarseCorridor(TownGridData g, int sbx, int sby, int gbx, int gby)
        {
            int bw = g.BlocksWide, bh = g.BlocksHigh;
            if (sbx == gbx && sby == gby)
                return new HashSet<int> { sby * bw + sbx };

            // state = (block * 4 + entrySide); -1 parent = unvisited.
            var parent = new int[bw * bh * 4];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            var queue = new Queue<int>();

            // From the start block we may leave via any side its gates touch.
            for (int side = 0; side < 4; side++)
            {
                var gates = g.Gates[sby * bw + sbx];
                if (!TouchesSide(gates, side)) continue;
                int nbx = sbx + SideDx[side], nby = sby + SideDy[side];
                if (nbx < 0 || nbx >= bw || nby < 0 || nby >= bh) continue;
                int entry = Opposite[side];
                int state = (nby * bw + nbx) * 4 + entry;
                if (parent[state] >= 0) continue;
                parent[state] = state;          // root marker
                queue.Enqueue(state);
            }

            while (queue.Count > 0)
            {
                int state = queue.Dequeue();
                int block = state / 4, entry = state % 4;
                int bx = block % bw, by = block / bw;

                if (bx == gbx && by == gby)
                    return CollectCorridor(parent, state, bw, sby * bw + sbx);

                var gates = g.Gates[block];
                for (int exit = 0; exit < 4; exit++)
                {
                    if (exit == entry || !GateConnects(gates, entry, exit)) continue;
                    int nbx = bx + SideDx[exit], nby = by + SideDy[exit];
                    if (nbx < 0 || nbx >= bw || nby < 0 || nby >= bh) continue;
                    int next = (nby * bw + nbx) * 4 + Opposite[exit];
                    if (parent[next] >= 0) continue;
                    parent[next] = state;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        static HashSet<int> CollectCorridor(int[] parent, int endState, int bw, int startBlock)
        {
            var corridor = new HashSet<int> { startBlock };
            int state = endState;
            while (true)
            {
                corridor.Add(state / 4);
                if (parent[state] == state) break;
                state = parent[state];
            }
            return corridor;
        }

        // ---- Fine layer ----

        /// A* over cells (4-neighbor). `corridor` of allowed block indices, or
        /// null for unrestricted. Returns cell-index path including endpoints.
        static List<int> FineAStar(TownGridData g, int sx, int sy, int gx, int gy, HashSet<int> corridor)
        {
            int w = g.Width, h = g.Height;
            int start = sy * w + sx, goal = gy * w + gx;
            int cellsPerBlock = TownGridData.CellsPerBlock;

            var gScore = new int[w * h];
            for (int i = 0; i < gScore.Length; i++) gScore[i] = int.MaxValue;
            var cameFrom = new int[w * h];
            var open = new BinaryHeap(w * h);

            gScore[start] = 0;
            cameFrom[start] = start;
            open.Push(start, Heuristic(sx, sy, gx, gy));

            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (current == goal)
                    return Reconstruct(cameFrom, goal, start);

                int cx = current % w, cy = current / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d], ny = cy + dy[d];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    int next = ny * w + nx;
                    byte cost = g.Cost[next];
                    if (cost == 0) continue;
                    if (corridor != null
                        && !corridor.Contains((ny / cellsPerBlock) * g.BlocksWide + nx / cellsPerBlock))
                        continue;

                    int tentative = gScore[current] + cost;
                    if (tentative >= gScore[next]) continue;
                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    open.Push(next, tentative + Heuristic(nx, ny, gx, gy));
                }
            }
            return null;
        }

        static int Heuristic(int x, int y, int gx, int gy) =>
            System.Math.Abs(x - gx) + System.Math.Abs(y - gy);      // min cost is 1 (roads)

        static List<int> Reconstruct(int[] cameFrom, int goal, int start)
        {
            var path = new List<int>();
            int current = goal;
            while (current != start)
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Add(start);
            path.Reverse();
            return path;
        }

        /// Minimal binary min-heap of (cellIndex, priority). Duplicate pushes
        /// allowed; stale pops are filtered by gScore monotonicity upstream.
        sealed class BinaryHeap
        {
            int[] _items;
            int[] _priorities;
            int _count;

            public BinaryHeap(int capacity)
            {
                _items = new int[64];
                _priorities = new int[64];
            }

            public int Count => _count;

            public void Push(int item, int priority)
            {
                if (_count == _items.Length)
                {
                    System.Array.Resize(ref _items, _count * 2);
                    System.Array.Resize(ref _priorities, _count * 2);
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
