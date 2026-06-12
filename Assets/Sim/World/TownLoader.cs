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

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    string blockName = location.Exterior.ExteriorData.BlockNames[y * width + x];
                    var block = blocksFile.GetBlock(blockName);
                    if (block.Type != DFBlock.BlockTypes.Rmb || block.RmbBlock.SubRecords == null)
                        continue;

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

            return result;
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
        }
    }
}
