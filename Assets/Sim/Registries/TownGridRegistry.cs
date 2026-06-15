using System;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Which edges of a block connect to each other through walkable ground —
    /// the precomputed BFS result that makes the coarse path layer cheap.
    /// Computed once per block TEMPLATE (block name), so the whole province
    /// reuses the same ~1300 entries.
    [Flags]
    public enum BlockGates
    {
        None = 0,
        NS = 1 << 0,
        NE = 1 << 1,
        NW = 1 << 2,
        SE = 1 << 3,
        SW = 1 << 4,
        EW = 1 << 5,
    }

    /// Walkability of the loaded town at automap resolution (64 cells per
    /// block side, 1.6 m per cell — same source DFU's CityNavigation uses):
    /// Cost[y * Width + x], 0 = blocked, else step cost (roads cheapest).
    /// Plus per-block gate flags for the coarse path layer.
    public sealed class TownGridData
    {
        public const float CellSize = 1.6f;
        public const int CellsPerBlock = 64;

        public int Width, Height;           // cells
        public int BlocksWide, BlocksHigh;
        public int RegionIndex;             // classic region of the loaded town (holidays)
        public byte[] Cost;                 // Width * Height
        public BlockGates[] Gates;          // BlocksWide * BlocksHigh

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public byte CostAt(int x, int y) => Cost[y * Width + x];
        public bool Walkable(int x, int y) => InBounds(x, y) && Cost[y * Width + x] > 0;

        public int CellX(float worldX) => (int)(worldX / CellSize);
        public int CellY(float worldZ) => (int)(worldZ / CellSize);
        public float WorldX(int cellX) => (cellX + 0.5f) * CellSize;
        public float WorldZ(int cellY) => (cellY + 0.5f) * CellSize;

        /// Grid line-of-sight: true if no wall sits strictly between the two
        /// world points. Bresenham over cells, blocked by any non-walkable
        /// (or out-of-bounds) cell. Endpoints aren't tested — an agent may
        /// stand on a doorway/edge cell. Headless stand-in for a raycast.
        public bool LineClear(float ax, float az, float bx, float bz)
        {
            int x0 = CellX(ax), y0 = CellY(az), x1 = CellX(bx), y1 = CellY(bz);
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            while (x != x1 || y != y1)
            {
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
                if (x == x1 && y == y1) break;      // reached target — don't test the endpoint
                if (!Walkable(x, y)) return false;  // a wall (or off-map) blocks sight
            }
            return true;
        }
    }

    /// Single-global town walkability. Written once by TownLoader at load.
    public sealed class TownGridRegistry
    {
        TownGridData _data;

        public TownGridData Current => Volatile.Read(ref _data);
        public void Set(TownGridData data) => Interlocked.Exchange(ref _data, data);
    }
}
