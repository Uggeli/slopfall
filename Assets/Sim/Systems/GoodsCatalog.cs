namespace DaggerfallWorkshop.Sim
{
    /// The kinds of real goods that move through the town economy. A small
    /// authored set to start (docs/goods_economy.md §2) — recipes, quality tiers
    /// and a wider catalog come later. Values are 0..Count so per-building stock
    /// can live in a flat array indexed by (int)Good.
    public enum Good
    {
        Provisions = 0,  // food/raw — imported by general stores, served as meals by taverns
        Drink,           // ale — imported/brewed; sold by taverns
        Wares,           // clothing, arms, sundries, gems — made by craftsmen, sold by their shops
        Ore,             // raw metal/stone — mined in the hills; export-only (no local buyer yet)
    }

    /// Where a good sits along the supply chain — price rises with each step:
    /// a shop pays Import off-map, sells Wholesale to another business (B2B),
    /// and Retail to a walk-in consumer (B2C). See docs/goods_economy.md §4.
    public enum PriceTier
    {
        Import = 0,    // off-map cost to bring a good into town
        Wholesale,     // business-to-business price
        Retail,        // consumer price
    }

    /// Authored price + chain markup for one good. Money is in the normalized
    /// coin world (1.0 = a comfortable purse). Markups are >1, so the chain is
    /// monotonic by construction (import &lt; wholesale &lt; retail) and tuning the
    /// Base scales the whole chain at once. PLACEHOLDER numbers — prices get tuned
    /// against the soak once the whole loop is in (G6), not before.
    public struct GoodDef
    {
        public Good Good;
        public string Name;
        public double Base;            // = Import price (the anchor)
        public double WholesaleMarkup; // Wholesale = Base * this
        public double RetailMarkup;    // Retail    = Base * this  (> WholesaleMarkup)
        public double WorldDemandPerDay; // units/day the off-map market wants at full price; beyond it the export price falls
    }

    /// The goods table: item kinds, authored prices, and which buildings stock
    /// which goods. The one place value is declared — every trade in the economy
    /// reads its price from here. Pure data; no per-instance state.
    public static class GoodsCatalog
    {
        public const int Count = 4;

        // Indexed by (int)Good. The single source of authored prices.
        public static readonly GoodDef[] Defs =
        {
            new GoodDef { Good = Good.Provisions, Name = "provisions", Base = 0.02,  WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 500 },
            new GoodDef { Good = Good.Drink,      Name = "drink",      Base = 0.015, WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 300 },
            new GoodDef { Good = Good.Wares,      Name = "wares",      Base = 0.05,  WholesaleMarkup = 2.0, RetailMarkup = 3.0, WorldDemandPerDay = 200 },
            new GoodDef { Good = Good.Ore,        Name = "ore",        Base = 0.04,  WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 200 },
        };

        public static GoodDef Def(Good good) => Defs[(int)good];

        /// A primary-sector workplace (farm, fishery): a sim-native production site
        /// whose output scales with the hands working it. Drives the worker-scaling and
        /// wage-share in EconomySystem so a second industry slots in as data.
        public static bool IsPrimaryWorkplace(BuildingKind kind)
            => kind == BuildingKind.Farm || kind == BuildingKind.Fishery || kind == BuildingKind.Mine;

        /// Price of a good at a point in the supply chain.
        public static double PriceOf(Good good, PriceTier tier)
        {
            var d = Defs[(int)good];
            switch (tier)
            {
                case PriceTier.Wholesale: return d.Base * d.WholesaleMarkup;
                case PriceTier.Retail:    return d.Base * d.RetailMarkup;
                default:                  return d.Base;   // Import
            }
        }

        /// What goods a building of this kind keeps in stock (and therefore can
        /// sell). General stores carry the imported staples; craft shops carry
        /// their wares; taverns hold raw provisions + drink to serve. Institutions
        /// (temple/guild/bank) sell services, not goods — they hold none (G5).
        public static Good[] Stocks(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Farm:
                case BuildingKind.Fishery:
                    return ProvisionsOnly; // the harvest/catch it produces and sells on
                case BuildingKind.Mine:
                    return OreOnly;        // the ore it digs and exports
                case BuildingKind.GeneralStore:
                    return Staples;        // provisions (sourced local) + drink (imported)
                case BuildingKind.Tavern:
                    return Staples;        // raw provisions + drink, served as meals/ale
                case BuildingKind.Alchemist:
                case BuildingKind.Armorer:
                case BuildingKind.Bookseller:
                case BuildingKind.ClothingStore:
                case BuildingKind.FurnitureStore:
                case BuildingKind.GemStore:
                case BuildingKind.PawnShop:
                case BuildingKind.WeaponSmith:
                    return WaresOnly;      // craft shops sell wares they produce
                default:
                    return System.Array.Empty<Good>();
            }
        }

        /// Goods a building brings in from off-map, paying the import price out
        /// of town — the economy's one controlled leak. With local farms producing
        /// food (Stage 5), general stores no longer import provisions — they source
        /// those locally (B2B) from the farm and import only drink (the one staple
        /// not yet produced in town). Taverns get theirs via B2B from stores (G4).
        public static Good[] Imports(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.GeneralStore: return DrinkOnly;
                default:                        return System.Array.Empty<Good>();
            }
        }

        /// Goods a building makes locally by working — craft, value created from
        /// abstracted materials (no coin in). Craft shops produce their wares.
        public static Good[] Produces(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Alchemist:
                case BuildingKind.Armorer:
                case BuildingKind.Bookseller:
                case BuildingKind.ClothingStore:
                case BuildingKind.FurnitureStore:
                case BuildingKind.GemStore:
                case BuildingKind.PawnShop:
                case BuildingKind.WeaponSmith:
                    return WaresOnly;
                case BuildingKind.Farm:
                case BuildingKind.Fishery:
                    return ProvisionsOnly;   // local food: the primary-sector faucet
                case BuildingKind.Mine:
                    return OreOnly;          // raw ore: the mountain-town export faucet
                default:
                    return System.Array.Empty<Good>();
            }
        }

        /// Does a building of this kind originate a good — bring it into existence
        /// in town, by importing it off-map or producing it locally? Such a
        /// building is a wholesale SOURCE others can restock from (B2B, G4).
        public static bool Originates(BuildingKind kind, Good good)
        {
            var imp = Imports(kind);
            for (int i = 0; i < imp.Length; i++) if (imp[i] == good) return true;
            var pro = Produces(kind);
            for (int i = 0; i < pro.Length; i++) if (pro[i] == good) return true;
            return false;
        }

        /// Goods a building sells but doesn't source itself — it must buy them
        /// wholesale from a business that does (B2B, G4). Derived from Stocks minus
        /// what it imports/produces, so it can't drift: a tavern sells provisions
        /// and drink it neither imports nor makes, so it buys both from stores.
        public static Good[] B2BNeeds(BuildingKind kind)
        {
            var sells = Stocks(kind);
            int n = 0;
            for (int i = 0; i < sells.Length; i++) if (!Originates(kind, sells[i])) n++;
            if (n == 0) return System.Array.Empty<Good>();
            var need = new Good[n];
            int j = 0;
            for (int i = 0; i < sells.Length; i++) if (!Originates(kind, sells[i])) need[j++] = sells[i];
            return need;
        }

        /// Does a building sell a (stockless) SERVICE for this activity — a visit
        /// that's really patronage, paid to the keeper (G5)? Temple offerings,
        /// guild dues, bank fees: institutions earn from what they provide, so
        /// they're funded like any business, not special-cased. A library/palace
        /// is a free public good (no fee). Services have no shelf, so they're never
        /// stock-gated — only the visit's open hours apply.
        public static bool IsPaidService(BuildingKind kind, ActivityKind activity)
        {
            if (activity != ActivityKind.Visit) return false;
            switch (kind)
            {
                case BuildingKind.Temple:
                case BuildingKind.GuildHall:
                case BuildingKind.Bank:
                    return true;
                default:
                    return false;
            }
        }

        /// The good a building serves for a given consumer activity (the thing
        /// drawn off its shelf in a B2C sale). A tavern serves provisions as meals
        /// (EatTavern) and drink for socialising; a shop sells whatever it stocks
        /// (its primary good — provisions at a general store, wares at a craftsman).
        /// Returns false if the activity sells nothing here.
        public static bool SaleGoodFor(BuildingKind kind, ActivityKind activity, out Good good)
        {
            switch (activity)
            {
                case ActivityKind.EatTavern:
                    good = Good.Provisions; return kind == BuildingKind.Tavern;
                case ActivityKind.Socialize:
                    good = Good.Drink; return kind == BuildingKind.Tavern;
                case ActivityKind.Buy:
                    var sells = Stocks(kind);
                    if (sells.Length > 0) { good = sells[0]; return true; }
                    break;
            }
            good = Good.Provisions;
            return false;
        }

        static readonly Good[] Staples  = { Good.Provisions, Good.Drink };
        static readonly Good[] WaresOnly = { Good.Wares };
        static readonly Good[] ProvisionsOnly = { Good.Provisions };
        static readonly Good[] DrinkOnly = { Good.Drink };
        static readonly Good[] OreOnly = { Good.Ore };
    }
}
