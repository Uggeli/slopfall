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
        Wool,            // raw fibre — sheared at a pasture; material for cloth
        Cloth,           // material — a weaver turns wool into cloth
        Clothes,         // finished — a clothier turns cloth into clothes (Wearable)
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
        public const int Count = 7;

        // Indexed by (int)Good. The single source of authored prices.
        public static readonly GoodDef[] Defs =
        {
            new GoodDef { Good = Good.Provisions, Name = "provisions", Base = 0.02,  WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 500 },
            new GoodDef { Good = Good.Drink,      Name = "drink",      Base = 0.015, WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 300 },
            new GoodDef { Good = Good.Wares,      Name = "wares",      Base = 0.05,  WholesaleMarkup = 2.0, RetailMarkup = 3.0, WorldDemandPerDay = 200 },
            new GoodDef { Good = Good.Ore,        Name = "ore",        Base = 0.04,  WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 200 },
            new GoodDef { Good = Good.Wool,       Name = "wool",       Base = 0.02,  WholesaleMarkup = 1.5, RetailMarkup = 2.5, WorldDemandPerDay = 300 },
            new GoodDef { Good = Good.Cloth,      Name = "cloth",      Base = 0.05,  WholesaleMarkup = 1.6, RetailMarkup = 2.6, WorldDemandPerDay = 200 },
            new GoodDef { Good = Good.Clothes,    Name = "clothes",    Base = 0.12,  WholesaleMarkup = 1.7, RetailMarkup = 2.8, WorldDemandPerDay = 150 },
        };

        public static GoodDef Def(Good good) => Defs[(int)good];

        /// A primary-sector workplace (farm, fishery): a sim-native production site
        /// whose output scales with the hands working it. Drives the worker-scaling and
        /// wage-share in EconomySystem so a second industry slots in as data.
        public static bool IsPrimaryWorkplace(BuildingKind kind)
            => kind == BuildingKind.Farm || kind == BuildingKind.Fishery || kind == BuildingKind.Mine
               || kind == BuildingKind.Pasture;

        /// A sim-native workplace staffed by laborers (hands): its output scales with the
        /// hands working it and its till is shared among them — vs a DF craft shop a lone
        /// keeper runs at a flat rate. The primary producers plus the synth secondary
        /// workshops (weaver, …). EconomySystem's hands-count + scaling + wage-share key
        /// off this. (Industry layers.)
        public static bool IsStaffedWorkplace(BuildingKind kind)
            => IsPrimaryWorkplace(kind) || kind == BuildingKind.Weaver;

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

        /// Mint a fresh DISCRETE item of a good — its atom bundle: the affordance(s) the
        /// good affords + its market worth + weight. The single place a `Good` becomes
        /// an `ItemData`, so individuation (theft, carry, buy, wear) is one path for every
        /// good — `GoodsCatalog` is the item-atom source too (docs/items_and_inventory.md
        /// "a good IS an item"). Owner/location are the caller's to set.
        public static ItemData NewItem(Good good)
        {
            var item = new ItemData
            {
                Name = Def(good).Name,
                Valuable = PriceOf(good, PriceTier.Retail),   // market worth = the consumer price
                Weight = WeightOf(good),
                EquipSlot = -1,
            };
            switch (good)
            {
                case Good.Provisions: item.Edible = 1.0; break;     // food — one provision = one person-day (the metabolic anchor)
                case Good.Drink:      item.Drinkable = 0.5; break;  // ale
                case Good.Clothes:    item.Wearable = 0.5; break;   // worn
                // Wool / Cloth / Wares / Ore: raw or intermediate materials — no consumer
                // affordance, only worth + weight (they're inputs, sold or worked, not used).
            }
            return item;
        }

        static double WeightOf(Good good)
        {
            switch (good)
            {
                case Good.Ore:     return 1.0;   // heavy raw stone/metal
                case Good.Clothes: return 0.6;
                case Good.Wool:    return 0.5;
                case Good.Wares:   return 0.5;
                case Good.Cloth:   return 0.4;
                case Good.Drink:   return 0.4;
                default:           return 0.3;   // provisions
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
                case BuildingKind.Pasture:
                    return WoolOnly;       // raw fibre it shears and sells on
                case BuildingKind.Weaver:
                    return ClothOnly;      // cloth it weaves from wool
                case BuildingKind.Mine:
                    return OreOnly;        // the ore it digs and exports
                case BuildingKind.GeneralStore:
                    return Staples;        // provisions (sourced local) + drink (imported)
                case BuildingKind.Tavern:
                    return Staples;        // raw provisions + drink, served as meals/ale
                case BuildingKind.ClothingStore:
                    return ClothesOnly;    // finished clothing it tailors from cloth
                case BuildingKind.Alchemist:
                case BuildingKind.Armorer:
                case BuildingKind.Bookseller:
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
                case BuildingKind.FurnitureStore:
                case BuildingKind.GemStore:
                case BuildingKind.PawnShop:
                case BuildingKind.WeaponSmith:
                    return WaresOnly;
                case BuildingKind.ClothingStore:
                    return ClothesOnly;      // cloth → clothes (the finished-good stage)
                case BuildingKind.Weaver:
                    return ClothOnly;        // wool → cloth (intermediate)
                case BuildingKind.Farm:
                case BuildingKind.Fishery:
                    return ProvisionsOnly;   // local food: the primary-sector faucet
                case BuildingKind.Pasture:
                    return WoolOnly;         // raw fibre: a primary-sector output
                case BuildingKind.Mine:
                    return OreOnly;          // raw ore: the mountain-town export faucet
                default:
                    return System.Array.Empty<Good>();
            }
        }

        /// The recipe inputs a building consumes to make its output — the edges of
        /// the production graph (docs/industry_layers.md). Primary producers
        /// (farm/fishery/mine/pasture) take none; their output comes from the land. A
        /// secondary/finished producer eats material: a weaver eats wool, a clothier
        /// eats cloth. Multi-input allowed (armour = metal + leather, later).
        public static Good[] Inputs(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Weaver:        return WoolOnly;    // wool → cloth
                case BuildingKind.ClothingStore: return ClothOnly;   // cloth → clothes
                default:                         return System.Array.Empty<Good>();
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
            // A building sources (B2B) both the goods it SELLS but doesn't originate
            // AND the recipe INPUTS it consumes but doesn't originate (a weaver buys
            // wool; a clothier buys cloth). The sold-set and input-set are disjoint here.
            var sells = Stocks(kind);
            var inputs = Inputs(kind);
            int n = 0;
            for (int i = 0; i < sells.Length; i++) if (!Originates(kind, sells[i])) n++;
            for (int i = 0; i < inputs.Length; i++) if (!Originates(kind, inputs[i])) n++;
            if (n == 0) return System.Array.Empty<Good>();
            var need = new Good[n];
            int j = 0;
            for (int i = 0; i < sells.Length; i++) if (!Originates(kind, sells[i])) need[j++] = sells[i];
            for (int i = 0; i < inputs.Length; i++) if (!Originates(kind, inputs[i])) need[j++] = inputs[i];
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
                case ActivityKind.Steal:
                    // A thief takes food: provisions, from anywhere that stocks them
                    // (a general store, a tavern's larder of meals). Non-food shops
                    // have nothing edible to take.
                    good = Good.Provisions;
                    var held = Stocks(kind);
                    for (int i = 0; i < held.Length; i++) if (held[i] == Good.Provisions) return true;
                    return false;
            }
            good = Good.Provisions;
            return false;
        }

        static readonly Good[] Staples  = { Good.Provisions, Good.Drink };
        static readonly Good[] WaresOnly = { Good.Wares };
        static readonly Good[] ProvisionsOnly = { Good.Provisions };
        static readonly Good[] DrinkOnly = { Good.Drink };
        static readonly Good[] OreOnly = { Good.Ore };
        static readonly Good[] WoolOnly = { Good.Wool };
        static readonly Good[] ClothOnly = { Good.Cloth };
        static readonly Good[] ClothesOnly = { Good.Clothes };
    }
}
