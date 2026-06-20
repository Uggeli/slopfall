using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop.Sim.Engine;

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

        /// Every loadable location of a region, in region order.
        static IEnumerable<DFLocation> EnumerateLocations(MapsFile maps, DFRegion region, string regionName)
        {
            for (int i = 0; i < region.LocationCount; i++)
            {
                var loc = maps.GetLocation(regionName, region.MapNames[i]);
                if (loc.Loaded) yield return loc;
            }
        }

        /// Attach a freshly-built settlement to its POI (matched by name+region).
        static void LinkSettlement(SimWorld world, SettlementData s)
        {
            foreach (var poi in world.Pois.All)
                if (poi.Settlement == null && poi.Name == s.Name && poi.RegionName == s.RegionName)
                { poi.Settlement = s; return; }
        }

        public static RegionLoadResult LoadRegion(SimWorld world, SimRandom rng, MapsFile maps, BlocksFile blocks, string regionName, WoodsFile woods = null,
            System.Func<string, int, int, float> tileFloor = null, float maxTerrainHeight = 0f)
        {
            var region = maps.GetRegion(regionName);
            var result = new RegionLoadResult { RegionName = regionName };

            // Pass 1: build a POI for every loadable location; collect the settled,
            // non-empty ones (in region order) for the settlement seed below.
            var locs = new List<DFLocation>();
            foreach (var poiLoc in EnumerateLocations(maps, region, regionName))
            {
                var role = PoiClassifier.PoiRoleOf(poiLoc.MapTableData.LocationType);
                if (role == PoiRole.PlayerShip) continue;   // out of scope

                bool hasExterior = poiLoc.Exterior.ExteriorData.Width > 0 &&
                                   poiLoc.Exterior.ExteriorData.Height > 0;
                var pixPoi = MapsFile.LongitudeLatitudeToMapPixel(
                    poiLoc.MapTableData.Longitude, poiLoc.MapTableData.Latitude);

                world.Pois.Add(new RegionPoi
                {
                    Name = poiLoc.Name,
                    RegionName = regionName,
                    RawLocationType = (int)poiLoc.MapTableData.LocationType,
                    Role = role,
                    MapPixelX = pixPoi.X,
                    MapPixelY = pixPoi.Y,
                    BlocksWide = poiLoc.Exterior.ExteriorData.Width,
                    BlocksHigh = poiLoc.Exterior.ExteriorData.Height,
                    HasExterior = hasExterior,
                });

                if (PoiRoles.IsSettled(role) && hasExterior)
                    locs.Add(poiLoc);
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

            if (locs.Count == 0) { grid.Connectivity = BlockConnectivity.Build(grid); world.TownGrid.Set(grid); return result; }

            float blockSide = BlocksFile.RMBDimension * TownLoader.GlobalScale;

            // Pass 2: load each settlement into the combined grid at its block origin.
            for (int i = 0; i < locs.Count; i++)
            {
                var loc = locs[i];
                var s = world.Settlements.Add(loc.Name, regionName, TownLoader.KindOf(loc));
                LinkSettlement(world, s);
                s.BlocksWide = loc.Exterior.ExteriorData.Width;
                s.BlocksHigh = loc.Exterior.ExteriorData.Height;
                s.OriginX = originX[i] * blockSide;
                s.OriginZ = originY[i] * blockSide;
                var pix = MapsFile.LongitudeLatitudeToMapPixel(loc.MapTableData.Longitude, loc.MapTableData.Latitude);
                s.MapPixelX = pix.X;
                s.MapPixelY = pix.Y;
                RegionIndustry.DetectInto(maps, woods, s);   // read climate/coast/elevation before the employment seed

                var sub = new TownLoadResult
                {
                    Name = loc.Name, RegionName = regionName,
                    BlocksWide = s.BlocksWide, BlocksHigh = s.BlocksHigh,
                };
                TownLoader.LoadLocationInto(world, rng, loc, blocks, grid, originX[i], originY[i], s, sub);
                TownLoader.SeedSettlement(world, s);

                result.Buildings += sub.Buildings;
                result.Civilians += sub.Civilians;
                result.Settlements++;
            }

            TownLoader.SeedStock(world);     // global, per-building — same as single-town
            grid.Connectivity = BlockConnectivity.Build(grid);   // bake once at load → read phase never builds it
            world.TownGrid.Set(grid);

            SeedGeography(world, regionName, tileFloor, maxTerrainHeight);
            return result;
        }

        // Pass 3: geo overworld placement as sim truth. Places every exterior POI at its
        // TRUE map-pixel position (the packed grid above is for pathfinding only), records
        // each origin + the packed→geo agent remap, and seeds world.Geography. Skipped when
        // no tileFloor is injected (non-render callers: soak/probe). The XZ math is pure
        // map-pixel geometry; only the Y pad needs the terrain floor (injected, since the
        // sim can't reference AssetExport's TerrainTile).
        static void SeedGeography(SimWorld world, string regionName,
            System.Func<string, int, int, float> tileFloor, float maxTerrainHeight)
        {
            if (tileFloor == null) return;

            const float TileSize = 32768f * TownLoader.GlobalScale;   // 819.2 m, one map pixel
            const float BlockSide = 4096f * TownLoader.GlobalScale;   // 102.4 m (BlocksFile.RMBDimension)
            const int TerrainPad = 3;   // pixels of wilderness/sea kept around the locations

            // Each town's terrain tile flattens a footprint CENTRED in its pixel, so the
            // buildings (and agents) must be centred to match: (128 - blocks*16)/2 tiles.
            (float x, float z) TownCentre(int bw, int bh)
            {
                int tx = (128 - bw * 16) / 2, ty = (128 - bh * 16) / 2;
                return (tx / 16f * BlockSide, ty / 16f * BlockSide);
            }

            var pAll = new List<RegionPoi>();
            foreach (var p in world.Pois.All) if (p.HasExterior) pAll.Add(p);
            if (pAll.Count == 0) return;

            int mx0 = int.MaxValue, my0 = int.MaxValue, mx1 = int.MinValue, my1 = int.MinValue;
            foreach (var p in pAll)
            {
                if (p.MapPixelX < mx0) mx0 = p.MapPixelX;
                if (p.MapPixelX > mx1) mx1 = p.MapPixelX;
                if (p.MapPixelY < my0) my0 = p.MapPixelY;
                if (p.MapPixelY > my1) my1 = p.MapPixelY;
            }
            mx0 = System.Math.Max(0, mx0 - TerrainPad); my0 = System.Math.Max(0, my0 - TerrainPad);
            mx1 = System.Math.Min(999, mx1 + TerrainPad); my1 = System.Math.Min(499, my1 + TerrainPad);

            // Datum: the POI nearest the bbox centre supplies the floor the whole region
            // levels to, so every streamed tile meets its neighbours at a continuous seam.
            int cmx = (mx0 + mx1) / 2, cmy = (my0 + my1) / 2;
            RegionPoi centre = pAll[0];
            int bestD = int.MaxValue;
            foreach (var p in pAll)
            {
                int d = (p.MapPixelX - cmx) * (p.MapPixelX - cmx) + (p.MapPixelY - cmy) * (p.MapPixelY - cmy);
                if (d < bestD) { bestD = d; centre = p; }
            }
            float datum = tileFloor(centre.Name, centre.BlocksWide, centre.BlocksHigh);

            var poiPixels = new List<(int, int, int)>();
            var remap = new List<GeoRemapEntry>();
            for (int i = 0; i < pAll.Count; i++)
            {
                var p = pAll[i];
                var (cx, cz) = TownCentre(p.BlocksWide, p.BlocksHigh);
                // +X east (MapPixelX grows east); +Z north (MapPixelY grows south, so Z
                // counts down from the bbox south edge my1) — DFU's native terrain frame.
                float geoX = (p.MapPixelX - mx0) * TileSize + cx;
                float geoZ = (my1 - p.MapPixelY) * TileSize + cz;
                float floor = tileFloor(p.Name, p.BlocksWide, p.BlocksHigh);
                float padY = (floor - datum) * maxTerrainHeight;
                p.OriginX = geoX; p.OriginY = padY; p.OriginZ = geoZ;
                poiPixels.Add((p.MapPixelX, p.MapPixelY, i));

                // Only settled POIs have agents to remap from the packed grid to geo space.
                var s = p.Settlement;
                if (s != null)
                    remap.Add(new GeoRemapEntry
                    {
                        MinX = s.OriginX, MinZ = s.OriginZ,
                        MaxX = s.OriginX + s.BlocksWide * BlockSide,
                        MaxZ = s.OriginZ + s.BlocksHigh * BlockSide,
                        Dx = geoX - s.OriginX, Dy = padY, Dz = geoZ - s.OriginZ,
                    });
            }
            world.Geography.Seed(mx0, my0, mx1, my1, TileSize, datum, poiPixels, remap);
        }
    }
}
