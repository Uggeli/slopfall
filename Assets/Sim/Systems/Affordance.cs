namespace DaggerfallWorkshop.Sim
{
    /// One opportunity offered to an agent: a verb to perform at a place. The
    /// world (buildings, persons) and the self advertise these; the future
    /// DeliberationSystem gathers them, culls by drive, scores, and argmaxes.
    /// This is what replaces OddSystem's hardcoded candidate blocks — a candidate
    /// becomes a row of data, not a branch.
    public struct Ad
    {
        public ActivityKind Verb;
        public int Building;        // BuildingRegistry key, -1 = at-self / on the street
        public float X, Z;
        public ActivityCatalog.Spec Spec;   // duration + served-Δ + base utility
        public ItemId Item;         // the item a Take/Use/Drop block acts on; None for place/activity ads
    }

    /// What advertises what. The table that dissolves the special cases: a
    /// building kind offers public verbs; an agent's home/workplace offer verbs
    /// to their occupant/employee; a person offers verbs to those nearby; and the
    /// self always offers the Object-Zero floor. Nothing consumes this yet —
    /// DeliberationSystem (E0b-4) gathers ads from it.
    public static class AffordanceCatalog
    {
        static readonly ActivityKind[] None = new ActivityKind[0];
        // Gossip rides beside Socialize at the tavern (P3): the same gathering place,
        // generated the same way, so an agent who picks the social hall may instead
        // choose the lighter chatting — and its Liveliness/RelationSensitive gates reward
        // others being present (gossip wants an audience).
        static readonly ActivityKind[] TavernVerbs = { ActivityKind.EatTavern, ActivityKind.Socialize, ActivityKind.Gossip };
        // Beg is offered at shops and temples — the alms-giving venues, where a
        // keeper with coin is present (L4). Taverns stay social halls. A poor
        // agent picks Beg only when poverty is loud; the comfortable score it ~0
        // (its value comes from the coinDef gap).
        // Steal is offered wherever Buy is, but self-gates to provisions-holders
        // (general stores): SaleStockAvailable culls it where there's no food to
        // take (a craftsman's wares aren't edible). The desperate take it; the
        // conscience charge keeps the honest from it.
        static readonly ActivityKind[] ShopVerbs = { ActivityKind.Buy, ActivityKind.Beg, ActivityKind.Steal };
        static readonly ActivityKind[] LandmarkVerbs = { ActivityKind.Visit, ActivityKind.Beg };

        /// Public affordances — any agent who senses or remembers the building
        /// can use them. Landmark set matches OddSystem.EnsureLandmarks so the
        /// rewrite preserves which places draw visitors. Private/role
        /// affordances (Home, Workplace) are agent-relative and resolved by the
        /// ad-gatherer, not here.
        public static ActivityKind[] Public(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Tavern:
                    return TavernVerbs;
                case BuildingKind.Alchemist:
                case BuildingKind.Armorer:
                case BuildingKind.Bookseller:
                case BuildingKind.ClothingStore:
                case BuildingKind.FurnitureStore:
                case BuildingKind.GemStore:
                case BuildingKind.GeneralStore:
                case BuildingKind.PawnShop:
                case BuildingKind.WeaponSmith:
                    return ShopVerbs;
                case BuildingKind.Temple:
                case BuildingKind.GuildHall:
                case BuildingKind.Bank:
                case BuildingKind.Library:
                case BuildingKind.Palace:
                    return LandmarkVerbs;
                default:
                    return None;
            }
        }

        /// Home → its residents (sleep, eat). Labor moved to the employer's
        /// business (jobs-at-business; resolved in ActionDiscovery).
        public static readonly ActivityKind[] Home = { ActivityKind.Sleep, ActivityKind.EatHome };
        /// Workplace → its employee (keepers today; resident jobs in E1, no new code).
        public static readonly ActivityKind[] Workplace = { ActivityKind.Work };
        /// A person → those near enough to approach (greet, or ask for help —
        /// the latter is driven by RequestSystem's embodied flow, not gathered
        /// as a marketplace ad).
        public static readonly ActivityKind[] Person = { ActivityKind.Chat, ActivityKind.SeekHelp };
        /// Innate, always available: the Object-Zero liveness floor + the fear
        /// responses (Flee/Attack are always on the table; they only win when fear
        /// is loud, and personality picks which — the timid flee, the bold fight).
        public static readonly ActivityKind[] Innate = { ActivityKind.Idle, ActivityKind.Wander, ActivityKind.Flee, ActivityKind.Attack };
    }
}
