using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the legacy DaggerfallWorkshop.Sim.EconomySystem. The economy
    // logic is preserved EXACTLY (harvest, B2C/B2B trade, payday, monthly tax, civic
    // dividend, exports, conservation accounting); only the plumbing changed:
    //   - reads registries READ-ONLY, holds NO tick-state fields (statelessness is
    //     structural — there is nothing to spool across ticks here);
    //   - writes ONLY by emitting delta/transfer intents (the registries SUM deltas,
    //     so this just publishes per-transaction deltas, order-free);
    //   - cadence (payday / tax) comes from reacting to NewDayEvent / NewMonthEvent
    //     this tick, not from an armed _payday/_taxDue flag.
    //
    // The three pieces of genuine cross-tick state are homed elsewhere:
    //   - per-worker accrued wage (_earnedToday)  → EarningsRegistry;
    //   - the cumulative audit ledger             → LedgerData itself (read the current
    //                                                row, add this tick, re-emit whole);
    //   - everything else (_order/_payOrder/_farmWorkers/_keeperOf/_sourcesByGood) was
    //     per-tick derived → rebuilt as LOCALS in Update().
    //
    // Within a single tick, a transfer that depends on a prior one (A pays B who pays C)
    // is resolved against a LOCAL working balance map so the chained read sees the
    // in-tick spend before the registry settles it next tick. CoinRegistry's clamp-once
    // makes the emitted deltas net correctly regardless.
    public sealed class EconomySystem : SimSystem
    {
        // --- economy constants (copied verbatim from the legacy EconomySystem) ---
        public const double LaborDailyWage = 3.0;
        public const double WorkdayMinutes = 600.0;
        public const double StockTarget = 40.0;
        public const double ImportPerMinute = 1.0;
        public const double FarmProducePerWorkerMinute = 0.010;
        public const int FarmCapacity = 50;
        public const double InKindProvisionsPerMinute = 0.01;
        public const double ProducePerMinute = 0.5;
        public const double ExportFloorFraction = 0.5;
        public const double WarehouseCap = StockTarget * 3.0;
        public const double TaxExemption = 10.0;
        public const double TaxRatePerMonth = 0.3;
        public const double GuardDailyWage = 6.0;
        public const double CivicDividendPerMinute = 0.0002;

        readonly CoinRegistry _coin;
        readonly StockRegistry _stock;
        readonly LarderRegistry _larder;
        readonly TreasuryRegistry _treasury;
        readonly LedgerRegistry _ledger;
        readonly WorldMarketRegistry _market;
        readonly EarningsRegistry _earnings;
        readonly BehaviorRegistry _behavior;
        readonly BuildingRegistry _buildings;
        readonly ResidencyRegistry _residency;
        readonly EmploymentRegistry _employment;
        readonly SettlementRegistry _settlements;
        readonly PlaceMemoryRegistry _places;   // read only (Recall etc. live elsewhere); writes go via PlaceNoteIntent
        readonly WorldClockRegistry _clock;

        public EconomySystem(
            EventBus events,
            CoinRegistry coin,
            StockRegistry stock,
            LarderRegistry larder,
            TreasuryRegistry treasury,
            LedgerRegistry ledger,
            WorldMarketRegistry market,
            EarningsRegistry earnings,
            BehaviorRegistry behavior,
            BuildingRegistry buildings,
            ResidencyRegistry residency,
            EmploymentRegistry employment,
            SettlementRegistry settlements,
            PlaceMemoryRegistry places,
            WorldClockRegistry clock) : base(events)
        {
            _coin = coin;
            _stock = stock;
            _larder = larder;
            _treasury = treasury;
            _ledger = ledger;
            _market = market;
            _earnings = earnings;
            _behavior = behavior;
            _buildings = buildings;
            _residency = residency;
            _employment = employment;
            _settlements = settlements;
            _places = places;
            _clock = clock;
        }

        // Per-tick working state. NOT fields — passed by ref into the helpers so the
        // system itself holds nothing across ticks. Bundled so the long economy helpers
        // keep their original shape without a dozen ref params.
        sealed class Tick
        {
            public long Index;
            public double GameMinutes;
            // Local working coin balances: read Coin.Get the first time, then mutate
            // locally so a chained in-tick transfer (sale → keeper, then keeper restocks)
            // sees the running balance. Net deltas are emitted as CoinTransferEvents.
            public readonly Dictionary<EntityId, double> Bal = new Dictionary<EntityId, double>();
            // Local working stock so an in-tick produce-then-export / draw-then-restock
            // reads the running shelf, not last tick's settled value.
            public readonly Dictionary<long, double> Stock = new Dictionary<long, double>();
            // Local working treasuries (tax in → guard pay out → dividend out, same tick).
            public readonly Dictionary<int, double> Treasury = new Dictionary<int, double>();
            // Local working world-market tally so an earlier seller's glut depresses the
            // export price for a later seller THIS tick (the legacy saw immediate sells).
            public readonly double[] MarketBought = new double[GoodsCatalog.Count];
            public bool MarketReset;   // day rollover zeroed the tally before this tick's sells
            // Derived-once-per-tick lookups (were per-tick fields on the legacy class).
            public readonly Dictionary<int, EntityId> KeeperOf = new Dictionary<int, EntityId>();
            public readonly Dictionary<Good, List<int>> SourcesByGood = new Dictionary<Good, List<int>>();
            // Audit accumulators for THIS tick (added onto the carried ledger at the end).
            public double Minted, Sunk, Exports, CrownSubsidy, SalesRevenue, ServiceRevenue;
            public double Alms, Imports, Wholesale, Taxes, GuardPay;
            public readonly double[] Produced = new double[GoodsCatalog.Count];
            public readonly double[] Imported = new double[GoodsCatalog.Count];
            public readonly double[] Exported = new double[GoodsCatalog.Count];
            public readonly double[] Consumed = new double[GoodsCatalog.Count];
        }

        static long StockKey(int building, Good good) => ((long)building << 8) | (uint)good;

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = clock.DeltaGameSeconds / 60.0;
            if (gameMinutes <= 0) return;

            var t = new Tick { Index = tick, GameMinutes = gameMinutes };
            BuildKeeperOf(t);

            // Escheat (L2): a dead civilian's purse passes to its settlement treasury.
            // LifecycleSystem fires EscheatEvent{Dead} (only for entities that HAD a
            // settlement) AND a paired DespawnedEvent{Entity,Settlement,...} the same
            // tick — the settlement id we need lives on the latter. CoinRegistry removes
            // the dead purse off DespawnedEvent, so here we only move the value
            // purse → treasury (a conserved transfer — no faucet/sink). Run first so it
            // lands before any other treasury mutation this tick.
            ProcessEscheats(t);

            // Alms audit (RequestSystem's CoinTransferEvents are APPLIED by CoinRegistry,
            // not here — we only TALLY them for the ledger, capping at the payer's purse
            // exactly as the legacy ProcessEvents did). RequestSystem alms are always
            // entity→entity (both From and To set); the economy's OWN coin moves (emitted
            // by FlushBalances) are deliberately half-open (one side None) so this tally
            // skips them — their faucet/sink/transfer categories are recorded explicitly.
            TallyTransfers(t);

            // Tax man (E3): on a month rollover, drain wealth above the exemption from
            // each purse → its settlement treasury (a conserved transfer).
            if (Events.GetEvents<NewMonthEvent>().Length > 0) CollectMonthlyTax(t);

            // Payday (NewDayEvent): wages move once a day. Each hand paid what it accrued
            // (from its employer's till, capped), guards a flat salary from the treasury
            // (crown mints the shortfall). On the day rollover external demand replenishes
            // — emit the market reset (replaces the legacy WorldMarket.ResetDay()).
            if (Events.GetEvents<NewDayEvent>().Length > 0)
            {
                Events.Publish(new MarketResetIntent());
                t.MarketReset = true;     // this tick's exports measure against a fresh quota
                PayDay(t);
            }

            // Count the hands working each staffed site this tick (its harvest scales
            // with them, capped by land).
            var farmWorkers = new Dictionary<int, int>();
            foreach (var kvb in _behavior.All)
            {
                var bh = kvb.Value;
                if (bh == null || bh.Phase != ActivityPhase.Doing || bh.TargetBuilding < 0) continue;
                if (bh.Activity != ActivityKind.Farm && bh.Activity != ActivityKind.Fish
                    && bh.Activity != ActivityKind.Mine && bh.Activity != ActivityKind.Labor
                    && bh.Activity != ActivityKind.Weave) continue;
                if (!_buildings.TryGet(bh.TargetBuilding, out var wb) || wb == null
                    || !GoodsCatalog.IsStaffedWorkplace(wb.Kind)) continue;
                farmWorkers.TryGetValue(bh.TargetBuilding, out var c);
                farmWorkers[bh.TargetBuilding] = c + 1;
            }

            // Harvest pass: a staffed site's output is produced off the HANDS working it
            // (keeper-independent), so a field full of laborers feeds the market even
            // when the keeper sleeps. Runs before the trade loop so the day's output is
            // on the shelf when stores source it B2B.
            foreach (var kvw in farmWorkers)
                if (kvw.Value > 0 && _buildings.TryGet(kvw.Key, out var wb) && wb != null)
                    ProduceAtWorkplace(t, kvw.Key, wb, kvw.Value);

            // Deterministic walk over the EntityId-sorted behavior keys (shared shelves
            // and tills make the outcome order-dependent — audit F3).
            var order = SortedKeys(_behavior.All);
            for (int oi = 0; oi < order.Count; oi++)
            {
                var id = order[oi];
                if (!_behavior.TryGet(id, out var behavior) || behavior == null) continue;

                if (behavior.Phase == ActivityPhase.Doing)
                {
                    switch (behavior.Activity)
                    {
                        case ActivityKind.Work:
                            RunBusiness(t, id, behavior.TargetBuilding);
                            break;
                        case ActivityKind.Farm:
                        case ActivityKind.Fish:
                        case ActivityKind.Mine:
                        case ActivityKind.Labor:
                        case ActivityKind.Weave:
                            // A hand accrues its pro-rated daily wage (settled on payday).
                            Events.Publish(new EarningsDeltaIntent
                            {
                                Id = id,
                                Delta = LaborDailyWage * gameMinutes / WorkdayMinutes,
                            });
                            // In-kind subsistence: a food-producer (farm/fishery) hand
                            // takes provisions home for the larder. A miner makes ore → cash only.
                            if (behavior.Activity == ActivityKind.Farm || behavior.Activity == ActivityKind.Fish)
                            {
                                int fh = HomeOf(id);
                                double inKind = InKindProvisionsPerMinute * gameMinutes;
                                if (fh >= 0) Events.Publish(new LarderDeltaIntent { Building = fh, Delta = inKind });
                                t.Produced[(int)Good.Provisions] += inKind;
                                Note(id, fh, PlaceFact.ProvisionsHere, 1, t.Index);
                            }
                            break;
                        case ActivityKind.EatTavern:
                        case ActivityKind.Socialize:
                        case ActivityKind.Buy:
                            PaySale(t, id, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), isService: false);
                            break;
                        case ActivityKind.Visit:
                            PaySale(t, id, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), isService: true);
                            break;
                        case ActivityKind.Steal:
                            StealProvisions(t, id, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity));
                            break;
                        case ActivityKind.EatHome:
                            int eatHome = HomeOf(id);
                            double larderBefore = _larder.Get(eatHome);
                            double draw = ActivityCatalog.EatHome.LarderUnitsPerMinute * gameMinutes;
                            double afterMeal = larderBefore - draw;
                            if (afterMeal < 0) afterMeal = 0;            // clamp mirrors Larder.Add
                            if (eatHome >= 0 && draw > 0)
                                Events.Publish(new LarderDeltaIntent { Building = eatHome, Delta = -draw });
                            t.Consumed[(int)Good.Provisions] += larderBefore - afterMeal;
                            Note(id, eatHome, PlaceFact.ProvisionsHere, afterMeal > 1e-6 ? 1 : 0, t.Index);
                            break;
                    }
                }
            }

            // Recirculate each settlement's treasury back to its residents (poor relief).
            DistributeTreasury(t);

            // Emit the net coin deltas accumulated in the local working balances. Each
            // entity that lost/gained net coin this tick gets one CoinTransferEvent
            // against the "outside" (From/To None), which CoinRegistry sums into its
            // balance. Faucets (exports, crown) and sinks (imports) are part of those
            // nets; the audit below records them by category, not by residual.
            FlushBalances(t);

            // Conservation accounting — carried in the ledger row itself. Read the
            // current (last-tick) cumulative row, add this tick's category contributions,
            // emit the new whole row. Minted/Sunk are computed EXPLICITLY here (the
            // legacy used a before/after SumCoin residual, which equals: minted =
            // exports + crown subsidy; sunk = imports off-map — both tracked directly).
            EmitLedger(t);
        }

        // --- escheat -----------------------------------------------------------------

        void ProcessEscheats(Tick t)
        {
            var escheats = Events.GetEvents<EscheatEvent>();
            if (escheats.Length == 0) return;

            // The settlement id for each escheating entity comes from its paired
            // DespawnedEvent this tick (EscheatEvent itself only carries Dead).
            var settlementOf = new Dictionary<EntityId, int>();
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                settlementOf[d.Entity] = d.Settlement;

            foreach (var e in escheats)
            {
                if (!settlementOf.TryGetValue(e.Dead, out int settlement)) continue;
                if (settlement < 0 || settlement >= _settlements.Count) continue;
                double purse = _coin.Get(e.Dead);
                if (purse <= 0) continue;
                // CoinRegistry removes the dead row off the paired DespawnedEvent, so we
                // only move the value into the treasury (a conserved transfer — no faucet/
                // sink tally). NOT a CoinTransferEvent: those are TALLIED (alms/sink) and
                // APPLIED by CoinRegistry, which would double-remove the purse.
                var treasury = _settlements.Get(settlement).Treasury;
                TreasuryAdd(t, treasury, purse);
            }
        }

        // --- alms audit (transfers applied by CoinRegistry; tallied here) ------------

        void TallyTransfers(Tick t)
        {
            foreach (var tr in Events.GetEvents<CoinTransferEvent>())
            {
                if (tr.Amount <= 0) continue;
                // ONLY genuine entity→entity alms (both sides set). Half-open transfers
                // are the economy's own FlushBalances mint/burn — already categorized.
                if (tr.From.IsNone || tr.To.IsNone) continue;
                double have = _coin.Get(tr.From);   // cap exactly as the legacy did
                if (have <= 0) continue;
                double amount = tr.Amount > have ? have : tr.Amount;
                t.Alms += amount;
            }
        }

        // --- monthly tax (E3) --------------------------------------------------------

        void CollectMonthlyTax(Tick t)
        {
            var settlements = _settlements.All;
            for (int si = 0; si < settlements.Count; si++)
            {
                var s = settlements[si];
                double rate = TaxRateFor(s.Kind);
                if (rate <= 0) continue;

                var ids = new List<EntityId>(s.Residents);
                ids.Sort((a, b) => a.Value.CompareTo(b.Value));   // deterministic float sum (F3)

                double collected = 0;
                for (int oi = 0; oi < ids.Count; oi++)
                {
                    var id = ids[oi];
                    double coin = Coin(t, id);
                    double taxable = coin - TaxExemption;
                    if (taxable <= 0) continue;
                    double tax = taxable * rate;
                    SetCoin(t, id, coin - tax);
                    collected += tax;
                }
                if (collected > 0)
                {
                    TreasuryAdd(t, s.Treasury, collected);
                    t.Taxes += collected;
                }
            }
        }

        static double TaxRateFor(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.City:    return TaxRatePerMonth;          // 0.30
                case SettlementKind.Hamlet:  return TaxRatePerMonth * 0.5;    // 0.15
                case SettlementKind.Village: return TaxRatePerMonth * 0.33;   // 0.10
                default:                     return TaxRatePerMonth * 0.17;   // ~0.05
            }
        }

        // --- payday ------------------------------------------------------------------

        void PayDay(Tick t)
        {
            // Hands: settle accrued wages from employers' tills (capped at the till).
            var hands = new List<EntityId>();
            foreach (var kv in _earnings.All) if (kv.Value > 0) hands.Add(kv.Key);
            hands.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < hands.Count; i++)
            {
                var w = hands[i];
                double paid = PayWage(t, w, _earnings.Get(w));     // employer → worker, capped
                if (paid > 0) SetCoin(t, w, Coin(t, w) + paid);
                Events.Publish(new EarningsResetIntent { Id = w });   // settled — zero the accrual
            }

            // Guards: flat daily salary from the local treasury; crown mints the gap.
            var guards = new List<EntityId>();
            foreach (var kv in _employment.All)
                if (kv.Value != null && !kv.Value.PublicOwner.IsNone) guards.Add(kv.Key);
            guards.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < guards.Count; i++)
            {
                var g = guards[i];
                if (!_employment.TryGet(g, out var emp) || emp == null || emp.PublicOwner.IsNone) continue;
                double drawn = PayGuard(t, emp.PublicOwner, GuardDailyWage);   // treasury → guard, capped
                SetCoin(t, g, Coin(t, g) + GuardDailyWage);                    // paid in full
                t.GuardPay += drawn;
                double shortfall = GuardDailyWage - drawn;
                if (shortfall > 0)                                            // crown mints only the gap (F1)
                {
                    t.CrownSubsidy += shortfall;
                    t.Minted += shortfall;
                }
            }
        }

        double PayGuard(Tick t, OwnerId owner, double wage)
        {
            double have = Treasury(t, owner);
            if (have <= 0) return 0;
            if (wage > have) wage = have;
            TreasuryAdd(t, owner, -wage);
            return wage;
        }

        double PayWage(Tick t, EntityId worker, double wage)
        {
            if (!_employment.TryGet(worker, out var emp) || emp == null || emp.Employer.IsNone) return 0;
            double have = Coin(t, emp.Employer);
            if (have <= 0) return 0;
            if (wage > have) wage = have;
            SetCoin(t, emp.Employer, have - wage);
            return wage;
        }

        // --- production --------------------------------------------------------------

        void ProduceAtWorkplace(Tick t, int building, BuildingRow b, int hands)
        {
            if (hands > FarmCapacity) hands = FarmCapacity;
            var produced = GoodsCatalog.Produces(b.Kind);
            var recipeInputs = GoodsCatalog.Inputs(b.Kind);
            for (int i = 0; i < produced.Length; i++)
            {
                var good = produced[i];
                if (Stock(t, building, good) >= WarehouseCap) continue;            // glut → idle
                double output = hands * FarmProducePerWorkerMinute * t.GameMinutes;
                for (int k = 0; k < recipeInputs.Length; k++)                      // recipe: 1 input per unit out
                {
                    double have = Stock(t, building, recipeInputs[k]);
                    if (have < output) output = have;
                }
                if (output <= 0) continue;
                for (int k = 0; k < recipeInputs.Length; k++)
                    AddStock(t, building, recipeInputs[k], -output);
                AddStock(t, building, good, output);
                t.Produced[(int)good] += output;
            }
        }

        // --- business (Work) ---------------------------------------------------------

        void RunBusiness(Tick t, EntityId keeper, int building)
        {
            if (!_buildings.TryGet(building, out var b) || b == null) return;

            // Legacy budget semantics: imports/B2B are paid out of the keeper's coin AS
            // IT STOOD ENTERING Work — this tick's export earnings are NOT yet available
            // (the caller added them to `next` only AFTER RunBusiness returned). So we
            // snapshot the entering balance, spend against a running `available - spent`,
            // and credit the deferred export income at the very end.
            double available = Coin(t, keeper);
            double exported = 0;

            var produced = GoodsCatalog.Produces(b.Kind);
            var recipeInputs = GoodsCatalog.Inputs(b.Kind);
            for (int i = 0; i < produced.Length; i++)
            {
                var good = produced[i];
                // Lone-keeper craft produces here (staffed sites produce in the harvest
                // pass), unless the warehouse is full of unsold stock.
                if (!GoodsCatalog.IsStaffedWorkplace(b.Kind) && Stock(t, building, good) < WarehouseCap)
                {
                    double output = ProducePerMinute * t.GameMinutes;
                    for (int k = 0; k < recipeInputs.Length; k++)                  // recipe-gated (e.g. weaver needs wool)
                    {
                        double have = Stock(t, building, recipeInputs[k]);
                        if (have < output) output = have;
                    }
                    if (output > 0)
                    {
                        for (int k = 0; k < recipeInputs.Length; k++)
                            AddStock(t, building, recipeInputs[k], -output);
                        AddStock(t, building, good, output);
                        t.Produced[(int)good] += output;
                    }
                }

                // Export the surplus above StockTarget — only if the world market still
                // pays above the reservation; else hold the surplus.
                double surplus = Stock(t, building, good) - StockTarget;
                if (surplus > 0)
                {
                    double wholesalePrice = GoodsCatalog.PriceOf(good, PriceTier.Wholesale);
                    double price = ExportPrice(t, good, wholesalePrice);
                    if (price >= wholesalePrice * ExportFloorFraction)
                    {
                        AddStock(t, building, good, -surplus);                    // surplus leaves town
                        Events.Publish(new MarketSellIntent { Good = good, Units = surplus });  // glut the market (registry sums)
                        t.MarketBought[(int)good] += surplus;                     // and locally, so the NEXT seller this tick sees the glut
                        double earned = surplus * price;
                        exported += earned;                                       // coin IN (faucet) — credited AFTER imports/B2B (legacy)
                        t.Exports += earned;
                        t.Minted += earned;
                        t.Exported[(int)good] += surplus;
                    }
                    // else: hold (warehoused), wait for demand to recover.
                }
            }

            // Imports off-map (coin out — a sink). Budget = the entering balance minus
            // imports already paid (legacy `available - imports`).
            double importsPaid = 0;
            var imported = GoodsCatalog.Imports(b.Kind);
            for (int i = 0; i < imported.Length; i++)
            {
                double spent = Acquire(t, keeper, building, imported[i], ImportPerMinute * t.GameMinutes,
                    GoodsCatalog.PriceOf(imported[i], PriceTier.Import), -1, available - importsPaid);
                if (spent > 0) { importsPaid += spent; t.Imports += spent; t.Sunk += spent; }   // off-map: coin destroyed
            }

            // B2B wholesale (a transfer — coin stays in town). Budget = entering balance
            // minus imports AND wholesale already paid (legacy `available - imports - wholesale`).
            double wholesalePaid = 0;
            var needs = GoodsCatalog.B2BNeeds(b.Kind);
            for (int i = 0; i < needs.Length; i++)
            {
                int source = NearestSource(t, needs[i], building, b.X, b.Z);
                if (source < 0) continue;
                double wholesalePrice = GoodsCatalog.PriceOf(needs[i], PriceTier.Wholesale)
                                      * GoodsCatalog.Scarcity(Stock(t, source, needs[i]));
                double spent = Acquire(t, keeper, building, needs[i], ImportPerMinute * t.GameMinutes,
                    wholesalePrice, source, available - importsPaid - wholesalePaid);
                if (spent > 0) { wholesalePaid += spent; t.Wholesale += spent; }   // seller credited inside Acquire
            }

            // Settle the keeper's purse: entering balance − imports − wholesale + exports
            // (exports credited last, exactly as the legacy caller did: next += exported).
            SetCoin(t, keeper, available - importsPaid - wholesalePaid + exported);
        }

        double ExportPrice(Tick t, Good good, double wholesale)
        {
            double quota = GoodsCatalog.Def(good).WorldDemandPerDay;
            if (quota <= 0) return wholesale;
            // Measure against this tick's running tally: the registry's settled value
            // (unless the day just rolled over → fresh quota) PLUS what earlier sellers
            // already dumped this tick.
            double baseBought = t.MarketReset ? 0 : _market.BoughtOf(good);
            double fill = (baseBought + t.MarketBought[(int)good]) / quota;
            return fill <= 1.0 ? wholesale : wholesale / fill;
        }

        /// Move up to `units` of a good onto a building's shelf without overshooting
        /// StockTarget, paying `price`/unit within `budget`. Returns coin spent (the
        /// CALLER debits the keeper; here we credit a B2B seller and move stock). Same
        /// semantics as the legacy Acquire, minus the in-place keeper debit.
        double Acquire(Tick t, EntityId keeper, int dst, Good good, double units, double price, int sourceBuilding, double budget)
        {
            double room = StockTarget - Stock(t, dst, good);
            if (room <= 0) return 0;
            if (units > room) units = room;

            if (sourceBuilding >= 0)
            {
                double srcStock = Stock(t, sourceBuilding, good);
                if (units > srcStock) units = srcStock;       // can't buy more than they stock
            }

            double cost = units * price;
            if (price > 0)
            {
                if (budget <= 0) return 0;
                if (cost > budget) { cost = budget; units = cost / price; }
            }
            if (units <= 0) return 0;

            AddStock(t, dst, good, units);
            if (sourceBuilding < 0 && price > 0) t.Imported[(int)good] += units;   // bought off-map
            if (sourceBuilding >= 0)
            {
                AddStock(t, sourceBuilding, good, -units);
                var seller = KeeperOf(t, sourceBuilding);
                if (!seller.IsNone) SetCoin(t, seller, Coin(t, seller) + cost);
            }
            return price > 0 ? cost : 0;
        }

        int NearestSource(Tick t, Good good, int buyer, float bx, float bz)
        {
            if (!t.SourcesByGood.TryGetValue(good, out var sources))
            {
                sources = new List<int>();
                foreach (var kv in _buildings.All)
                    if (kv.Value != null && GoodsCatalog.Originates(kv.Value.Kind, good))
                        sources.Add(kv.Key);
                t.SourcesByGood[good] = sources;
            }

            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < sources.Count; i++)
            {
                int s = sources[i];
                if (s == buyer || Stock(t, s, good) <= 0) continue;
                if (!_buildings.TryGet(s, out var sb) || sb == null) continue;
                double dx = sb.X - bx, dz = sb.Z - bz;
                double d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        // --- B2C sale / service / theft ----------------------------------------------

        void PaySale(Tick t, EntityId patron, int building, ActivityCatalog.Spec spec, bool isService)
        {
            if (spec == null || spec.SaleUnits <= 0 || spec.DurationMinutes <= 0) return;
            double patronCoin = Coin(t, patron);
            if (patronCoin <= 0) return;
            if (!_buildings.TryGet(building, out var b) || b == null) return;

            bool goods = GoodsCatalog.SaleGoodFor(b.Kind, spec.Kind, out var good);
            bool service = !goods && GoodsCatalog.IsPaidService(b.Kind, spec.Kind);
            if (!goods && !service) return;                                      // nothing for sale here

            double units = spec.SaleUnits / spec.DurationMinutes * t.GameMinutes; // this tick's share
            double price = spec.SalePrice;
            if (goods)
            {
                double available = Stock(t, building, good);
                price *= GoodsCatalog.Scarcity(available);                       // local price floats with the shelf
                if (units > available) units = available;                        // no stock → no sale
            }
            double affordable = price > 0 ? patronCoin / price : units;
            if (units > affordable) units = affordable;                          // can't buy what you can't pay for
            if (units <= 0) return;

            double bill = units * price;
            if (goods)
            {
                AddStock(t, building, good, -units);                             // off the shelf
                if (spec.Kind != ActivityKind.Buy) t.Consumed[(int)good] += units;   // eaten on the spot
            }

            // Shopping for the larder: provisions bought (Buy) go home to the larder.
            if (goods && spec.Kind == ActivityKind.Buy && good == Good.Provisions)
            {
                int ph = HomeOf(patron);
                if (ph >= 0) Events.Publish(new LarderDeltaIntent { Building = ph, Delta = units });
                Note(patron, ph, PlaceFact.ProvisionsHere, 1, t.Index);
            }

            // The patron ALWAYS pays `bill` (legacy: next -= PaySale(...)). The keeper is
            // credited only when it's a distinct, real entity — so a keeper buying at their
            // own shop (keeper == patron) or a keeperless sale loses the coin to nothing,
            // exactly as the legacy residual-sink captured it.
            SetCoin(t, patron, patronCoin - bill);
            var keeper = KeeperOf(t, building);
            if (!keeper.IsNone && keeper != patron)
            {
                SetCoin(t, keeper, Coin(t, keeper) + bill);
                if (isService) t.ServiceRevenue += bill; else t.SalesRevenue += bill;
            }
            else
                t.Sunk += bill;   // coin paid to nobody → destroyed (matches legacy before/after residual)
        }

        void StealProvisions(Tick t, EntityId thief, int building, ActivityCatalog.Spec spec)
        {
            if (spec == null || spec.SaleUnits <= 0 || spec.DurationMinutes <= 0) return;
            if (!_buildings.TryGet(building, out var b) || b == null) return;
            double available = Stock(t, building, Good.Provisions);
            if (available <= 0) return;
            double units = spec.SaleUnits / spec.DurationMinutes * t.GameMinutes;
            if (units > available) units = available;
            if (units <= 0) return;
            AddStock(t, building, Good.Provisions, -units);
            // The loot becomes carried loaves (ItemSystem mints them; keeper stays owner).
            Events.Publish(new ProvisionsTakenEvent
            {
                Taker = thief, Owner = KeeperOf(t, building), Building = building, Units = units,
            });
        }

        // --- civic dividend ----------------------------------------------------------

        void DistributeTreasury(Tick t)
        {
            var settlements = _settlements.All;
            for (int si = 0; si < settlements.Count; si++)
            {
                var s = settlements[si];
                if (s.Residents.Count == 0) continue;
                double bal = Treasury(t, s.Treasury);
                if (bal <= 0) continue;
                double per = bal * CivicDividendPerMinute * t.GameMinutes / s.Residents.Count;
                if (per <= 0) continue;

                var ids = new List<EntityId>(s.Residents);
                ids.Sort((a, b) => a.Value.CompareTo(b.Value));

                double paid = 0;
                for (int oi = 0; oi < ids.Count; oi++)
                {
                    SetCoin(t, ids[oi], Coin(t, ids[oi]) + per);
                    paid += per;
                }
                TreasuryAdd(t, s.Treasury, -paid);
            }
        }

        // --- local working-state accessors (read-through to the registry, then local) -

        double Coin(Tick t, EntityId id)
            => t.Bal.TryGetValue(id, out var c) ? c : _coin.Get(id);

        void SetCoin(Tick t, EntityId id, double v) => t.Bal[id] = v < 0 ? 0 : v;

        double Stock(Tick t, int building, Good good)
        {
            long k = StockKey(building, good);
            return t.Stock.TryGetValue(k, out var s) ? s : _stock.Get(building, good);
        }

        void AddStock(Tick t, int building, Good good, double delta)
        {
            long k = StockKey(building, good);
            double cur = t.Stock.TryGetValue(k, out var s) ? s : _stock.Get(building, good);
            double next = cur + delta;
            if (next < 0) next = 0;
            t.Stock[k] = next;
            Events.Publish(new StockDeltaIntent { Building = building, Good = good, Delta = delta });
        }

        double Treasury(Tick t, OwnerId owner)
            => t.Treasury.TryGetValue(owner.Value, out var v) ? v : _treasury.Get(owner);

        void TreasuryAdd(Tick t, OwnerId owner, double delta)
        {
            double cur = t.Treasury.TryGetValue(owner.Value, out var v) ? v : _treasury.Get(owner);
            double next = cur + delta;
            if (next < 0) next = 0;
            t.Treasury[owner.Value] = next;
            Events.Publish(new TreasuryDeltaIntent { Owner = owner, Delta = delta });
        }

        /// Net every locally-mutated purse against its registry value and emit ONE
        /// CoinTransferEvent against the outside for the difference. CoinRegistry sums
        /// these into the settled balances next tick. (Stock and treasury deltas are
        /// emitted as they happen, since their registries clamp-once on the summed total
        /// — coin is the same, but coin SetCoin clamps locally too, so a net delta is
        /// the faithful single source of truth for the purse.)
        void FlushBalances(Tick t)
        {
            foreach (var kv in t.Bal)
            {
                double before = _coin.Get(kv.Key);
                double delta = kv.Value - before;
                if (delta > 0) Events.Publish(new CoinTransferEvent { From = EntityId.None, To = kv.Key, Amount = delta });
                else if (delta < 0) Events.Publish(new CoinTransferEvent { From = kv.Key, To = EntityId.None, Amount = -delta });
            }
        }

        // --- ledger ------------------------------------------------------------------

        void EmitLedger(Tick t)
        {
            var prev = _ledger.Current;   // last tick's cumulative row (never mutated in place)
            var next = new LedgerData
            {
                Minted = prev.Minted + t.Minted,
                Sunk = prev.Sunk + t.Sunk,
                Exports = prev.Exports + t.Exports,
                CrownSubsidy = prev.CrownSubsidy + t.CrownSubsidy,
                SalesRevenue = prev.SalesRevenue + t.SalesRevenue,
                ServiceRevenue = prev.ServiceRevenue + t.ServiceRevenue,
                Alms = prev.Alms + t.Alms,
                Imports = prev.Imports + t.Imports,
                Wholesale = prev.Wholesale + t.Wholesale,
                Taxes = prev.Taxes + t.Taxes,
                GuardPay = prev.GuardPay + t.GuardPay,
                Produced = AddArr(prev.Produced, t.Produced),
                Imported = AddArr(prev.Imported, t.Imported),
                Exported = AddArr(prev.Exported, t.Exported),
                Consumed = AddArr(prev.Consumed, t.Consumed),
            };
            Events.Publish(new LedgerSetIntent { Value = next });
        }

        static double[] AddArr(double[] prev, double[] add)
        {
            var r = new double[GoodsCatalog.Count];
            if (prev != null)
                for (int i = 0; i < r.Length && i < prev.Length; i++) r[i] = prev[i];
            for (int i = 0; i < r.Length; i++) r[i] += add[i];
            return r;
        }

        // --- helpers -----------------------------------------------------------------

        int HomeOf(EntityId id)
            => _residency.TryGet(id, out var r) && r != null ? r.BuildingIndex : -1;

        void Note(EntityId id, int building, PlaceFact fact, double value, long tick)
        {
            if (building < 0) return;
            Events.Publish(new PlaceNoteIntent { Id = id, Building = building, Fact = fact, Value = value, Tick = tick });

            // Mirror the provisions observation into the rich PLACES atom store (the learned
            // layer ODD scores on in Phase 2). Owner-stamps-its-own: economy owns provisions.
            if (fact == PlaceFact.ProvisionsHere)
                Events.Publish(new PlaceObserveIntent
                { Agent = id, Building = building, Atom = PlaceAtoms.Provisions, Value = Fixed.FromDouble(value) });
        }

        void BuildKeeperOf(Tick t)
        {
            foreach (var kv in _residency.All)
                if (kv.Value != null && kv.Value.Role == ResidentRole.Keeper)
                    t.KeeperOf[kv.Value.BuildingIndex] = kv.Key;
        }

        EntityId KeeperOf(Tick t, int building)
            => t.KeeperOf.TryGetValue(building, out var keeper) ? keeper : EntityId.None;

        static List<EntityId> SortedKeys<T>(IEnumerable<KeyValuePair<EntityId, T>> src)
        {
            var dst = new List<EntityId>();
            foreach (var kv in src) dst.Add(kv.Key);
            dst.Sort((a, b) => a.Value.CompareTo(b.Value));
            return dst;
        }
    }
}
