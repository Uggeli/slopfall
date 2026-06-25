namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The single, flat identity vocabulary for every perceivable / memory atom. The enum member
    /// IS the identity — auto-numbered, so the compiler guarantees uniqueness (no hand-allocated
    /// bands to collide). The underlying int is the AtomTypeId.Value (see AtomNames.ToId): nothing
    /// persists it across runs, so the numbering is free and chosen dense/clean. Identity atoms
    /// (Civilian, PlaceTavern) and — later — property atoms share this one enum.
    ///
    /// There is, by construction, NO verdict member (no Predator/Threat/Danger): a thing's atoms
    /// describe; the perceiver concludes. The affective TONE a Phase-B reading consumes lives in
    /// AtomCatalog, never here.
    /// </summary>
    public enum AtomName
    {
        None = 0,

        // ── Entity kinds (EntityKind) ──
        Player, EnemyClass, EnemyMonster, Civilian, StaticNpc,

        // ── Resident roles (ResidentRole) ──
        Resident, Keeper,

        // ── Races (Daggerfall Races roster) ──
        RaceBreton, RaceRedguard, RaceNord, RaceDarkElf, RaceHighElf, RaceWoodElf,
        RaceKhajiit, RaceArgonian, RaceVampire, RaceWerewolf, RaceWereboar,

        // ── Activities (ActivityKind, minus None) ──
        ActIdle, ActWander, ActSleep, ActWork, ActLabor, ActFarm, ActFish, ActMine,
        ActEatHome, ActEatTavern, ActSocialize, ActVisit, ActBuy, ActChat, ActSeekHelp,
        ActBeg, ActSteal, ActFlee, ActAttack, ActUseItem, ActStoreItem, ActWeave,
        ActPatrol, ActStandWatch, ActGossip, ActNegotiate,

        // ── Somatic (an agent's perception of its own body) ──
        // No From(...) overload — stamped directly via SomaticAtoms fields.
        SomaticHunger, SomaticEnergy, SomaticFear,

        // ── Place: building kinds (BuildingKind, minus None) ──
        PlaceAlchemist, PlaceHouseForSale, PlaceArmorer, PlaceBank, PlaceTown4,
        PlaceBookseller, PlaceClothingStore, PlaceFurnitureStore, PlaceGemStore,
        PlaceGeneralStore, PlaceLibrary, PlaceGuildHall, PlacePawnShop, PlaceWeaponSmith,
        PlaceTemple, PlaceTavern, PlacePalace, PlaceHouse1, PlaceHouse2, PlaceHouse3,
        PlaceHouse4, PlaceHouse5, PlaceHouse6, PlaceTown23, PlaceShip,
        PlaceSpecial1, PlaceSpecial2, PlaceSpecial3, PlaceSpecial4,
        PlaceFarm, PlaceFishery, PlaceMine, PlacePasture, PlaceWeaver,

        // ── Place: graded facts ──
        // No From(...) overload — stamped directly via PlaceAtoms fields.
        PlaceProvisions, PlaceDanger,
    }
}
