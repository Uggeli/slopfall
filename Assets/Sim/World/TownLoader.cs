using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;

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

        /// Daggerfall name generator, injected once at bring-up (the host parses
        /// NameGen.txt). Null leaves NPCs with the descriptive placeholder names —
        /// e.g. Unity, or a headless run that didn't wire it up.
        public static DfNameGen Names;

        public static TownLoadResult Load(SimWorld world, SimRandom rng, in DFLocation location, BlocksFile blocksFile, MapsFile maps = null, WoodsFile woods = null)
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
            var settlement = world.Settlements.Add(location.Name, location.RegionName, KindOf(location));
            settlement.BlocksWide = width;
            settlement.BlocksHigh = height;
            var pix = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            settlement.MapPixelX = pix.X;
            settlement.MapPixelY = pix.Y;
            RegionIndustry.DetectInto(maps, woods, settlement);   // climate/coast/elevation (maps==null → authored fallback)

            LoadLocationInto(world, rng, location, blocksFile, grid, 0, 0, settlement, result);

            grid.Connectivity = BlockConnectivity.Build(grid);   // bake once at load (pure fn of grid) → read phase never builds it
            world.TownGrid.Set(grid);
            SeedSettlement(world, settlement);
            SeedStock(world);
            return result;
        }

        /// Run the per-settlement structural seeds (local knowledge, employment,
        /// guards). Called once per settlement by both Load and RegionLoader.
        public static void SeedSettlement(SimWorld world, SettlementData s)
        {
            // Employment/guards first — they SYNTHESIZE the primary workplaces (farm,
            // and a fishery where the coast is near). Knowledge runs last so every
            // resident knows their whole settlement INCLUDING those workplaces (a
            // farmhand still knows where the shore is). Knowledge draws no RNG, so the
            // spawn/RNG order is unchanged.
            SeedFarmAndEmployment(world, s);
            SeedGuards(world, s);
            SeedTownKnowledge(world, s);

            // Knowledge is now final for every resident — seed each one's innate agent
            // memory (place-kind atoms today; ownerships/reputations/social later, all via
            // the one AgentMemorySeeding entry point). Reads no RNG → spawn order unchanged.
            for (int r = 0; r < s.Residents.Count; r++)
                AgentMemorySeeding.SeedAgent(world, s.Residents[r]);
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
                GateBlock = new bool[blocksWide * blocksHigh],
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
        public static void LoadLocationInto(SimWorld world, SimRandom rng, in DFLocation location, BlocksFile blocksFile,
            TownGridData grid, int blockOriginX, int blockOriginY, SettlementData settlement, TownLoadResult result)
        {
            float blockSide = BlocksFile.RMBDimension * GlobalScale;
            int width = location.Exterior.ExteriorData.Width;
            int height = location.Exterior.ExteriorData.Height;
            int cells = TownGridData.CellsPerBlock;

            // Dominant race of this place (climate People) — drives every resident's
            // name bank + IdentityData.Race. Carried on the settlement so immigrants
            // and guards spawned later read the same culture.
            settlement.Race = (int)location.Climate.People;

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

                    // Perimeter wall blocks (model 444/445 walls + 446/447 gates):
                    // their only walkable cells are the gate openings. Flag the block
                    // so the curfew overlay can seal it and gate posts can be derived.
                    if (blockName.StartsWith("WALL"))
                        grid.GateBlock[gy * grid.BlocksWide + gx] = true;

                    var buildingDataList = block.RmbBlock.FldHeader.BuildingDataList;
                    for (int i = 0; i < block.RmbBlock.SubRecords.Length; i++)
                    {
                        var sub = block.RmbBlock.SubRecords[i];

                        // Gate posts: a guard-holdable opening is a gate 3D model
                        // (446 open / 447 closed) carried by a WALL* block's subrecord.
                        // World pos = subrecord origin (same math as BuildingRow.X/Z below)
                        // + the gate object's offset within the subrecord.
                        if (blockName.StartsWith("WALL") && sub.Exterior.Block3dObjectRecords != null)
                        {
                            foreach (var obj in sub.Exterior.Block3dObjectRecords)
                            {
                                if (obj.ModelIdNum != 446 && obj.ModelIdNum != 447) continue;
                                float gateX = gx * blockSide + (sub.XPos + obj.XPos) * GlobalScale;
                                float gateZ = gy * blockSide
                                              + (BlocksFile.RMBDimension - sub.ZPos) * GlobalScale
                                              + obj.ZPos * GlobalScale;
                                settlement.GatePosts.Add(new GatePost { X = gateX, Z = gateZ });
                            }
                        }

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

                        int buildingIndex = world.Buildings.Add(building);
                        settlement.Buildings.Add(buildingIndex);
                        result.Buildings++;

                        result.Civilians += SpawnCivilians(world, rng, buildingIndex, building, blockName, settlement);
                    }

                    // World flora: record nature ground scenery as FloraInstance (render is a
                    // separate consumer; harvesting is future). Town-local frame, same as BuildingRow.
                    int natureArchive = location.Climate.NatureArchive;
                    int worldClimate = location.Climate.WorldClimate;
                    var ground = block.RmbBlock.FldHeader.GroundData.GroundScenery;
                    if (ground != null)
                    {
                        const float TileDim = 256f, NatureOffsetY = -2f;
                        for (int sx = 0; sx < 16; sx++)
                        for (int sy = 0; sy < 16; sy++)
                        {
                            int rec = ground[sx, 15 - sy].TextureRecord;
                            if (rec < 1) continue;
                            int sid = SpeciesCatalog.SpeciesIdOf(natureArchive, rec);
                            var e = SpeciesCatalog.Lookup(natureArchive, rec);
                            world.Flora.Add(new FloraInstance
                            {
                                Archive = natureArchive, Record = rec, Climate = worldClimate,
                                X = gx * blockSide + sx * TileDim * GlobalScale,
                                Y = NatureOffsetY * GlobalScale,
                                Z = gy * blockSide + (sy * TileDim + TileDim) * GlobalScale,
                                SpeciesId = sid, Category = e.Category, Resource = e.Resource,
                            });
                        }
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
        static void SeedGuards(SimWorld world, SettlementData s)
        {
            var residents = ResidentsOfRole(world, s, ResidentRole.Resident);
            if (residents.Count == 0) return;

            int guards = GuardCountFor(s.Kind, residents.Count);    // scaled by settlement kind
            if (guards <= 0) return;
            if (guards > residents.Count) guards = residents.Count;

            for (int i = 0; i < guards; i++)
            {
                var id = residents[i];
                var emp = new EmploymentData { Employer = EntityId.None, PublicOwner = s.Treasury };
                emp.NightShift = (i % 2) == 1;          // even index = day watch, odd = night watch
                if (s.GatePosts.Count > 0)
                {
                    emp.GateIndex = i % s.GatePosts.Count;   // round-robin across posts
                    emp.GateX = s.GatePosts[emp.GateIndex].X;
                    emp.GateZ = s.GatePosts[emp.GateIndex].Z;
                }
                world.Employment.Seed(id, emp);
                // Keep the generated name, tag the public role: "Tindyl Sorensen (Guard)".
                if (world.Identity.TryGet(id, out var ident) && ident != null && ident.Name != null
                    && !ident.Name.Contains("(Guard)"))
                    ident.Name += " (Guard)";
            }

            // A day's payroll as an opening buffer; the crown tops it up continuously.
            // Add (not Set) so it accumulates when settlements share the global purse
            // (Stage 2); single-town starts from an empty treasury, so it's the same.
            world.Treasury.Seed(s.Treasury,
                guards * Engine.EconomySystem.GuardDailyWage);
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
            SimWorld world, SettlementData s, ResidentRole role)
        {
            var list = new System.Collections.Generic.List<EntityId>();
            for (int i = 0; i < s.Residents.Count; i++)
            {
                var id = s.Residents[i];
                if (world.Residency.TryGet(id, out var r) && r.Role == role) list.Add(id);
            }
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            return list;
        }

        /// Stock the shops and taverns so the town opens with goods to sell —
        /// the starting inventory the supply chain then replenishes (imports +
        /// craft, G2). Flat seed for now; scaling by building Quality is a tuning
        /// lever for later, not something to guess at before the loop is closed.
        public static void SeedStock(SimWorld world)
        {
            foreach (var kv in world.Buildings.All)
            {
                var goods = GoodsCatalog.Stocks(kv.Value.Kind);
                for (int i = 0; i < goods.Length; i++)
                    world.Stock.Seed(kv.Key, goods[i], InitialStock);
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
        static void SeedFarmAndEmployment(SimWorld world, SettlementData s)
        {
            var laborers = ResidentsOfRole(world, s, ResidentRole.Resident);
            if (laborers.Count == 0) return;

            // The settlement's primary workplaces — farmland always, plus a fishery in
            // coastal regions (so an island's idle hands fish the sea, not just the few
            // fields). Each sits at a distinct edge of the settlement (the fields one
            // side, the shore the other), away from the homes — so hands walk OUT to
            // work, not to the town centre. Each is run by a keeper promoted from the
            // laborers; the rest are its hands.
            var kinds = RegionIndustry.Workplaces(s);
            var anchors = PeripheralAnchors(world, s, kinds.Count);
            if (anchors.Count == 0) return;

            var workplaces = new System.Collections.Generic.List<int>();
            var keepers = new System.Collections.Generic.List<EntityId>();

            int li = 0;
            for (int k = 0; k < kinds.Count && k < anchors.Count && li < laborers.Count; k++)
            {
                var a = anchors[k];
                int wp = world.Buildings.Add(new BuildingRow
                {
                    Kind = kinds[k],
                    X = a.X, Z = a.Z, BlockX = a.BlockX, BlockY = a.BlockY, RecordIndex = -1,
                });
                s.Buildings.Add(wp);
                var goods = GoodsCatalog.Stocks(kinds[k]);
                for (int i = 0; i < goods.Length; i++) world.Stock.Seed(wp, goods[i], InitialStock);

                var keeper = laborers[li++];
                world.Residency.Seed(keeper, new ResidencyData { BuildingIndex = wp, Role = ResidentRole.Keeper });
                world.Employment.Seed(keeper, new EmploymentData());   // a keeper has no employer
                world.PlaceMemory.Learn(keeper, wp);
                workplaces.Add(wp);
                keepers.Add(keeper);
            }
            if (workplaces.Count == 0) return;

            // The remaining laborers split across the workplaces (round-robin), employed
            // by that workplace's keeper and knowing where it is.
            for (; li < laborers.Count; li++)
            {
                int w = li % workplaces.Count;
                world.Employment.Seed(laborers[li], new EmploymentData { Employer = keepers[w] });
                world.PlaceMemory.Learn(laborers[li], workplaces[w]);
            }
        }

        /// Distinct edge positions for a settlement's `count` primary workplaces: the
        /// westmost real building for the first (the fields), the eastmost for the
        /// second (the shore), so the two sit on opposite sides of town and their hands
        /// walk out in different directions. Falls back to a single building if the
        /// settlement is tiny. Building positions are reachable (agents path to them),
        /// so the fields stay walkable while being away from the homes.
        static System.Collections.Generic.List<BuildingRow> PeripheralAnchors(
            SimWorld world, SettlementData s, int count)
        {
            // The settlement's real buildings (skip walls/None), sorted by X (Z as a
            // deterministic tiebreak), so the `count` workplaces spread across its width —
            // fields one side, shore/diggings the other, the workshops between — and sit
            // away from the centre. Positions are reachable (agents path to them).
            var reals = new System.Collections.Generic.List<BuildingRow>();
            for (int i = 0; i < s.Buildings.Count; i++)
                if (world.Buildings.TryGet(s.Buildings[i], out var b) && b != null && b.Kind != BuildingKind.None)
                    reals.Add(b);
            var list = new System.Collections.Generic.List<BuildingRow>();
            if (reals.Count == 0) return list;
            reals.Sort((a, b) => { int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Z.CompareTo(b.Z); });
            for (int k = 0; k < count; k++)
            {
                int idx = count <= 1 ? 0 : (int)((long)k * (reals.Count - 1) / (count - 1));
                list.Add(reals[idx]);
            }
            return list;
        }

        /// A town's residents know its layout — they've lived here. So every
        /// town agent starts with the whole building list in PlaceMemory; a
        /// resident is never blind to the tavern down the street. Sensing
        /// (SenseSystem) is then about live perception — who's near right now —
        /// not learning where places are. Discovery (other towns, the player,
        /// wandering creatures) comes later, for agents without this grant.
        static void SeedTownKnowledge(SimWorld world, SettlementData s)
        {
            // Each resident knows the buildings of their OWN settlement only — no
            // omniscient awareness of other settlements across the region.
            for (int r = 0; r < s.Residents.Count; r++)
                for (int b = 0; b < s.Buildings.Count; b++)
                {
                    int bi = s.Buildings[b];
                    if (world.Buildings.TryGet(bi, out var br) && br.Kind == BuildingKind.None) continue;
                    world.PlaceMemory.Learn(s.Residents[r], bi);
                }
        }

        static int SpawnCivilians(SimWorld world, SimRandom rng, int buildingIndex, BuildingRow row, string blockName, SettlementData s)
        {
            // One household per building: shared FamilyId + a surname seed both
            // residents draw the same surname from (the family-tree hook).
            int familyId = buildingIndex;
            uint familySeed = FamilySeed(buildingIndex);

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
                    Spawn(world, rng, buildingIndex, row, ResidentRole.Keeper,
                        row.Kind + " keeper",
                        row.Kind + " keeper (" + blockName + " #" + row.RecordIndex + ")",
                        familyId, familySeed, s);
                    return 1;

                case BuildingKind.House1:
                case BuildingKind.House2:
                case BuildingKind.House3:
                case BuildingKind.House4:
                case BuildingKind.House5:
                case BuildingKind.House6:
                    // A settled couple sharing the household surname — the first edges
                    // a future genealogy/faction system grows a family tree from.
                    var a = Spawn(world, rng, buildingIndex, row, ResidentRole.Resident,
                        null, "Resident (" + blockName + " #" + row.RecordIndex + "a)",
                        familyId, familySeed, s);
                    var b = Spawn(world, rng, buildingIndex, row, ResidentRole.Resident,
                        null, "Resident (" + blockName + " #" + row.RecordIndex + "b)",
                        familyId, familySeed, s);
                    LinkSpouses(world, a, b);
                    return 2;

                default:
                    // Walls, palaces, ships, empty houses, special markers —
                    // no permanent population in v1.
                    return 0;
            }
        }

        // Surname RNG seed for a household — id-hash salted so it's independent of the
        // per-NPC first-name seed and never touches the spawn RNG stream.
        static uint FamilySeed(int buildingIndex) => LifeHash(buildingIndex ^ 0x71717171);

        // Record a spouse edge both ways (load-time, single-threaded — mutate in place).
        static void LinkSpouses(SimWorld world, EntityId a, EntityId b)
        {
            if (a == EntityId.None || b == EntityId.None) return;
            if (world.Lineage.TryGet(a, out var la) && la != null) la.Spouse = b;
            if (world.Lineage.TryGet(b, out var lb) && lb != null) lb.Spouse = a;
        }

        static EntityId Spawn(SimWorld world, SimRandom rng, int buildingIndex, BuildingRow row, ResidentRole role,
            string roleLabel, string fallbackName, int familyId, uint familySeed, SettlementData settlement)
        {
            var id = world.Identity.Allocate();
            settlement.Residents.Add(id);

            // Gender is drawn from the spawn rng at exactly its old position, so the
            // stream stays byte-identical. The NAME is derived from id/family hashes
            // (not rng), so adding it doesn't perturb any downstream seeded value.
            int gender = rng.NextBool() ? 0 : 1;
            int race = DfNameGen.RaceFromFactionRace(settlement.Race);

            string surname = string.Empty;
            string name = fallbackName;
            var gen = Names;
            if (gen != null)
            {
                int bank = DfNameGen.BankFromFactionRace(settlement.Race);
                string first = gen.FirstName(bank, gender, LifeHash(id.Value ^ 0x2D2D2D2D));
                surname = gen.Surname(bank, familySeed);
                name = string.IsNullOrEmpty(surname) ? first : first + " " + surname;
                if (!string.IsNullOrEmpty(roleLabel)) name += " (" + roleLabel + ")";
            }

            world.Identity.Seed(id, new IdentityData
            {
                Name = name,
                Kind = EntityKind.CivilianNPC,
                Race = race,
                Gender = gender,
                CareerIndex = -1,
                Level = 1,
                FactionId = row.FactionId,
                Team = 0,
            });

            // Perceivable surface (identity atoms): kind, race, role — stamped once at spawn.
            world.Perceivable.Seed(id, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);
            if (race >= 0) world.Perceivable.Seed(id, PerceivableAtoms.Race(race), Fixed.One);
            world.Perceivable.Seed(id, PerceivableAtoms.Role(role), Fixed.One);
            // Phase C: an honest body Size — a perceiver reads threat RELATIVE to its own size.
            foreach (var form in HumanForms.Civilian)
                world.Perceivable.Seed(id, form.Type, form.Value);

            // Empty private memory — the agent learns categories from perception over time.
            world.AgentMemory.Seed(id);

            world.Lineage.Seed(id, new LineageData { FamilyId = familyId, Surname = surname });

            int maxHealth = 40 + rng.NextInt(21);
            world.Vitals.Seed(id, new VitalsData
            {
                CurrentHealth = maxHealth, MaxHealth = maxHealth,
                CurrentMagicka = 10, MaxMagicka = 10,
                CurrentFatigue = 100, MaxFatigue = 100,
                CurrentBreath = 10, MaxBreath = 10,
            });

            var stats = new StatsData();
            for (int s = 0; s < StatIndex.Count; s++)
                stats.Stats[s] = 30 + rng.NextInt(31);
            world.Stats.Seed(id, stats);

            world.Position.Seed(id, row.X, 0f, row.Z, row.YRotation);

            world.Residency.Seed(id, new ResidencyData
            {
                BuildingIndex = buildingIndex,
                Role = role,
            });

            // Seed the need poles mid-range so the first sim day starts varied
            // rather than everyone rushing the same urgent deficit at once.
            var needs = new NeedsData();
            needs.V[NeedAxis.Hunger] = 0.2 + rng.NextDouble() * 0.3;
            needs.V[NeedAxis.EnergyDef] = 0.1 + rng.NextDouble() * 0.3;
            needs.V[NeedAxis.SocialDef] = 0.3 + rng.NextDouble() * 0.4;
            needs.V[NeedAxis.GoodsDef] = 0.2 + rng.NextDouble() * 0.3;
            needs.V[NeedAxis.Attire] = 0.2 + rng.NextDouble() * 0.3;   // clothes already partly worn (spread) → the looms have real demand to serve from day one

            // Real money, recapitalised (money arc): everyone starts with a buffer (~20),
            // keepers a bit more for working capital to stock + pay wages before sales
            // come in. Coin is INSTRUMENTAL now (CoinDef retired) — this is liquidity,
            // not a poverty anchor; dynamic local prices decide whether 20 is rich or
            // poor (a busy city is dearer than a hamlet).
            double coin = role == ResidentRole.Keeper
                ? 25.0 + rng.NextDouble() * 10.0
                : 18.0 + rng.NextDouble() * 4.0;
            world.Coin.Seed(id, coin);
            needs.V[NeedAxis.CoinDef] = 0.0;              // retired axis; coin ≥ 1 ⇒ not "poor" (NeedsSystem re-derives, clamped)
            world.Needs.Seed(id, needs);

            // Personality: sum of two draws biases toward the middle, so
            // extremes exist but are rare.
            var traits = new double[TraitIndex.Count];
            for (int t = 0; t < TraitIndex.Count; t++)
                traits[t] = (rng.NextDouble() + rng.NextDouble()) * 0.5;
            world.Personality.Seed(id, PersonalityData.Derive(traits));

            // L2 (docs/living_world_L2_lifecycle.md): age + lifespan, derived from a
            // hash of the id — NOT rng — so adding it leaves the spawn RNG
            // stream (and every existing seeded value) byte-identical. Adults at
            // load / immigration; the distribution is a FROZEN placeholder.
            world.Life.Seed(id, SeedLife(id));
            world.Conscience.Seed(id, SeedConscience(id));
            return id;
        }

        /// L2.4 — backfill a vacated residency slot with a broke adult immigrant
        /// (docs/living_world_L2_lifecycle.md): re-staffs a dead keeper's shop and
        /// keeps headcount roughly stationary. Arrives with NO coin (mints no
        /// money → conservation holds) and knows its new settlement on arrival,
        /// matching the load-time "residents know the whole town" policy.
        public static EntityId SpawnImmigrant(SimWorld world, SimRandom rng, SettlementData settlement, int buildingIndex, ResidentRole role)
        {
            if (settlement == null) return EntityId.None;
            if (!world.Buildings.TryGet(buildingIndex, out var row) || row == null) return EntityId.None;

            string roleLabel = role == ResidentRole.Keeper ? row.Kind + " keeper" : null;
            var id = Spawn(world, rng, buildingIndex, row, role, roleLabel,
                "Newcomer (" + settlement.Name + ")", buildingIndex, FamilySeed(buildingIndex), settlement);
            world.Coin.Seed(id, 0);                        // broke newcomer — mints no money
            foreach (var b in settlement.Buildings)
                world.PlaceMemory.Learn(id, b);
            AgentMemorySeeding.SeedAgent(world, id);   // innate memory, same as load-time residents
            return id;
        }

        static LifeData SeedLife(EntityId id)
        {
            uint h = LifeHash(id.Value);
            double a = (h & 0xFFFF) / 65535.0;
            double b = ((h >> 16) & 0xFFFF) / 65535.0;
            return new LifeData
            {
                AgeYears = 18 + a * 42,        // 18..60 — adults at load/immigration
                LifespanYears = 58 + b * 24,   // 58..82
            };
        }

        /// S4 conscience: seeded charges over the agent's own shamed/taboo acts,
        /// scaled by dispositions (re-salted hashes of the id, so they don't perturb
        /// the spawn RNG stream). FROZEN placeholder distributions.
        ///   - begging-shame (pride): a proud soul would rather starve than beg; a
        ///     shameless one feels no qualm.
        ///   - theft-taboo (honesty): theft is broadly tabooed — everyone carries a
        ///     moderate-to-strong qualm [0.5,1.0], so only the least honest will steal,
        ///     and only when desperate enough for the drive to beat the penalty.
        static ConscienceData SeedConscience(EntityId id)
        {
            double pride = (LifeHash(id.Value ^ 0x5A5A5A5A) & 0xFFFF) / 65535.0;
            double honesty = (LifeHash(id.Value ^ 0x3C3C3C3C) & 0xFFFF) / 65535.0;
            var c = new ConscienceData();
            c.Charge[(int)ActivityKind.Beg] = pride * 0.8;
            c.Charge[(int)ActivityKind.Steal] = 0.5 + 0.5 * honesty;
            return c;
        }

        static uint LifeHash(int v)
        {
            uint x = (uint)(v * 2654435761u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }
    }
}
