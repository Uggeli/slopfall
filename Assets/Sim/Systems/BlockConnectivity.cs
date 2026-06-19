using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The baked reachability graph behind hierarchical pathfinding. Built ONCE per
    /// loaded grid (the world is static after load): each 64×64 block is flood-filled
    /// into walkable connected COMPONENTS, and components in adjacent blocks are linked
    /// where their shared border cells actually touch. So the graph encodes *real*
    /// connectivity — not the whole-block gate summary, which can claim a crossing via
    /// a region an agent isn't in (the old "corridor lied → full-grid fallback").
    ///
    /// A query is then: which component is the start in, which is the goal in, is there
    /// a route between them (macro BFS over components — bounded to one settlement, since
    /// the inter-settlement buffers are unwalkable and so disconnect the graph), and what
    /// border cells cross from each component toward the next. The fine search only ever
    /// runs *inside one block* between those crossings, never across the combined grid.
    public sealed class BlockConnectivity
    {
        public struct Link
        {
            public int To;            // destination global component
            public int Delta;         // entryCell = exitCell + Delta (constant: ±1 or ±Width per shared edge)
            public int[] ExitCells;   // cells in THIS block on the shared border that cross to `To` (multi-goal)
        }

        const int B = TownGridData.CellsPerBlock;   // 64
        const byte Blocked = 255;

        readonly byte[][] _blockComp;   // [combined block index] → 64×64 local component ids (255 = blocked); null = no block
        readonly int[] _base;           // [block] → global id of its component 0
        readonly List<Link>[] _links;   // [global component] → outgoing links
        public int Count { get; }

        // Reusable macro-BFS buffers — one set per thread so MovementSystem and
        // CreatureSystem can both pathfind from the parallel engine phase without
        // racing on the same BFS state. Generation-stamped so reset is O(1).
        sealed class MacroBfs
        {
            public int[] Stamp;
            public int Gen;
            public int[] PrevComp;
            public Link[] PrevLink;
            public readonly Queue<int> Queue = new Queue<int>();
            public readonly List<Link> Route = new List<Link>();
        }
        [System.ThreadStatic] static MacroBfs _bfs;

        BlockConnectivity(byte[][] blockComp, int[] baseArr, List<Link>[] links, int count)
        {
            _blockComp = blockComp; _base = baseArr; _links = links; Count = count;
        }

        /// Global component a cell belongs to, or −1 if blocked / no block.
        public int CompOfCell(TownGridData g, int cx, int cy)
        {
            int bx = cx / B, by = cy / B;
            int block = by * g.BlocksWide + bx;
            var bc = _blockComp[block];
            if (bc == null) return -1;
            byte c = bc[(cy % B) * B + (cx % B)];
            return c == Blocked ? -1 : _base[block] + c;
        }

        public IReadOnlyList<Link> LinksOf(int comp) => _links[comp] ?? EmptyLinks;
        static readonly List<Link> EmptyLinks = new List<Link>();

        /// Shortest component route (min block-crossings) from `from` to `to`, as the
        /// ordered links to traverse — or null if unreachable (NO full-grid fallback).
        /// BFS, bounded to the start's settlement (buffers disconnect the rest).
        /// Uses [ThreadStatic] BFS state so MovementSystem and CreatureSystem can
        /// both call this concurrently from the parallel engine phase.
        public List<Link> MacroRoute(int from, int to)
        {
            var s = _bfs ?? (_bfs = new MacroBfs());
            // Lazily size the per-thread arrays to this connectivity graph's count.
            if (s.Stamp == null || s.Stamp.Length < Count)
            {
                s.Stamp    = new int[Count];
                s.PrevComp = new int[Count];
                s.PrevLink = new Link[Count];
            }

            s.Route.Clear();
            if (from == to) return s.Route;

            int gen = ++s.Gen;
            s.Queue.Clear();
            s.Stamp[from] = gen; s.Queue.Enqueue(from);

            bool found = false;
            while (s.Queue.Count > 0)
            {
                int comp = s.Queue.Dequeue();
                if (comp == to) { found = true; break; }
                var links = _links[comp];
                if (links == null) continue;
                for (int i = 0; i < links.Count; i++)
                {
                    int nxt = links[i].To;
                    if (s.Stamp[nxt] == gen) continue;
                    s.Stamp[nxt] = gen;
                    s.PrevComp[nxt] = comp;
                    s.PrevLink[nxt] = links[i];
                    s.Queue.Enqueue(nxt);
                }
            }
            if (!found) return null;

            // Walk parents back, collecting the links, then reverse into forward order.
            int cur = to;
            while (cur != from) { s.Route.Add(s.PrevLink[cur]); cur = s.PrevComp[cur]; }
            s.Route.Reverse();
            return s.Route;
        }

        /// Flood every block into components and stitch the cross-block graph. Derives
        /// everything from the combined cost grid, so it works for any grid (loaded or
        /// synthetic test) and needs nothing precomputed.
        public static BlockConnectivity Build(TownGridData g)
        {
            int bw = g.BlocksWide, bh = g.BlocksHigh, blocks = bw * bh, W = g.Width;
            var blockComp = new byte[blocks][];
            var baseArr = new int[blocks];
            int total = 0;

            var q = new Queue<int>();
            for (int b = 0; b < blocks; b++)
            {
                baseArr[b] = total;
                int n = FloodBlock(g, (b % bw) * B, (b / bw) * B, q, out var comp);
                if (n == 0) { blockComp[b] = null; continue; }
                blockComp[b] = comp;
                total += n;
            }

            // Accumulate crossings per (fromComp,toComp), then materialize into links.
            var exits = new Dictionary<long, List<int>>();
            var delta = new Dictionary<long, int>();

            // Horizontal neighbours: block A's east column ↔ block B's west column.
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx + 1 < bw; bx++)
                {
                    int a = by * bw + bx, bb = by * bw + bx + 1;
                    if (blockComp[a] == null || blockComp[bb] == null) continue;
                    for (int r = 0; r < B; r++)
                    {
                        int cellA = (by * B + r) * W + (bx * B + B - 1);
                        int cellB = cellA + 1;
                        if (g.Cost[cellA] == 0 || g.Cost[cellB] == 0) continue;
                        int ca = baseArr[a] + blockComp[a][r * B + (B - 1)];
                        int cbv = baseArr[bb] + blockComp[bb][r * B + 0];
                        AddCross(exits, delta, total, ca, cbv, cellA, +1);
                        AddCross(exits, delta, total, cbv, ca, cellB, -1);
                    }
                }

            // Vertical neighbours: block A's south row ↔ block B's north row.
            for (int by = 0; by + 1 < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int a = by * bw + bx, bb = (by + 1) * bw + bx;
                    if (blockComp[a] == null || blockComp[bb] == null) continue;
                    for (int c = 0; c < B; c++)
                    {
                        int cellA = (by * B + B - 1) * W + (bx * B + c);
                        int cellB = cellA + W;
                        if (g.Cost[cellA] == 0 || g.Cost[cellB] == 0) continue;
                        int ca = baseArr[a] + blockComp[a][(B - 1) * B + c];
                        int cbv = baseArr[bb] + blockComp[bb][0 * B + c];
                        AddCross(exits, delta, total, ca, cbv, cellA, +W);
                        AddCross(exits, delta, total, cbv, ca, cellB, -W);
                    }
                }

            var links = new List<Link>[total];
            foreach (var kv in exits)
            {
                int from = (int)(kv.Key / total), to = (int)(kv.Key % total);
                (links[from] ?? (links[from] = new List<Link>()))
                    .Add(new Link { To = to, Delta = delta[kv.Key], ExitCells = kv.Value.ToArray() });
            }

            return new BlockConnectivity(blockComp, baseArr, links, total);
        }

        static void AddCross(Dictionary<long, List<int>> exits, Dictionary<long, int> delta,
            int total, int from, int to, int exitCell, int d)
        {
            long key = (long)from * total + to;
            if (!exits.TryGetValue(key, out var list)) { list = new List<int>(); exits[key] = list; delta[key] = d; }
            list.Add(exitCell);
        }

        /// Flood one block's walkable cells into components; writes a fresh 64×64 byte
        /// grid (255 = blocked). Returns the component count (0 = no walkable cell).
        static int FloodBlock(TownGridData g, int ox, int oy, Queue<int> q, out byte[] comp)
        {
            comp = new byte[B * B];
            for (int i = 0; i < comp.Length; i++) comp[i] = Blocked;
            int W = g.Width;
            byte next = 0;
            for (int seed = 0; seed < B * B; seed++)
            {
                if (comp[seed] != Blocked) continue;
                if (g.Cost[(oy + seed / B) * W + (ox + seed % B)] == 0) continue;
                if (next >= Blocked) break;     // ≥255 components in one block: impossible in practice
                byte id = next++;
                comp[seed] = id; q.Clear(); q.Enqueue(seed);
                while (q.Count > 0)
                {
                    int idx = q.Dequeue(); int x = idx % B, y = idx / B;
                    FloodNbr(g, comp, q, ox, oy, W, x - 1, y, id);
                    FloodNbr(g, comp, q, ox, oy, W, x + 1, y, id);
                    FloodNbr(g, comp, q, ox, oy, W, x, y - 1, id);
                    FloodNbr(g, comp, q, ox, oy, W, x, y + 1, id);
                }
            }
            return next;
        }

        static void FloodNbr(TownGridData g, byte[] comp, Queue<int> q, int ox, int oy, int W, int x, int y, byte id)
        {
            if (x < 0 || x >= B || y < 0 || y >= B) return;
            int local = y * B + x;
            if (comp[local] != Blocked) return;
            if (g.Cost[(oy + y) * W + (ox + x)] == 0) return;
            comp[local] = id; q.Enqueue(local);
        }
    }
}
