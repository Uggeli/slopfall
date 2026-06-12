using System.Collections.Generic;
using DaggerfallConnect;

namespace DaggerfallWorkshop.Sim
{
    /// Extracts a 64x64 walk-cost grid + edge-gate flags from one RMB block.
    /// Semantics match DFU's CityNavigation: AutoMapData != 0 means the cell
    /// is covered (building/model/flat) and blocked; ground tile records give
    /// terrain cost (water impassable, roads cheapest).
    ///
    /// Results are cached per block NAME — blocks are templates shared across
    /// every town in the province, so the cache tops out at the ~1300 RMB
    /// records in BLOCKS.BSA no matter how much world is loaded.
    public static class BlockWalkability
    {
        public const int Dim = TownGridData.CellsPerBlock;      // 64

        // Ground tile records, classic archive numbering (see CityNavigation).
        const int Water = 0, WaterDirtEdge1 = 5, WaterDirtEdge2 = 6;
        const int WaterGrassEdge1 = 20, WaterGrassEdge2 = 21;
        const int WaterStoneEdge1 = 30, WaterStoneEdge2 = 31;
        const int Stone = 1, Dirt = 2, Grass = 3;
        const int Road = 46, RoadCornerDirt = 47, RoadCornerGrass = 55;

        public sealed class Result
        {
            public byte[] Cost = new byte[Dim * Dim];   // row-major, y * Dim + x
            public BlockGates Gates;
        }

        static readonly Dictionary<string, Result> _cache = new Dictionary<string, Result>();

        public static Result For(string blockName, in DFBlock block)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(blockName, out var cached))
                    return cached;
            }

            var result = Compute(block);
            lock (_cache)
            {
                _cache[blockName] = result;
            }
            return result;
        }

        static Result Compute(in DFBlock block)
        {
            var result = new Result();
            var autoMap = block.RmbBlock.FldHeader.AutoMapData;
            var tiles = block.RmbBlock.FldHeader.GroundData.GroundTiles;

            for (int y = 0; y < Dim; y++)
            {
                // Sim Z is flipped within a block relative to raw block data
                // (TownLoader places subrecords at RMBDimension - ZPos, after
                // RMBLayout) — bake the grid in sim orientation so cell (x,y)
                // maps straight to world meters.
                int simY = Dim - 1 - y;

                for (int x = 0; x < Dim; x++)
                {
                    // Covered by building/model/flat → blocked.
                    if (autoMap != null && autoMap.Length == Dim * Dim && autoMap[y * Dim + x] != 0)
                        continue;       // stays 0

                    int record = tiles != null ? tiles[x / 4, y / 4].TextureRecord : Grass;
                    result.Cost[simY * Dim + x] = TileCost(record);
                }
            }

            result.Gates = ComputeGates(result.Cost);
            return result;
        }

        /// 0 = impassable; otherwise lower is preferred. Inverts DFU's
        /// weight table into step costs.
        static byte TileCost(int record)
        {
            switch (record)
            {
                case Water:
                case WaterDirtEdge1:
                case WaterDirtEdge2:
                case WaterGrassEdge1:
                case WaterGrassEdge2:
                case WaterStoneEdge1:
                case WaterStoneEdge2:
                    return 0;
                case Road:
                case RoadCornerDirt:
                case RoadCornerGrass:
                    return 1;
                case Grass:
                    return 4;
                case Dirt:
                    return 6;
                case Stone:
                    return 8;
                default:
                    return 5;
            }
        }

        /// Three flood fills (from N, E, S edges) answer all six edge pairs.
        static BlockGates ComputeGates(byte[] cost)
        {
            var fromN = Flood(cost, Edge.N);
            var fromE = Flood(cost, Edge.E);
            var fromS = Flood(cost, Edge.S);

            BlockGates gates = BlockGates.None;
            if (Touches(fromN, Edge.S)) gates |= BlockGates.NS;
            if (Touches(fromN, Edge.E)) gates |= BlockGates.NE;
            if (Touches(fromN, Edge.W)) gates |= BlockGates.NW;
            if (Touches(fromE, Edge.S)) gates |= BlockGates.SE;
            if (Touches(fromE, Edge.W)) gates |= BlockGates.EW;
            if (Touches(fromS, Edge.W)) gates |= BlockGates.SW;
            return gates;
        }

        enum Edge { N, S, E, W }

        static bool[] Flood(byte[] cost, Edge from)
        {
            var seen = new bool[Dim * Dim];
            var queue = new Queue<int>();

            for (int i = 0; i < Dim; i++)
            {
                int idx;
                switch (from)
                {
                    case Edge.N: idx = i; break;                        // y = 0 row
                    case Edge.S: idx = (Dim - 1) * Dim + i; break;      // y = Dim-1 row
                    case Edge.E: idx = i * Dim + (Dim - 1); break;      // x = Dim-1 col
                    default:     idx = i * Dim; break;                  // x = 0 col
                }
                if (cost[idx] > 0 && !seen[idx]) { seen[idx] = true; queue.Enqueue(idx); }
            }

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % Dim, y = idx / Dim;
                Visit(cost, seen, queue, x - 1, y);
                Visit(cost, seen, queue, x + 1, y);
                Visit(cost, seen, queue, x, y - 1);
                Visit(cost, seen, queue, x, y + 1);
            }
            return seen;
        }

        static void Visit(byte[] cost, bool[] seen, Queue<int> queue, int x, int y)
        {
            if (x < 0 || x >= Dim || y < 0 || y >= Dim) return;
            int idx = y * Dim + x;
            if (seen[idx] || cost[idx] == 0) return;
            seen[idx] = true;
            queue.Enqueue(idx);
        }

        static bool Touches(bool[] seen, Edge edge)
        {
            for (int i = 0; i < Dim; i++)
            {
                int idx;
                switch (edge)
                {
                    case Edge.N: idx = i; break;
                    case Edge.S: idx = (Dim - 1) * Dim + i; break;
                    case Edge.E: idx = i * Dim + (Dim - 1); break;
                    default:     idx = i * Dim; break;
                }
                if (seen[idx]) return true;
            }
            return false;
        }
    }
}
