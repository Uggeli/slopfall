using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace DaggerfallWorkshop.Sim
{
    public sealed class TownLoadResult
    {
        public string Name;
        public string RegionName;
        public int Buildings;
        public int Civilians;
        public int BlocksWide, BlocksHigh;
    }

    /// Populates the sim registries from a location's RMB block data:
    /// one BuildingRow per block subrecord (city walls and the like included,
    /// as BuildingKind.None/Town23), and persistent civilians spawned per
    /// building by a fixed policy (shops/taverns/temples/guilds get a keeper,
    /// houses get two residents).
    ///
    /// Classic Daggerfall has no persistent townsfolk — its street crowds are
    /// ephemeral props. These civilians are new sim citizens: deterministic for
    /// a given SimRandom seed, and the population the ODD layer will animate.
    ///
    /// Coordinates follow DFU's RMBLayout convention: a block is 4096 classic
    /// units (102.4 m at GlobalScale 0.025); a subrecord sits at
    /// (XPos, RMBDimension - ZPos) * scale within its block; block (x, y) has
    /// its origin at (x, y) * blockSide in town-local meters.
    public static class TownLoader
    {
        public const float GlobalScale = 0.025f;    // matches MeshReader.GlobalScale
        const double InitialStock = 20.0;            // units a shop/tavern holds at load; G2 replenishes

        public static TownLoadResult Load(SimulationContext ctx, in DFLocation location, BlocksFile blocksFile)
        {
            int width = location.Exterior.ExteriorData.Width;
            int height = location.Exterior.ExteriorData.Height;

            var result = new TownLoadResult
            {
                Name = location.Name,
                RegionName = location.RegionName,
                BlocksWide = width,
                BlocksHigh = height,
            };

            // One settlement filling its own grid at origin (0,0) — RegionLoader is
            // the multi-settlement path; this stays the single-town case and must
            // stay behavior-identical (the LOD ground-truth + the test/soak baseline).
            var grid = NewGrid(width, height, location.RegionIndex);
            var settlement = ctx.Settlements.Add(location.Name, location.RegionName, KindOf(location));
            settlement.BlocksWide = width;
            settlement.BlocksHigh = height;

            LoadLocationInto(ctx, location, blocksFile, grid, 0, 0, settlement, result);

            ctx.TownGrid.Set(grid);
            SeedSettlement(ctx, settlement);
            SeedStock(ctx);
            return result;
        }

        /// Run the per-settlement structural seeds (local knowledge, employment,
        /// guards). Called once per settlement by both Load and RegionLoader.
        public static void SeedSettlement(SimulationContext ctx, SettlementData s)
        {
            SeedTownKnowledge(ctx, s);
            SeedFarmAndEmployment(ctx, s);
            SeedGuards(ctx, s);
        }

        /// Allocate a combined walkability grid of blocksWide × blocksHigh blocks.
        /// RegionLoader sizes this to the whole packed region; single-town Load sizes
        /// it to the one location.
        public static TownGridData NewGrid(int blocksWide, int blocksHigh, int regionIndex)
        {
            int cells = TownGridData.CellsPerBlock;
            return new TownGridData
            {
                Width = blocksWide * cells,
                Height = blocksHigh * cells,
                BlocksWide = blocksWide,
                BlocksHigh = blocksHigh,
                RegionIndex = regionIndex,
                Cost = new byte[blocksWide * cells * blocksHigh * cells],
                Gates = new BlockGates[blocksWide * blocksHigh],
            };
        }

        /// Daggerfall location type → the sim's economic archetype.
        public static SettlementKind KindOf(in DFLocation location)
        {
            switch (location.MapTableData.LocationType)
            {
                case DFRegion.LocationTypes.TownCity:       return SettlementKind.City;
                case DFRegion.LocationTypes.TownHamlet:     return SettlementKind.Hamlet;
                case DFRegion.LocationTypes.TownVillage:    return SettlementKind.Village;
                case DFRegion.LocationTypes.HomeFarms:      return SettlementKind.Farm;
                case DFRegion.LocationTypes.ReligionTemple: return SettlementKind.Temple;
                case DFRegion.LocationTypes.Tavern:         return SettlementKind.Tavern;
                default:                                    return SettlementKind.Other;
            }
        }

        /// Walk one location's RMB blocks into `grid` at block offset
        /// (blockOriginX, blockOriginY) (combined block coords), creating buildings
        /// with world coords offset to match and spawning civilians, all tagged to
        /// `settlement`. The y,x,subrecord walk order is fixed, so EntityId/RNG draws
        /// are deterministic and the single-town path (offset 0,0, grid sized to the
        /// one location) is bit-for-bit unchanged.
        public static void LoadLocationInto(SimulationContext ctx, in DFLocation location, BlocksFile blocksFile,
            TownGridData grid, int blockOriginX, int blockOriginY, SettlementData settlement, TownLoadResult result)
        {
            float blockSide = BlocksFile.RMBDimension * GlobalScale;
            int width = location.Exterior.ExteriorData.Width;
            int height = location.Exterior.ExteriorData.Height;
            int cells = TownGridData.CellsPerBlock;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    string blockName = location.Exterior.ExteriorData.BlockNames[y * width + x];
                    var block = blocksFile.GetBlock(blockName);
                    if (block.Type != DFBlock.BlockTypes.Rmb || block.RmbBlock.SubRecords == null)
                        continue;

                    int gx = blockOriginX + x;       // combined block coordinates
                    int gy = blockOriginY + y;

                    // Walkability: per-template cost grid + gate flags (cached
                    // by block name — same template everywhere in the world).
                    var walk = BlockWalkability.For(blockName, block);
                    grid.Gates[gy * grid.BlocksWide + gx] = walk.Gates;
                    for (int row = 0; row < cells; row++)
                        Array.Copy(walk.Cost, row * cells,
                            grid.Cost, (gy * cells + row) * grid.Width + gx * cells, cells);

                    var buildingDataList = block.RmbBlock.FldHeader.BuildingDataList;
                    for (int i = 0; i < block.RmbBlock.SubRecords.Length; i++)
                    {
                        var sub = block.RmbBlock.SubRecords[i];

                        var building = new BuildingRow
                        {
                            Kind = BuildingKind.None,
                            X = gx * blockSide + sub.XPos * GlobalScale,
                            Z = gy * blockSide + (BlocksFile.RMBDimension - sub.ZPos) * GlobalScale,
                            YRotation = -sub.YRotation / BlocksFile.RotationDivisor,
                            BlockX = gx,
                            BlockY = gy,
                            RecordIndex = i,
                        };

                        if (buildingDataList != null && i < buildingDataList.Length)
                        {
                            var data = buildingDataList[i];
                            building.Kind = (BuildingKind)data.BuildingType;
                            building.FactionId = data.FactionId;
                            building.Quality = data.Quality;
                            building.NameSeed = data.NameSeed;
                        }

                        int buildingIndex = ctx.Buildings.Add(building);
                        settlement.Buildings.Add(buildingIndex);
                        result.Buildings++;

                        result.Civilians += SpawnCivilians(ctx, buildingIndex, building, blockName, settlement);
                    }
                }
            }
        }

        /// Put a few townsfolk on the public payroll (guards): salaried from the
        /// Town treasury (E3), not a keeper, so they don't take private Labor — the
        /// norm-loop seed and the channel that recirculates coin into spending. The
        /// crown reimburses the treasury for their pay each tick (G6), so a small
        /// opening buffer is plenty. Patrol behaviour + the KnightlyGuard faction
        /// are later; here a guard is a salaried resident.
        static void SeedGuards(SimulationContext ctx, SettlementData s)
        {
            var residents = ResidentsOfRole(ctx, s, ResidentRole.Resident);
            if (residents.Count == 0) return;

            int guards = GuardCountFor(s.Kind, residents.Count);    // scaled by settlement kind
            if (guards <= 0) return;
            if (guards > residents.Count) guards = residents.Count;

            for (int i = 0; i < guards; i++)
            {
                var id = residents[i];
                ctx.Employment.Set(id, new EmploymentData { Employer = EntityId.None, PublicOwner = s.Treasury });
                if (ctx.Identity.TryGet(id, out var ident) && ident != null) ident.Name = "Town Guard";
            }

            // A day's payroll as an opening buffer; the crown tops it up continuously.
            // Add (not Set) so it accumulates when settlements share the global purse
            // (Stage 2); single-town starts from an empty treasury, so it's the same.
            ctx.Treasury.Add(s.Treasury,
                guards * EconomySystem.GuardWagePerMinute * 1440.0);
        }

        /// How many town guards a settlement of this kind keeps — cities are policed,
        /// hamlets keep a token watch, and villages/farms/standalone temples & taverns
        /// have none (they're too poor and small for a salaried guard). This is the
        /// "hamlets are poor and don't really have a town guard" rule, and it also
        /// stops tiny settlements from turning their whole tiny population into guards.
        static int GuardCountFor(SettlementKind kind, int residents)
        {
            switch (kind)
            {
                case SettlementKind.City:   { int g = residents / 50; return g < 2 ? 2 : g; }
                case SettlementKind.Hamlet: { int g = residents / 100; return g < 1 ? 1 : g; }
                default:                    return 0;
            }
        }

        /// This settlement's residents holding `role`, sorted by EntityId for a
        /// deterministic, settlement-local pick (no cross-settlement bleed).
        static System.Collections.Generic.List<EntityId> ResidentsOfRole(
            SimulationContext ctx, SettlementData s, ResidentRole role)
        {
            var list = new System.Collections.Generic.List<EntityId>();
            for (int i = 0; i < s.Residents.Count; i++)
            {
                var id = s.Residents[i];
                if (ctx.Residency.TryGet(id, out var r) && r.Role == role) list.Add(id);
            }
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            return list;
        }

        /// Stock the shops and taverns so the town opens with goods to sell —
        /// the starting inventory the supply chain then replenishes (imports +
        /// craft, G2). Flat seed for now; scaling by building Quality is a tuning
        /// lever for later, not something to guess at before the loop is closed.
        public static void SeedStock(SimulationContext ctx)
        {
            foreach (var kv in ctx.Buildings.All)
            {
                var goods = GoodsCatalog.Stocks(kv.Value.Kind);
                for (int i = 0; i < goods.Length; i++)
                    ctx.Stock.Set(kv.Key, goods[i], InitialStock);
            }
        }

        /// Assign each resident an employer (a keeper) round-robin, so their
        /// Labor wage is paid by a real business — coin recirculates instead of
        /// being minted. Proper job-matching (skills, nearby business) is later;
        /// this is the employment relationship the wage transfer needs.
        /// Give the settlement's laborers a real workplace (Stage 5). Existing shop /
        /// service keepers run their own premises; everyone else is a laborer, and in
        /// v1 the settlement's farmland is the mass workplace — they farm (food, the
        /// primary sector), employed by a farm keeper promoted from among them. This
        /// is what turns "271 idle laborers" into producers earning from real output.
        static void SeedFarmAndEmployment(SimulationContext ctx, SettlementData s)
        {
            var laborers = ResidentsOfRole(ctx, s, ResidentRole.Resident);
            if (laborers.Count == 0) return;

            // The settlement's primary workplaces — farmland always, plus a fishery in
            // coastal regions (so an island's idle hands fish the sea, not just the few
            // fields). Each sits at a distinct edge of the settlement (the fields one
            // side, the shore the other), away from the homes — so hands walk OUT to
            // work, not to the town centre. Each is run by a keeper promoted from the
            // laborers; the rest are its hands.
            var kinds = RegionIndustry.Workplaces(s.RegionName);
            var anchors = PeripheralAnchors(ctx, s, kinds.Count);
            if (anchors.Count == 0) return;

            var workplaces = new System.Collections.Generic.List<int>();
            var keepers = new System.Collections.Generic.List<EntityId>();

            int li = 0;
            for (int k = 0; k < kinds.Count && k < anchors.Count && li < laborers.Count; k++)
            {
                var a = anchors[k];
                int wp = ctx.Buildings.Add(new BuildingRow
                {
                    Kind = kinds[k],
                    X = a.X, Z = a.Z, BlockX = a.BlockX, BlockY = a.BlockY, RecordIndex = -1,
                });
                s.Buildings.Add(wp);
                var goods = GoodsCatalog.Stocks(kinds[k]);
                for (int i = 0; i < goods.Length; i++) ctx.Stock.Set(wp, goods[i], InitialStock);

                var keeper = laborers[li++];
                ctx.Residency.Set(keeper, new ResidencyData { BuildingIndex = wp, Role = ResidentRole.Keeper });
                ctx.Employment.Set(keeper, new EmploymentData());   // a keeper has no employer
                ctx.PlaceMemory.Learn(keeper, wp);
                workplaces.Add(wp);
                keepers.Add(keeper);
            }
            if (workplaces.Count == 0) return;

            // The remaining laborers split across the workplaces (round-robin), employed
            // by that workplace's keeper and knowing where it is.
            for (; li < laborers.Count; li++)
            {
                int w = li % workplaces.Count;
                ctx.Employment.Set(laborers[li], new EmploymentData { Employer = keepers[w] });
                ctx.PlaceMemory.Learn(laborers[li], workplaces[w]);
            }
        }

        /// Distinct edge positions for a settlement's `count` primary workplaces: the
        /// westmost real building for the first (the fields), the eastmost for the
        /// second (the shore), so the two sit on opposite sides of town and their hands
        /// walk out in different directions. Falls back to a single building if the
        /// settlement is tiny. Building positions are reachable (agents path to them),
        /// so the fields stay walkable while being away from the homes.
        static System.Collections.Generic.List<BuildingRow> PeripheralAnchors(
            SimulationContext ctx, SettlementData s, int count)
        {
            BuildingRow west = null, east = null, any = null;
            for (int i = 0; i < s.Buildings.Count; i++)
            {
                if (!ctx.Buildings.TryGet(s.Buildings[i], out var b) || b == null) continue;
                if (any == null) any = b;
                if (b.Kind == BuildingKind.None) continue;
                if (west == null || b.X < west.X) west = b;
                if (east == null || b.X > east.X) east = b;
            }
            var list = new System.Collections.Generic.List<BuildingRow>();
            var first = west ?? any;
            if (first == null) return list;
            list.Add(first);
            if (count >= 2) list.Add(east != null && east != west ? east : first);
            return list;
        }

        /// A town's residents know its layout — they've lived here. So every
        /// town agent starts with the whole building list in PlaceMemory; a
        /// resident is never blind to the tavern down the street. Sensing
        /// (SenseSystem) is then about live perception — who's near right now —
        /// not learning where places are. Discovery (other towns, the player,
        /// wandering creatures) comes later, for agents without this grant.
        static void SeedTownKnowledge(SimulationContext ctx, SettlementData s)
        {
            // Each resident knows the buildings of their OWN settlement only — no
            // omniscient awareness of other settlements across the region.
            for (int r = 0; r < s.Residents.Count; r++)
                for (int b = 0; b < s.Buildings.Count; b++)
                {
                    int bi = s.Buildings[b];
                    if (ctx.Buildings.TryGet(bi, out var br) && br.Kind == BuildingKind.None) continue;
                    ctx.PlaceMemory.Learn(s.Residents[r], bi);
                }
        }

        static int SpawnCivilians(SimulationContext ctx, int buildingIndex, BuildingRow row, string blockName, SettlementData s)
        {
            switch (row.Kind)
            {
                case BuildingKind.Alchemist:
                case BuildingKind.Armorer:
                case BuildingKind.Bank:
                case BuildingKind.Bookseller:
                case BuildingKind.ClothingStore:
                case BuildingKind.FurnitureStore:
                case BuildingKind.GemStore:
                case BuildingKind.GeneralStore:
                case BuildingKind.Library:
                case BuildingKind.GuildHall:
                case BuildingKind.PawnShop:
                case BuildingKind.WeaponSmith:
                case BuildingKind.Temple:
                case BuildingKind.Tavern:
                    Spawn(ctx, buildingIndex, row, ResidentRole.Keeper,
                        row.Kind + " keeper (" + blockName + " #" + row.RecordIndex + ")", s);
                    return 1;

                case BuildingKind.House1:
                case BuildingKind.House2:
                case BuildingKind.House3:
                case BuildingKind.House4:
                case BuildingKind.House5:
                case BuildingKind.House6:
                    Spawn(ctx, buildingIndex, row, ResidentRole.Resident,
                        "Resident (" + blockName + " #" + row.RecordIndex + "a)", s);
                    Spawn(ctx, buildingIndex, row, ResidentRole.Resident,
                        "Resident (" + blockName + " #" + row.RecordIndex + "b)", s);
                    return 2;

                default:
                    // Walls, palaces, ships, empty houses, special markers —
                    // no permanent population in v1.
                    return 0;
            }
        }

        static void Spawn(SimulationContext ctx, int buildingIndex, BuildingRow row, ResidentRole role, string name, SettlementData settlement)
        {
            var id = ctx.Identity.Allocate();
            settlement.Residents.Add(id);

            ctx.Identity.Set(id, new IdentityData
            {
                Name = name,
                Kind = EntityKind.CivilianNPC,
                Race = -1,
                Gender = ctx.Random.NextBool() ? 0 : 1,
                CareerIndex = -1,
                Level = 1,
                FactionId = row.FactionId,
                Team = 0,
            });

            int maxHealth = 40 + ctx.Random.NextInt(21);
            ctx.Vitals.Set(id, new VitalsData
            {
                CurrentHealth = maxHealth, MaxHealth = maxHealth,
                CurrentMagicka = 10, MaxMagicka = 10,
                CurrentFatigue = 100, MaxFatigue = 100,
                CurrentBreath = 10, MaxBreath = 10,
            });

            var stats = new StatsData();
            for (int s = 0; s < StatIndex.Count; s++)
                stats.Stats[s] = 30 + ctx.Random.NextInt(31);
            ctx.Stats.Set(id, stats);

            ctx.Position.Set(id, row.X, 0f, row.Z, row.YRotation);

            ctx.Residency.Set(id, new ResidencyData
            {
                BuildingIndex = buildingIndex,
                Role = role,
            });

            // Seed the need poles mid-range so the first sim day starts varied
            // rather than everyone rushing the same urgent deficit at once.
            var needs = new NeedsData();
            needs.V[NeedAxis.Hunger] = 0.2 + ctx.Random.NextDouble() * 0.3;
            needs.V[NeedAxis.EnergyDef] = 0.1 + ctx.Random.NextDouble() * 0.3;
            needs.V[NeedAxis.SocialDef] = 0.3 + ctx.Random.NextDouble() * 0.4;
            needs.V[NeedAxis.GoodsDef] = 0.2 + ctx.Random.NextDouble() * 0.3;

            // Real money: keepers start comfortable, residents start poor —
            // poverty is structural until they find income, which is exactly
            // the problem source the request system feeds on. CoinDef derives.
            double coin = role == ResidentRole.Keeper
                ? 0.5 + ctx.Random.NextDouble() * 0.3
                : 0.15 + ctx.Random.NextDouble() * 0.25;
            ctx.Coin.Set(id, coin);
            needs.V[NeedAxis.CoinDef] = 1.0 - coin;
            ctx.Needs.Set(id, needs);

            // Personality: sum of two draws biases toward the middle, so
            // extremes exist but are rare.
            var traits = new double[TraitIndex.Count];
            for (int t = 0; t < TraitIndex.Count; t++)
                traits[t] = (ctx.Random.NextDouble() + ctx.Random.NextDouble()) * 0.5;
            ctx.Personality.Set(id, PersonalityData.Derive(traits));
        }
    }
}
