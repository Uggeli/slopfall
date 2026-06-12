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
    }

    /// Single-global town walkability. Written once by TownLoader at load.
    public sealed class TownGridRegistry
    {
        TownGridData _data;

        public TownGridData Current => Volatile.Read(ref _data);
        public void Set(TownGridData data) => Interlocked.Exchange(ref _data, data);
    }
}
