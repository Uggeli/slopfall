namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Maps each owning source enum to its AtomName — the atom doc's "two indexes of one relation."
    /// The source enums stay (behaviour/employment/spawning use them); this is the perceivable
    /// identity index. Sentinel source values (Unknown / None / unknown race id) are non-perceivable
    /// and map to AtomName.None. ToId() exposes the free AtomName→AtomTypeId conversion.
    /// </summary>
    public static class AtomNames
    {
        /// <summary>The closed Daggerfall race roster (Races enum, 1..11). -1/None is the sentinel.</summary>
        public static readonly int[] RaceRoster = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        public static AtomTypeId ToId(this AtomName name) => new AtomTypeId((int)name);

        public static AtomName From(EntityKind kind) => kind switch
        {
            EntityKind.Player       => AtomName.Player,
            EntityKind.EnemyClass   => AtomName.EnemyClass,
            EntityKind.EnemyMonster => AtomName.EnemyMonster,
            EntityKind.CivilianNPC  => AtomName.Civilian,
            EntityKind.StaticNPC    => AtomName.StaticNpc,
            _                       => AtomName.None,   // Unknown — never stamped
        };

        public static AtomName From(ResidentRole role) => role switch
        {
            ResidentRole.Resident => AtomName.Resident,
            ResidentRole.Keeper   => AtomName.Keeper,
            _                     => AtomName.None,
        };

        public static AtomName FromRace(int raceId) => raceId switch
        {
            1  => AtomName.RaceBreton,
            2  => AtomName.RaceRedguard,
            3  => AtomName.RaceNord,
            4  => AtomName.RaceDarkElf,
            5  => AtomName.RaceHighElf,
            6  => AtomName.RaceWoodElf,
            7  => AtomName.RaceKhajiit,
            8  => AtomName.RaceArgonian,
            9  => AtomName.RaceVampire,
            10 => AtomName.RaceWerewolf,
            11 => AtomName.RaceWereboar,
            _  => AtomName.None,   // -1/None or any unknown id
        };

        public static AtomName From(ActivityKind kind) => kind switch
        {
            ActivityKind.Idle       => AtomName.ActIdle,
            ActivityKind.Wander     => AtomName.ActWander,
            ActivityKind.Sleep      => AtomName.ActSleep,
            ActivityKind.Work       => AtomName.ActWork,
            ActivityKind.Labor      => AtomName.ActLabor,
            ActivityKind.Farm       => AtomName.ActFarm,
            ActivityKind.Fish       => AtomName.ActFish,
            ActivityKind.Mine       => AtomName.ActMine,
            ActivityKind.EatHome    => AtomName.ActEatHome,
            ActivityKind.EatTavern  => AtomName.ActEatTavern,
            ActivityKind.Socialize  => AtomName.ActSocialize,
            ActivityKind.Visit      => AtomName.ActVisit,
            ActivityKind.Buy        => AtomName.ActBuy,
            ActivityKind.Chat       => AtomName.ActChat,
            ActivityKind.SeekHelp   => AtomName.ActSeekHelp,
            ActivityKind.Beg        => AtomName.ActBeg,
            ActivityKind.Steal      => AtomName.ActSteal,
            ActivityKind.Flee       => AtomName.ActFlee,
            ActivityKind.Attack     => AtomName.ActAttack,
            ActivityKind.UseItem    => AtomName.ActUseItem,
            ActivityKind.StoreItem  => AtomName.ActStoreItem,
            ActivityKind.Weave      => AtomName.ActWeave,
            ActivityKind.Patrol     => AtomName.ActPatrol,
            ActivityKind.StandWatch => AtomName.ActStandWatch,
            ActivityKind.Gossip     => AtomName.ActGossip,
            ActivityKind.Negotiate  => AtomName.ActNegotiate,
            _                       => AtomName.None,   // None — no activity atom
        };

        public static AtomName From(BuildingKind kind) => kind switch
        {
            BuildingKind.Alchemist      => AtomName.PlaceAlchemist,
            BuildingKind.HouseForSale   => AtomName.PlaceHouseForSale,
            BuildingKind.Armorer        => AtomName.PlaceArmorer,
            BuildingKind.Bank           => AtomName.PlaceBank,
            BuildingKind.Town4          => AtomName.PlaceTown4,
            BuildingKind.Bookseller     => AtomName.PlaceBookseller,
            BuildingKind.ClothingStore  => AtomName.PlaceClothingStore,
            BuildingKind.FurnitureStore => AtomName.PlaceFurnitureStore,
            BuildingKind.GemStore       => AtomName.PlaceGemStore,
            BuildingKind.GeneralStore   => AtomName.PlaceGeneralStore,
            BuildingKind.Library        => AtomName.PlaceLibrary,
            BuildingKind.GuildHall      => AtomName.PlaceGuildHall,
            BuildingKind.PawnShop       => AtomName.PlacePawnShop,
            BuildingKind.WeaponSmith    => AtomName.PlaceWeaponSmith,
            BuildingKind.Temple         => AtomName.PlaceTemple,
            BuildingKind.Tavern         => AtomName.PlaceTavern,
            BuildingKind.Palace         => AtomName.PlacePalace,
            BuildingKind.House1         => AtomName.PlaceHouse1,
            BuildingKind.House2         => AtomName.PlaceHouse2,
            BuildingKind.House3         => AtomName.PlaceHouse3,
            BuildingKind.House4         => AtomName.PlaceHouse4,
            BuildingKind.House5         => AtomName.PlaceHouse5,
            BuildingKind.House6         => AtomName.PlaceHouse6,
            BuildingKind.Town23         => AtomName.PlaceTown23,
            BuildingKind.Ship           => AtomName.PlaceShip,
            BuildingKind.Special1       => AtomName.PlaceSpecial1,
            BuildingKind.Special2       => AtomName.PlaceSpecial2,
            BuildingKind.Special3       => AtomName.PlaceSpecial3,
            BuildingKind.Special4       => AtomName.PlaceSpecial4,
            BuildingKind.Farm           => AtomName.PlaceFarm,
            BuildingKind.Fishery        => AtomName.PlaceFishery,
            BuildingKind.Mine           => AtomName.PlaceMine,
            BuildingKind.Pasture        => AtomName.PlacePasture,
            BuildingKind.Weaver         => AtomName.PlaceWeaver,
            _                           => AtomName.None,   // None sentinel
        };
    }
}
