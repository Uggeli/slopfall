using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace DaggerfallWorkshop.Sim
{
    public sealed class RegionLoadResult
    {
        public string RegionName;
        public int Settlements;
        public int Buildings;
        public int Civilians;
        public int BlocksWide, BlocksHigh;   // combined grid extent, in blocks
    }

    /// Loads every settled location of a region into ONE context: one combined
    /// walkability grid, one unified world-coordinate space, every building and
    /// resident tagged to its settlement. Settlements are shelf-packed in block units
    /// with a blocked buffer between them (no inter-settlement walking until the v3
    /// caravan/wilderness layer); their real region-map positions are recorded for
    /// that later routing. The economy still runs region-global (single OwnerId.Town)
    /// — per-settlement public finance is Stage 3.
    public static class RegionLoader
    {
        const int ShelfMaxBlocks = 32;     // wrap to a new shelf once a row passes this width
        const int BufferBlocks = 1;        // blocked gap between packed settlements

        /// Location types that carry a permanent population worth simulating.
        static bool IsSettled(DFRegion.LocationTypes t) =>
            t == DFRegion.LocationTypes.TownCity ||
            t == DFRegion.LocationTypes.TownHamlet ||
            t == DFRegion.LocationTypes.TownVillage ||
            t == DFRegion.LocationTypes.HomeFarms ||
            t == DFRegion.LocationTypes.ReligionTemple ||
            t == DFRegion.LocationTypes.Tavern;

        public static RegionLoadResult LoadRegion(SimulationContext ctx, MapsFile maps, BlocksFile blocks, string regionName)
        {
            var region = maps.GetRegion(regionName);
            var result = new RegionLoadResult { RegionName = regionName };

            // Pass 1: gather the settled, loadable locations in region order.
            var locs = new List<DFLocation>();
            for (int i = 0; i < region.LocationCount; i++)
            {
                var loc = maps.GetLocation(regionName, region.MapNames[i]);
                if (!loc.Loaded) continue;
                if (!IsSettled(loc.MapTableData.LocationType)) continue;
                if (loc.Exterior.ExteriorData.Width <= 0 || loc.Exterior.ExteriorData.Height <= 0) continue;
                locs.Add(loc);
            }

            // Shelf-pack in block units → each settlement's block origin + the
            // combined extent. A blocked buffer keeps neighbours' walkable cells apart.
            var originX = new int[locs.Count];
            var originY = new int[locs.Count];
            int cursorX = 0, shelfY = 0, shelfH = 0, maxWidth = 0;
            for (int i = 0; i < locs.Count; i++)
            {
                int w = locs[i].Exterior.ExteriorData.Width;
                int h = locs[i].Exterior.ExteriorData.Height;
                if (cursorX > 0 && cursorX + w > ShelfMaxBlocks)   // wrap to next shelf
                {
                    shelfY += shelfH + BufferBlocks;
                    cursorX = 0;
                    shelfH = 0;
                }
                originX[i] = cursorX;
                originY[i] = shelfY;
                cursorX += w + BufferBlocks;
                if (h > shelfH) shelfH = h;
                if (cursorX > maxWidth) maxWidth = cursorX;
            }

            int gridW = maxWidth > 0 ? maxWidth : 1;        // trailing buffer is harmless (blocked)
            int gridH = shelfY + shelfH;
            if (gridH <= 0) gridH = 1;

            int regionIndex = locs.Count > 0 ? locs[0].RegionIndex : 0;
            var grid = TownLoader.NewGrid(gridW, gridH, regionIndex);
            result.BlocksWide = gridW;
            result.BlocksHigh = gridH;

            if (locs.Count == 0) { ctx.TownGrid.Set(grid); return result; }

            float blockSide = BlocksFile.RMBDimension * TownLoader.GlobalScale;

            // Pass 2: load each settlement into the combined grid at its block origin.
            for (int i = 0; i < locs.Count; i++)
            {
                var loc = locs[i];
                var s = ctx.Settlements.Add(loc.Name, regionName, TownLoader.KindOf(loc));
                s.BlocksWide = loc.Exterior.ExteriorData.Width;
                s.BlocksHigh = loc.Exterior.ExteriorData.Height;
                s.OriginX = originX[i] * blockSide;
                s.OriginZ = originY[i] * blockSide;
                var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
                s.MapPixelX = pix.X;
                s.MapPixelY = pix.Y;

                var sub = new TownLoadResult
                {
                    Name = loc.Name, RegionName = regionName,
                    BlocksWide = s.BlocksWide, BlocksHigh = s.BlocksHigh,
                };
                TownLoader.LoadLocationInto(ctx, loc, blocks, grid, originX[i], originY[i], s, sub);
                TownLoader.SeedSettlement(ctx, s);

                result.Buildings += sub.Buildings;
                result.Civilians += sub.Civilians;
                result.Settlements++;
            }

            TownLoader.SeedStock(ctx);     // global, per-building — same as single-town
            ctx.TownGrid.Set(grid);
            return result;
        }
    }
}
