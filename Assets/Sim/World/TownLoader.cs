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
            float blockSide = BlocksFile.RMBDimension * GlobalScale;
            int width = location.Exterior.ExteriorData.Width;
            int height = location.Exterior.ExteriorData.Height;

            var result = new TownLoadResult
            {
                Name = location.Name,
                RegionName = location.RegionName,
                BlocksWide = width,
                BlocksHigh = height,
            };

            int cells = TownGridData.CellsPerBlock;
            var grid = new TownGridData
            {
                Width = width * cells,
                Height = height * cells,
                BlocksWide = width,
                BlocksHigh = height,
                RegionIndex = location.RegionIndex,
                Cost = new byte[width * cells * height * cells],
                Gates = new BlockGates[width * height],
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    string blockName = location.Exterior.ExteriorData.BlockNames[y * width + x];
                    var block = blocksFile.GetBlock(blockName);
                    if (block.Type != DFBlock.BlockTypes.Rmb || block.RmbBlock.SubRecords == null)
                        continue;

                    // Walkability: per-template cost grid + gate flags (cached
                    // by block name — same template everywhere in the world).
                    var walk = BlockWalkability.For(blockName, block);
                    grid.Gates[y * width + x] = walk.Gates;
                    for (int row = 0; row < cells; row++)
                        Array.Copy(walk.Cost, row * cells,
                            grid.Cost, (y * cells + row) * grid.Width + x * cells, cells);

                    var buildingDataList = block.RmbBlock.FldHeader.BuildingDataList;
                    for (int i = 0; i < block.RmbBlock.SubRecords.Length; i++)
                    {
                        var sub = block.RmbBlock.SubRecords[i];

                        var row = new BuildingRow
                        {
                            Kind = BuildingKind.None,
                            X = x * blockSide + sub.XPos * GlobalScale,
                            Z = y * blockSide + (BlocksFile.RMBDimension - sub.ZPos) * GlobalScale,
                            YRotation = -sub.YRotation / BlocksFile.RotationDivisor,
                            BlockX = x,
                            BlockY = y,
                            RecordIndex = i,
                        };

                        if (buildingDataList != null && i < buildingDataList.Length)
                        {
                            var data = buildingDataList[i];
                            row.Kind = (BuildingKind)data.BuildingType;
                            row.FactionId = data.FactionId;
                            row.Quality = data.Quality;
                            row.NameSeed = data.NameSeed;
                        }

                        int buildingIndex = ctx.Buildings.Add(row);
                        result.Buildings++;

                        result.Civilians += SpawnCivilians(ctx, buildingIndex, row, blockName);
                    }
                }
            }

            ctx.TownGrid.Set(grid);
            SeedTownKnowledge(ctx);
            SeedEmployment(ctx);
            SeedGuards(ctx);
            SeedStock(ctx);
            return result;
        }

        /// Put a few townsfolk on the public payroll (guards): salaried from the
        /// Town treasury (E3), not a keeper, so they don't take private Labor — the
        /// norm-loop seed and the channel that recirculates coin into spending. The
        /// crown reimburses the treasury for their pay each tick (G6), so a small
        /// opening buffer is plenty. Patrol behaviour + the KnightlyGuard faction
        /// are later; here a guard is a salaried resident.
        static void SeedGuards(SimulationContext ctx)
        {
            var residents = new System.Collections.Generic.List<EntityId>();
            foreach (var kv in ctx.Residency.All)
                if (kv.Value.Role == ResidentRole.Resident) residents.Add(kv.Key);
            if (residents.Count == 0) return;
            residents.Sort((a, b) => a.Value.CompareTo(b.Value));   // deterministic pick

            int guards = residents.Count / 50;                      // ~1 guard per 50 residents
            if (guards < 2) guards = 2;
            if (guards > residents.Count) guards = residents.Count;

            for (int i = 0; i < guards; i++)
            {
                var id = residents[i];
                ctx.Employment.Set(id, new EmploymentData { Employer = EntityId.None, PublicOwner = OwnerId.Town });
                if (ctx.Identity.TryGet(id, out var ident) && ident != null) ident.Name = "Town Guard";
            }

            // A day's payroll as an opening buffer; the crown tops it up continuously.
            ctx.Treasury.Set(OwnerId.Town,
                guards * EconomySystem.GuardWagePerMinute * 1440.0);
        }

        /// Stock the shops and taverns so the town opens with goods to sell —
        /// the starting inventory the supply chain then replenishes (imports +
        /// craft, G2). Flat seed for now; scaling by building Quality is a tuning
        /// lever for later, not something to guess at before the loop is closed.
        static void SeedStock(SimulationContext ctx)
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
        static void SeedEmployment(SimulationContext ctx)
        {
            var keepers = new System.Collections.Generic.List<EntityId>();
            var residents = new System.Collections.Generic.List<EntityId>();
            foreach (var kv in ctx.Residency.All)
                (kv.Value.Role == ResidentRole.Keeper ? keepers : residents).Add(kv.Key);
            if (keepers.Count == 0) return;

            keepers.Sort((a, b) => a.Value.CompareTo(b.Value));        // deterministic assignment
            residents.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < residents.Count; i++)
                ctx.Employment.Set(residents[i], new EmploymentData { Employer = keepers[i % keepers.Count] });
        }

        /// A town's residents know its layout — they've lived here. So every
        /// town agent starts with the whole building list in PlaceMemory; a
        /// resident is never blind to the tavern down the street. Sensing
        /// (SenseSystem) is then about live perception — who's near right now —
        /// not learning where places are. Discovery (other towns, the player,
        /// wandering creatures) comes later, for agents without this grant.
        static void SeedTownKnowledge(SimulationContext ctx)
        {
            var buildings = new System.Collections.Generic.List<int>();
            foreach (var kv in ctx.Buildings.All)
                if (kv.Value.Kind != BuildingKind.None) buildings.Add(kv.Key);

            foreach (var res in ctx.Residency.All)
                for (int i = 0; i < buildings.Count; i++)
                    ctx.PlaceMemory.Learn(res.Key, buildings[i]);
        }

        static int SpawnCivilians(SimulationContext ctx, int buildingIndex, BuildingRow row, string blockName)
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
                        row.Kind + " keeper (" + blockName + " #" + row.RecordIndex + ")");
                    return 1;

                case BuildingKind.House1:
                case BuildingKind.House2:
                case BuildingKind.House3:
                case BuildingKind.House4:
                case BuildingKind.House5:
                case BuildingKind.House6:
                    Spawn(ctx, buildingIndex, row, ResidentRole.Resident,
                        "Resident (" + blockName + " #" + row.RecordIndex + "a)");
                    Spawn(ctx, buildingIndex, row, ResidentRole.Resident,
                        "Resident (" + blockName + " #" + row.RecordIndex + "b)");
                    return 2;

                default:
                    // Walls, palaces, ships, empty houses, special markers —
                    // no permanent population in v1.
                    return 0;
            }
        }

        static void Spawn(SimulationContext ctx, int buildingIndex, BuildingRow row, ResidentRole role, string name)
        {
            var id = ctx.Identity.Allocate();

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
