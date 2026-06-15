using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Sole writer of CoinRegistry and (after TownLoader seeds it) StockRegistry.
    /// Coin moves, mostly conserved:
    ///   - B2C sales (EatTavern/Socialize/Buy) draw a real good off the seller's
    ///     shelf and transfer the patron's coin → that keeper (no stock or no coin
    ///     → no sale); NeedsSystem applies the same stock gate to the relief;
    ///   - services (a Visit to a temple/guild/bank) are stockless patronage —
    ///     an offering/dues/fee → that keeper (G5);
    ///   - CoinTransferEvents (alms from RequestSystem) move coin directly;
    ///   - working keepers stock their business and trade at the off-map edges:
    ///     craft shops PRODUCE wares (no coin) and EXPORT the surplus (coin IN —
    ///     the productive faucet, G6); stores IMPORT staples (coin out — the sink);
    ///     businesses restock B2B from each other (G4 transfer);
    ///   - a monthly progressive tax drains wealth above an exemption into the
    ///     Town treasury, which pays guards a salary (E3 — both transfers; the
    ///     treasury is part of the money supply);
    ///   - everyone pays a small cost of living (explicit sink).
    /// The CoinDef need axis is derived from coin by NeedsSystem, so scoring
    /// keeps working unchanged on top of real money.
    public sealed class EconomySystem : ISystem
    {
        public const double LaborWagePerMinute = 0.15 / 120.0;      // matches Labor's promise (resident day-work)
        // Supply (G2): a working keeper restocks toward StockTarget — craft is
        // free, imports cost the import price off-map. Rates/targets are
        // PLACEHOLDERS, tuned once the loop (G3 sales, G4 B2B) draws stock down.
        public const double StockTarget = 40.0;                     // units a working keeper keeps on hand
        public const double ImportPerMinute = 1.0;                  // off-map restock rate (units/game-min)
        public const double ProducePerMinute = 0.5;                 // local craft output (units/game-min)
        // Necessity drain (placeholder). Real balance comes from the full
        // circular flow — employer-paid wages (E1) + rent (E2) + tax (E3) — and
        // gets tuned once all mechanisms are in, not before.
        public const double CostOfLivingPerHour = 0.001;
        // Public sector (E3): a monthly progressive wealth tax drains purses above
        // an exemption into the treasury, which pays guards a steady salary. Tax is
        // the recirculation that counters concentration; guard pay puts it back into
        // circulation as spending. PLACEHOLDERS — tuned once G6's export edge lands.
        public const double TaxExemption = 0.5;                     // wealth below this is untaxed (protects the poor)
        public const double TaxRatePerMonth = 0.3;                  // share of wealth above the exemption, per month
        public const double GuardWagePerMinute = 0.3 / 1440.0;      // guard salary ≈ 0.3 coin/day, from the treasury

        SimulationContext _ctx;
        readonly List<CoinTransferEvent> _pending = new List<CoinTransferEvent>();
        readonly Dictionary<int, EntityId> _keeperOf = new Dictionary<int, EntityId>();
        readonly Dictionary<Good, List<int>> _sourcesByGood = new Dictionary<Good, List<int>>();   // wholesale sources (G4)
        bool _taxDue;                                              // armed by NewMonthSimEvent; collected next Update

        // Cumulative audit totals; written to LedgerRegistry each tick. The coin
        // arithmetic below is unchanged — these only observe it. Minted/Sunk are
        // computed by conservation residual (Σ-before + minted − Σ-after) so the
        // tally stays exact whatever the cause of a sink.
        double _minted, _sunk, _exports, _crownSubsidy, _salesRevenue, _serviceRevenue, _alms, _imports, _wholesale, _taxes, _guardPay;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<CoinTransferEvent>(e => _pending.Add(e));
            ctx.Events.Subscribe<NewMonthSimEvent>(e => _taxDue = true);   // tax man arrives (E3)
        }

        public void ProcessEvents()
        {
            if (_pending.Count == 0) return;

            double before = SumCoin();
            double minted = 0, almsMoved = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                var t = _pending[i];
                if (t.Amount <= 0) continue;
                double amount = t.Amount;
                if (!t.From.IsNone)
                {
                    double have = _ctx.Coin.Get(t.From);
                    if (have <= 0) continue;
                    if (amount > have) amount = have;   // can't give what you don't have
                    _ctx.Coin.Set(t.From, have - amount);
                    if (!t.To.IsNone) almsMoved += amount;  // entity → entity transfer
                }
                else if (!t.To.IsNone)
                    minted += amount;                       // From=None: minted from outside

                if (!t.To.IsNone)
                    _ctx.Coin.Set(t.To, _ctx.Coin.Get(t.To) + amount);
            }
            _pending.Clear();

            _minted += minted;
            _sunk += before + minted - SumCoin();           // To=None burns land here
            _alms += almsMoved;
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            if (gameMinutes <= 0) return;

            double before = SumCoin();
            double salesRev = 0, serviceRev = 0, importsPaid = 0, wholesalePaid = 0, guardPaid = 0;
            double exportsEarned = 0, crownMinted = 0;
            double living = CostOfLivingPerHour * gameMinutes / 60.0;

            // Tax man (E3): on a month rollover (NewMonthSimEvent), drain wealth
            // above the exemption from every purse → treasury (a conserved transfer;
            // the recirculation that counters keeper-ward concentration). The crown's
            // remittance is handled continuously below, not here.
            if (_taxDue) { CollectMonthlyTax(); _taxDue = false; }

            foreach (var kv in _ctx.Behavior.All)
            {
                var behavior = kv.Value;
                double coin = _ctx.Coin.Get(kv.Key);
                double next = coin - living;

                // Guards are on the public payroll: a steady salary out of the
                // treasury (no keeper), recirculating tax back into spending.
                if (_ctx.Employment.TryGet(kv.Key, out var emp) && !emp.PublicOwner.IsNone)
                {
                    double drawn = PayGuard(emp.PublicOwner, GuardWagePerMinute * gameMinutes);
                    next += drawn;
                    guardPaid += drawn;
                }

                if (behavior.Phase == ActivityPhase.Doing)
                {
                    switch (behavior.Activity)
                    {
                        case ActivityKind.Work:
                            // Working the business stocks it AND trades at the edges:
                            //   - craft shops PRODUCE wares (free) and EXPORT the
                            //     surplus off-map (coin IN — the productive faucet, G6);
                            //   - stores IMPORT staples off-map (coin out, G2 sink);
                            //   - businesses buy what they resell from another business
                            //     wholesale (B2B transfer, G4).
                            // The old minted keeper wage is GONE (G6): keepers now live
                            // on real customer revenue + exports, the crown funds guards.
                            double importCost = RunBusiness(behavior.TargetBuilding, gameMinutes, next,
                                out double wholesaleCost, out double exported);
                            next -= importCost + wholesaleCost;
                            next += exported;
                            importsPaid += importCost;
                            wholesalePaid += wholesaleCost;
                            exportsEarned += exported;
                            break;
                        case ActivityKind.Labor:
                            // Paid by the employer (a transfer, not minted) — so
                            // resident income recirculates instead of inflating.
                            next += PayWage(kv.Key, LaborWagePerMinute * gameMinutes);
                            break;
                        case ActivityKind.EatTavern:
                        case ActivityKind.Socialize:
                        case ActivityKind.Buy:
                            // A real B2C sale: draw the good off the seller's shelf,
                            // pay the keeper. No stock (or no coin) → no sale.
                            next -= PaySale(kv.Key, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), gameMinutes, next, ref salesRev);
                            break;
                        case ActivityKind.Visit:
                            // At a service institution the visit is paid patronage
                            // (offering/dues/fee → keeper); free elsewhere (G5).
                            next -= PaySale(kv.Key, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), gameMinutes, next, ref serviceRev);
                            break;
                    }
                }

                _ctx.Coin.Set(kv.Key, next < 0 ? 0 : next);
            }

            // Crown remittance (G6): the province reimburses the treasury for the
            // guards' pay this tick — a smooth coin-IN faucet that keeps the public
            // payroll solvent ("palace/guards are crown-funded"). Paid continuously
            // (not a monthly lump) so the money supply doesn't saw-tooth.
            crownMinted = guardPaid;
            if (crownMinted > 0) _ctx.Treasury.Add(OwnerId.Town, crownMinted);

            // Conservation accounting. Money is now created only at the off-map
            // edges — exports (coin in to producers) and the crown subsidy (coin in
            // to the treasury); everything destroyed (cost of living, imports off-map,
            // bills to no keeper) is whatever didn't survive: before + minted − after.
            double minted = exportsEarned + crownMinted;
            _minted += minted;
            _sunk += before + minted - SumCoin();      // imports (off-map) land here too
            _exports += exportsEarned;
            _crownSubsidy += crownMinted;
            _salesRevenue += salesRev;
            _serviceRevenue += serviceRev;             // institution offerings/dues/fees (transfer)
            _imports += importsPaid;
            _wholesale += wholesalePaid;               // B2B is a transfer (stays in town)
            _guardPay += guardPaid;                    // treasury → guards (transfer)

            _ctx.Ledger.Set(new LedgerData
            {
                Minted = _minted, Sunk = _sunk, Exports = _exports, CrownSubsidy = _crownSubsidy,
                SalesRevenue = _salesRevenue, ServiceRevenue = _serviceRevenue,
                Alms = _alms, Imports = _imports, Wholesale = _wholesale,
                Taxes = _taxes, GuardPay = _guardPay,
            });
        }

        double SumCoin()
        {
            double s = 0;
            foreach (var kv in _ctx.Coin.All) s += kv.Value;
            s += _ctx.Treasury.Total;          // treasury is part of the money supply (E3)
            return s;
        }

        /// Collect a progressive wealth tax into the Town treasury: each purse pays
        /// TaxRatePerMonth of whatever it holds above TaxExemption (the poor pay
        /// nothing). A pure transfer — the money supply is unchanged, it just moves
        /// from private hands to the commons. Fired once per month rollover.
        void CollectMonthlyTax()
        {
            double collected = 0;
            foreach (var kv in _ctx.Coin.All)
            {
                double coin = kv.Value;
                double taxable = coin - TaxExemption;
                if (taxable <= 0) continue;
                double tax = taxable * TaxRatePerMonth;
                _ctx.Coin.Set(kv.Key, coin - tax);
                collected += tax;
            }
            if (collected > 0)
            {
                _ctx.Treasury.Add(OwnerId.Town, collected);
                _taxes += collected;
            }
        }

        /// Pay a guard's salary out of the treasury, capped at the balance (an empty
        /// treasury can't make payroll). A transfer — treasury → guard. Returns paid.
        double PayGuard(OwnerId owner, double wage)
        {
            double have = _ctx.Treasury.Get(owner);
            if (have <= 0) return 0;
            if (wage > have) wage = have;
            _ctx.Treasury.Add(owner, -wage);
            return wage;
        }

        /// Pay a worker's wage out of their employer's purse — capped at what the
        /// employer can afford (no revenue → no wage). A transfer, so it never
        /// mints coin; it drains the business sector that resident spending fills.
        double PayWage(EntityId worker, double wage)
        {
            if (!_ctx.Employment.TryGet(worker, out var emp) || emp.Employer.IsNone) return 0;
            double have = _ctx.Coin.Get(emp.Employer);
            if (have <= 0) return 0;
            if (wage > have) wage = have;
            _ctx.Coin.Set(emp.Employer, have - wage);
            return wage;
        }

        /// A working keeper stocks their business and trades at the edges:
        ///   - PRODUCE: craft shops make wares locally (no coin), then EXPORT the
        ///     surplus above StockTarget off-map at the wholesale price (coin IN —
        ///     the productive faucet, G6), reported via `exports`;
        ///   - IMPORT: stores bring staples in off-map, paying the import price out
        ///     of town (a sink — the loop's one controlled leak);
        ///   - B2B: buy what they resell but don't source from a fellow business
        ///     wholesale (a transfer — coin stays in town), reported via `wholesale`.
        /// Coin-out paths capped at the keeper's purse. Returns off-map import spend.
        double RunBusiness(int building, double gameMinutes, double available, out double wholesale, out double exports)
        {
            wholesale = 0;
            exports = 0;
            if (!_ctx.Buildings.TryGet(building, out var b) || b == null) return 0;

            var produced = GoodsCatalog.Produces(b.Kind);
            for (int i = 0; i < produced.Length; i++)
            {
                var good = produced[i];
                _ctx.Stock.Add(building, good, ProducePerMinute * gameMinutes);    // craft (free, builds surplus)
                double surplus = _ctx.Stock.Get(building, good) - StockTarget;     // keep StockTarget for local sale
                if (surplus > 0)
                {
                    _ctx.Stock.Add(building, good, -surplus);                      // surplus leaves town
                    exports += surplus * GoodsCatalog.PriceOf(good, PriceTier.Wholesale);   // off-map pays wholesale
                }
            }

            double imports = 0;
            var imported = GoodsCatalog.Imports(b.Kind);
            for (int i = 0; i < imported.Length; i++)
                imports += Acquire(building, imported[i], ImportPerMinute * gameMinutes,
                    GoodsCatalog.PriceOf(imported[i], PriceTier.Import), -1, available - imports);

            var needs = GoodsCatalog.B2BNeeds(b.Kind);
            for (int i = 0; i < needs.Length; i++)
            {
                int source = NearestSource(needs[i], building, b.X, b.Z);
                if (source < 0) continue;
                wholesale += Acquire(building, needs[i], ImportPerMinute * gameMinutes,
                    GoodsCatalog.PriceOf(needs[i], PriceTier.Wholesale), source, available - imports - wholesale);
            }
            return imports;
        }

        /// Move up to `units` of a good onto a building's shelf without overshooting
        /// StockTarget, paying `price`/unit within `budget`. Where it comes from:
        ///   sourceBuilding < 0, price 0 → produced from nothing (craft);
        ///   sourceBuilding < 0, price > 0 → imported off-map (coin leaves town);
        ///   sourceBuilding ≥ 0 → bought from that business (its stock −, its keeper
        ///     paid — an in-town transfer), bounded by what it has.
        /// Returns coin spent (0 for produce).
        double Acquire(int dst, Good good, double units, double price, int sourceBuilding, double budget)
        {
            double room = StockTarget - _ctx.Stock.Get(dst, good);
            if (room <= 0) return 0;
            if (units > room) units = room;

            if (sourceBuilding >= 0)
            {
                double srcStock = _ctx.Stock.Get(sourceBuilding, good);
                if (units > srcStock) units = srcStock;       // can't buy more than they stock
            }

            double cost = units * price;
            if (price > 0)
            {
                if (budget <= 0) return 0;
                if (cost > budget) { cost = budget; units = cost / price; }
            }
            if (units <= 0) return 0;

            _ctx.Stock.Add(dst, good, units);
            if (sourceBuilding >= 0)
            {
                _ctx.Stock.Add(sourceBuilding, good, -units);
                var seller = KeeperOf(sourceBuilding);
                if (!seller.IsNone) _ctx.Coin.Set(seller, _ctx.Coin.Get(seller) + cost);
            }
            return price > 0 ? cost : 0;
        }

        /// The closest business that originates `good` (imports/produces it) and has
        /// some in stock, other than the buyer — where a B2B restock sources from.
        /// −1 if none can supply right now (shelves stay low until they restock).
        int NearestSource(Good good, int buyer, float bx, float bz)
        {
            if (!_sourcesByGood.TryGetValue(good, out var sources))
            {
                sources = new List<int>();
                foreach (var kv in _ctx.Buildings.All)
                    if (kv.Value != null && GoodsCatalog.Originates(kv.Value.Kind, good))
                        sources.Add(kv.Key);
                _sourcesByGood[good] = sources;
            }

            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < sources.Count; i++)
            {
                int s = sources[i];
                if (s == buyer || _ctx.Stock.Get(s, good) <= 0) continue;
                if (!_ctx.Buildings.TryGet(s, out var sb) || sb == null) continue;
                double dx = sb.X - bx, dz = sb.Z - bz;
                double d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        /// A B2C transaction this tick: the patron pays their per-minute share to
        /// the keeper. Two flavours, by what the activity sells here:
        ///   - GOODS (meal/drink/wares): drawn off the seller's shelf, so it's
        ///     bounded by stock (no goods → no sale; NeedsSystem mirrors the gate,
        ///     so an empty shelf yields no relief either);
        ///   - SERVICE (temple/guild/bank visit, G5): stockless patronage — bounded
        ///     only by the rate and what the patron can afford.
        /// Returns coin the patron paid.
        double PaySale(EntityId patron, int building, ActivityCatalog.Spec spec, double gameMinutes, double patronCoin, ref double revenue)
        {
            if (spec == null || spec.SaleUnits <= 0 || spec.DurationMinutes <= 0) return 0;
            if (patronCoin <= 0) return 0;
            if (!_ctx.Buildings.TryGet(building, out var b) || b == null) return 0;

            bool goods = GoodsCatalog.SaleGoodFor(b.Kind, spec.Kind, out var good);
            bool service = !goods && GoodsCatalog.IsPaidService(b.Kind, spec.Kind);
            if (!goods && !service) return 0;                                    // nothing for sale here

            double units = spec.SaleUnits / spec.DurationMinutes * gameMinutes;  // this tick's share
            if (goods)
            {
                double available = _ctx.Stock.Get(building, good);
                if (units > available) units = available;                        // no stock → no sale
            }
            double price = spec.SalePrice;
            double affordable = price > 0 ? patronCoin / price : units;
            if (units > affordable) units = affordable;                          // can't buy what you can't pay for
            if (units <= 0) return 0;

            double bill = units * price;
            if (goods) _ctx.Stock.Add(building, good, -units);                   // off the shelf (services have none)

            var keeper = KeeperOf(building);
            if (!keeper.IsNone && keeper != patron)
            {
                _ctx.Coin.Set(keeper, _ctx.Coin.Get(keeper) + bill);
                revenue += bill;
            }
            return bill;
        }

        EntityId KeeperOf(int building)
        {
            // Residency is immutable after town load — build the map once.
            if (_keeperOf.Count == 0)
            {
                foreach (var kv in _ctx.Residency.All)
                    if (kv.Value.Role == ResidentRole.Keeper)
                        _keeperOf[kv.Value.BuildingIndex] = kv.Key;
            }
            return _keeperOf.TryGetValue(building, out var keeper) ? keeper : EntityId.None;
        }
    }
}
