using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class BlockWalkabilityTests
    {
        const int D = BlockWalkability.Dim;

        /// Synthetic RMB block: open grass everywhere, optional covered band.
        static DFBlock MakeBlock(Func<int, int, bool> covered)
        {
            var block = new DFBlock();
            block.RmbBlock.FldHeader.AutoMapData = new byte[D * D];
            var tiles = new DFBlock.RmbGroundTiles[16, 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    tiles[x, y].TextureRecord = 3;      // grass
            block.RmbBlock.FldHeader.GroundData.GroundTiles = tiles;

            for (int y = 0; y < D; y++)
                for (int x = 0; x < D; x++)
                    if (covered(x, y))
                        block.RmbBlock.FldHeader.AutoMapData[y * D + x] = 1;
            return block;
        }

        [Fact]
        public void OpenBlock_HasAllGates()
        {
            var block = MakeBlock((x, y) => false);
            var walk = BlockWalkability.For("TEST_OPEN", block);
            Assert.Equal(BlockGates.NS | BlockGates.NE | BlockGates.NW
                | BlockGates.SE | BlockGates.SW | BlockGates.EW, walk.Gates);
        }

        [Fact]
        public void FullWallBand_CutsNorthSouth_KeepsOthers()
        {
            // Horizontal covered band across the full width: N and S halves
            // disconnect, but each half still reaches both E and W edges.
            var block = MakeBlock((x, y) => y >= 28 && y <= 35);
            var walk = BlockWalkability.For("TEST_BAND", block);

            Assert.False(walk.Gates.HasFlag(BlockGates.NS));
            Assert.True(walk.Gates.HasFlag(BlockGates.NE));
            Assert.True(walk.Gates.HasFlag(BlockGates.NW));
            Assert.True(walk.Gates.HasFlag(BlockGates.SE));
            Assert.True(walk.Gates.HasFlag(BlockGates.SW));
            Assert.True(walk.Gates.HasFlag(BlockGates.EW));
        }

        [Fact]
        public void SealedBlock_HasNoGates()
        {
            var block = MakeBlock((x, y) => true);
            var walk = BlockWalkability.For("TEST_SEALED", block);
            Assert.Equal(BlockGates.None, walk.Gates);
            Assert.All(walk.Cost, c => Assert.Equal(0, c));
        }
    }

    public class TownPathfindingTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        static SimHarness LoadTown(string location = "Gothway Garden")
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            TownLoader.Load(h.Ctx, maps.GetLocation("Daggerfall", location), blocks);
            return h;
        }

        [Fact]
        public void TownGrid_HasRoads_AndOpenGround()
        {
            if (!Available) return;
            var h = LoadTown();
            var g = h.Ctx.TownGrid.Current;

            Assert.NotNull(g);
            Assert.Equal(4 * 64, g.Width);
            Assert.Equal(3 * 64, g.Height);

            int roads = 0, walkable = 0;
            foreach (var c in g.Cost)
            {
                if (c == 1) roads++;
                if (c > 0) walkable++;
            }
            Assert.True(roads > 300, "only " + roads + " road cells");
            Assert.True(walkable > g.Cost.Length / 4, "only " + walkable + " walkable cells");
        }

        [Fact]
        public void GridOrientation_BuildingsSitOnBlockedCells()
        {
            // The walk grid and building positions come from different data
            // (AutoMapData vs subrecord XPos/ZPos) through different transforms;
            // if orientations disagree, buildings land on open ground. Houses
            // must overwhelmingly sit on covered (blocked) cells.
            if (!Available) return;
            var h = LoadTown();
            var g = h.Ctx.TownGrid.Current;

            int houses = 0, onBlocked = 0;
            foreach (var kv in h.Ctx.Buildings.All)
            {
                if (kv.Value.Kind < BuildingKind.House1 || kv.Value.Kind > BuildingKind.House6) continue;
                houses++;
                if (!g.Walkable(g.CellX(kv.Value.X), g.CellY(kv.Value.Z))) onBlocked++;
            }
            Assert.True(houses > 50);
            Assert.True(onBlocked >= houses * 0.7,
                onBlocked + "/" + houses + " houses on blocked cells — grid orientation is wrong");
        }

        [Fact]
        public void Path_AcrossTown_FollowsWalkableCells()
        {
            if (!Available) return;
            var h = LoadTown();
            var g = h.Ctx.TownGrid.Current;

            // Two buildings far apart.
            BuildingRow a = null, b = null;
            foreach (var kv in h.Ctx.Buildings.All)
            {
                if (kv.Value.Kind == BuildingKind.None || kv.Value.Kind == BuildingKind.Town23) continue;
                if (a == null) { a = kv.Value; continue; }
                float dx = kv.Value.X - a.X, dz = kv.Value.Z - a.Z;
                if (b == null || dx * dx + dz * dz > (b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z))
                    b = kv.Value;
            }

            var path = new List<PathPoint>();
            Assert.True(TownPathfinder.FindPath(g, a.X, a.Z, b.X, b.Z, path),
                "no path between buildings " + a.Kind + " and " + b.Kind);
            Assert.True(path.Count >= 2);

            // Every waypoint except the final door-approach is on walkable ground.
            for (int i = 0; i < path.Count - 1; i++)
                Assert.True(g.Walkable(g.CellX(path[i].X), g.CellY(path[i].Z)),
                    "waypoint " + i + " at " + path[i].X + "," + path[i].Z + " is blocked");

            // A real street path is at least as long as the crow flies.
            double walked = 0;
            for (int i = 1; i < path.Count; i++)
            {
                double dx = path[i].X - path[i - 1].X, dz = path[i].Z - path[i - 1].Z;
                walked += Math.Sqrt(dx * dx + dz * dz);
            }
            double straight = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            Assert.True(walked >= straight * 0.99, "path shorter than straight line?");
        }

        [Fact]
        public void Capital_PathsAtScale()
        {
            if (!Available) return;
            var h = LoadTown("Daggerfall");
            var g = h.Ctx.TownGrid.Current;
            Assert.Equal(8 * 64, g.Width);

            // Corner to corner through the whole city.
            var path = new List<PathPoint>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool found = TownPathfinder.FindPath(g, 60, 60, g.Width * 1.6f - 60, g.Height * 1.6f - 60, path);
            sw.Stop();
            Assert.True(found, "no corner-to-corner path in the capital");
            Assert.True(sw.ElapsedMilliseconds < 250, "pathfinding took " + sw.ElapsedMilliseconds + "ms");
        }
    }
}
