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
        // Wages are a concrete DAILY payday (PayDay, on NewDaySimEvent), not an abstract
        // per-minute trickle: a hand accrues LaborDailyWage pro-rated by the fraction of a
        // WorkdayMinutes day it actually worked, settled in ONE transfer from its
        // employer's till. No per-tick wage rates, no cost-of-living bleed.
        public const double LaborDailyWage = 3.0;                   // a hand's full-day wage (~3 days' food at the money-arc scale)
        public const double WorkdayMinutes = 600.0;                 // a standard 10-hour day — the denominator for pro-rating a partial day
        // Supply (G2): a working keeper restocks toward StockTarget — craft is
        // free, imports cost the import price off-map. Rates/targets are
        // PLACEHOLDERS, tuned once the loop (G3 sales, G4 B2B) draws stock down.
        public const double StockTarget = 40.0;                     // units a working keeper keeps on hand
        public const double ImportPerMinute = 1.0;                  // off-map restock rate (units/game-min)
        // Stage 5: a farm's output scales with the hands working it this tick, capped
        // by the land it has — the production throttle that bounds the export faucet
        // without touching craft shops (a lone keeper is no runaway). PLACEHOLDERS
        // tuned against the soak (food self-sufficient, stable money, low poverty).
        public const double FarmProducePerWorkerMinute = 0.010;     // food per active farmhand (units/game-min) — keeper-independent (harvest pass); the tripling probe showed supply isn't the bottleneck, so back to a sane level
        public const int FarmCapacity = 50;                         // hands a settlement's farmland supports
        // In-kind subsistence (Subsistence slice): a farm/fishery hand takes home a
        // share of the harvest — provisions → their household larder — on top of the
        // small cash wage. Modelled as free extra yield (NOT drawn off the farm's
        // sale stock), so it doesn't disturb the sale/export flow; it's the hand's
        // own subsistence portion. PLACEHOLDER, tuned against the famine soak once
        // EatHome draws on the larder (sub-step 3). See docs/subsistence.md.
        public const double InKindProvisionsPerMinute = 0.01;       // provisions/game-min to a working food-producer's larder
        public const double ProducePerMinute = 0.5;                 // local craft output (units/game-min, a keeper working)
        // World market (export edge): a producer sells surplus off-map only while the
        // saturating price holds above this fraction of wholesale — below it the glut
        // isn't worth selling into, so it HOLDS stock instead of dumping (export isn't
        // automatic). Production pauses once a held warehouse fills, so a glutted
        // producer idles rather than piling up forever. PLACEHOLDERS, tuned vs the soak.
        public const double ExportFloorFraction = 0.5;             // reservation: hold below 0.5× wholesale
        public const double WarehouseCap = StockTarget * 3.0;       // stop producing once this full of unsold stock
        // No cost-of-living: money leaves a purse only by buying something or paying
        // someone (concrete transactions), never an abstract per-tick bleed.
        // Public sector (E3): a monthly progressive wealth tax drains purses above
        // an exemption into the treasury, which pays guards a steady salary. Tax is
        // the recirculation that counters concentration; guard pay puts it back into
        // circulation as spending. PLACEHOLDERS — tuned once G6's export edge lands.
        public const double TaxExemption = 10.0;                    // wealth below this is untaxed (protects the poor) — ×20 money-arc scale
        public const double TaxRatePerMonth = 0.3;                  // share of wealth above the exemption, per month
        public const double GuardDailyWage = 6.0;                   // guard's flat daily salary, paid from the treasury on payday (crown mints any shortfall)
        // The treasury's spend path: a flat civic dividend (poor relief) back to a
        // settlement's residents, so the progressive tax actually RECIRCULATES against
        // concentration instead of hoarding. A transfer (treasury → residents).
        public const double CivicDividendPerMinute = 0.0002;        // fraction of treasury paid out per game-minute

        SimulationContext _ctx;
        readonly List<CoinTransferEvent> _pending = new List<CoinTransferEvent>();
        readonly List<EscheatEvent> _escheats = new List<EscheatEvent>();
        readonly List<EntityId> _order = new List<EntityId>();      // reused per-tick deterministic walk order (F3)
        readonly Dictionary<int, int> _farmWorkers = new Dictionary<int, int>();             // hands working each farm this tick (scales its harvest)
        readonly Dictionary<EntityId, double> _earnedToday = new Dictionary<EntityId, double>();   // wage a hand has accrued by working today; settled on payday
        readonly List<EntityId> _payOrder = new List<EntityId>();   // reused deterministic payday order
        bool _payday;                                               // armed by NewDaySimEvent; settled next Update
        readonly Dictionary<int, EntityId> _keeperOf = new Dictionary<int, EntityId>();
        readonly Dictionary<Good, List<int>> _sourcesByGood = new Dictionary<Good, List<int>>();   // wholesale sources (G4)
        bool _taxDue;                                              // armed by NewMonthSimEvent; collected next Update

        // Cumulative audit totals; written to LedgerRegistry each tick. The coin
        // arithmetic below is unchanged — these only observe it. Minted/Sunk are
        // computed by conservation residual (Σ-before + minted − Σ-after) so the
        // tally stays exact whatever the cause of a sink.
        double _minted, _sunk, _exports, _crownSubsidy, _salesRevenue, _serviceRevenue, _alms, _imports, _wholesale, _taxes, _guardPay;
        // Goods-flow audit (units, per Good) — what the economy physically moved.
        readonly double[] _producedUnits = new double[GoodsCatalog.Count];
        readonly double[] _importedUnits = new double[GoodsCatalog.Count];
        readonly double[] _exportedUnits = new double[GoodsCatalog.Count];
        readonly double[] _consumedUnits = new double[GoodsCatalog.Count];

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<CoinTransferEvent>(e => _pending.Add(e));
            ctx.Events.Subscribe<EscheatEvent>(e => _escheats.Add(e));
            ctx.Events.Subscribe<NewMonthSimEvent>(e => _taxDue = true);   // tax man arrives (E3)
            ctx.Events.Subscribe<NewDaySimEvent>(e => { ctx.WorldMarket.ResetDay(); _payday = true; });   // external demand replenishes daily; wages settle on payday
        }

        public void ProcessEvents()
        {
            ProcessEscheats();
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

        /// A dead agent's purse passes to its settlement treasury (escheat, L2 —
        /// docs/living_world_L2_lifecycle.md). A within-supply transfer (purse →
        /// treasury, both counted by SumCoin), so it touches no faucet/sink tally
        /// and conservation holds; then the purse row is removed (EconomySystem is
        /// the sole writer of Coin). Runs before the transfer pass so that pass's
        /// before/after sink accounting is unaffected.
        void ProcessEscheats()
        {
            if (_escheats.Count == 0) return;
            for (int i = 0; i < _escheats.Count; i++)
            {
                var e = _escheats[i];
                double purse = _ctx.Coin.Get(e.Dead);
                if (purse > 0) _ctx.Treasury.Add(e.To, purse);
                _ctx.Coin.Remove(e.Dead);
            }
            _escheats.Clear();
        }

        long _tick;   // current tick, stashed so the larder-contact helpers can stamp the belief (memory write)

        public void Update(long tick)
        {
            _tick = tick;
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = clock.DeltaGameSeconds / 60.0;
            if (gameMinutes <= 0) return;

            double before = SumCoin();
            double salesRev = 0, serviceRev = 0, importsPaid = 0, wholesalePaid = 0, guardPaid = 0;
            double exportsEarned = 0, crownMinted = 0, crownShortfall = 0;

            // Tax man (E3): on a month rollover (NewMonthSimEvent), drain wealth
            // above the exemption from every purse → treasury (a conserved transfer;
            // the recirculation that counters keeper-ward concentration). The crown's
            // remittance is handled continuously below, not here.
            if (_taxDue) { CollectMonthlyTax(); _taxDue = false; }

            // Payday (NewDaySimEvent): wages move ONCE a day, in concrete transfers — each
            // hand paid what it earned working today (from its employer's till), guards a
            // flat salary from the treasury. Settled before the day's trade.
            if (_payday) { PayDay(ref guardPaid, ref crownShortfall); _payday = false; }

            // Count the hands working each farm this tick — its harvest scales with
            // them (capped by land), so a town's whole workforce makes a sane amount.
            _farmWorkers.Clear();
            foreach (var kvb in _ctx.Behavior.All)
            {
                var bh = kvb.Value;
                if (bh.Phase != ActivityPhase.Doing || bh.TargetBuilding < 0) continue;
                if (bh.Activity != ActivityKind.Farm && bh.Activity != ActivityKind.Fish
                    && bh.Activity != ActivityKind.Mine && bh.Activity != ActivityKind.Labor) continue;
                if (!_ctx.Buildings.TryGet(bh.TargetBuilding, out var wb) || wb == null || !GoodsCatalog.IsStaffedWorkplace(wb.Kind)) continue;
                _farmWorkers.TryGetValue(bh.TargetBuilding, out var c);
                _farmWorkers[bh.TargetBuilding] = c + 1;
            }

            // Harvest pass: a staffed site's output is produced off the HANDS working
            // it (counted above), NOT its keeper — so a field full of laborers feeds
            // the MARKET even when the keeper is asleep, breaking the empty-shelf
            // deadlock (no shop food → no Buy → no work → no food). Runs before the
            // trade loop so the day's output is on the shelf when stores source it B2B.
            foreach (var kvw in _farmWorkers)
                if (kvw.Value > 0 && _ctx.Buildings.TryGet(kvw.Key, out var wb) && wb != null)
                    ProduceAtWorkplace(kvw.Key, wb, kvw.Value, gameMinutes);

            // Deterministic walk: registry enumeration is unordered, and this pass
            // draws down SHARED shop stock (B2C sales, B2B restock), so when a shelf
            // is stock-constrained the outcome (who gets the last unit, who pays)
            // depends on order. Sort by EntityId — same discipline SocialSystem uses
            // for partner order. MANDATORY before Update runs in parallel / across
            // region servers (audit F3).
            SortedKeys(_ctx.Behavior.All, _order);
            for (int oi = 0; oi < _order.Count; oi++)
            {
                var id = _order[oi];
                if (!_ctx.Behavior.TryGet(id, out var behavior)) continue;
                double coin = _ctx.Coin.Get(id);
                double next = coin;   // no cost-of-living drain; wages arrive on payday, not per tick

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
                        case ActivityKind.Farm:
                        case ActivityKind.Fish:
                        case ActivityKind.Mine:
                        case ActivityKind.Labor:
                            // Working the employer's premises (fields, shore, diggings, loom):
                            // wages aren't paid by the tick — the hand ACCRUES what it earns
                            // (LaborDailyWage pro-rated by the workday) and is paid in one
                            // transfer on payday, capped then by the employer's till.
                            _earnedToday.TryGetValue(id, out var acc);
                            _earnedToday[id] = acc + LaborDailyWage * gameMinutes / WorkdayMinutes;
                            // In-kind subsistence: a food-producer (farm/fishery) hand
                            // also takes home provisions for the household larder. A
                            // miner produces ore (not edible) → cash wage only.
                            if (behavior.Activity == ActivityKind.Farm || behavior.Activity == ActivityKind.Fish)
                            {
                                int fh = HomeOf(id);
                                double inKind = InKindProvisionsPerMinute * gameMinutes;
                                _ctx.Larder.Add(fh, inKind);
                                _producedUnits[(int)Good.Provisions] += inKind;     // food the land produced, carried home
                                _ctx.PlaceMemory.Note(id, fh, PlaceFact.ProvisionsHere, 1, _tick);   // carried the harvest home → remembers it's stocked
                            }
                            break;
                        case ActivityKind.EatTavern:
                        case ActivityKind.Socialize:
                        case ActivityKind.Buy:
                            // A real B2C sale: draw the good off the seller's shelf,
                            // pay the keeper. No stock (or no coin) → no sale.
                            next -= PaySale(id, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), gameMinutes, next, ref salesRev);
                            break;
                        case ActivityKind.Visit:
                            // At a service institution the visit is paid patronage
                            // (offering/dues/fee → keeper); free elsewhere (G5).
                            next -= PaySale(id, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), gameMinutes, next, ref serviceRev);
                            break;
                        case ActivityKind.Steal:
                            // Theft (Subsistence slice): take provisions off the shelf
                            // into the thief's larder — no coin changes hands (the only
                            // "cost" is the conscience charge OddSystem reads at decision
                            // time). `next` (coin) is untouched.
                            StealProvisions(id, behavior.TargetBuilding,
                                ActivityCatalog.SpecFor(behavior.Activity), gameMinutes);
                            break;
                        case ActivityKind.EatHome:
                            // A home meal is no longer free (Subsistence): it eats
                            // provisions from the household larder. The draw clamps at
                            // zero (Larder.Add), and NeedsSystem gates the hunger relief
                            // on the same larder having food — so an empty larder yields
                            // no meal, mirroring the shop-stock sale gate. Coin untouched.
                            int eatHome = HomeOf(id);
                            double larderBefore = _ctx.Larder.Get(eatHome);
                            double afterMeal = _ctx.Larder.Add(eatHome,
                                -ActivityCatalog.EatHome.LarderUnitsPerMinute * gameMinutes);
                            _consumedUnits[(int)Good.Provisions] += larderBefore - afterMeal;   // provisions actually eaten (clamped at empty)
                            // Eating IS contact with the pantry — the agent remembers
                            // its state (a PLACES memory write): an empty result is the
                            // surprise that stops it coming back (ActionDiscovery recalls
                            // the fact and doesn't re-offer EatHome).
                            _ctx.PlaceMemory.Note(id, eatHome, PlaceFact.ProvisionsHere, afterMeal > 1e-6 ? 1 : 0, _tick);
                            break;
                    }
                }

                _ctx.Coin.Set(id, next < 0 ? 0 : next);
            }

            // Crown as lender of last resort (F1): each settlement's treasury — filled
            // by its own tax — funds its guards, and the crown mints only the shortfall
            // when a treasury runs dry. So tax actually recirculates (the treasury
            // depletes paying guards) instead of sitting idle, and the crown is a
            // backstop, not the primary money faucet.
            crownMinted = crownShortfall;

            // Recirculate the treasuries back to residents (poor relief), so the tax
            // counters concentration instead of hoarding.
            DistributeTreasury(gameMinutes);

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
                Produced = _producedUnits, Imported = _importedUnits,
                Exported = _exportedUnits, Consumed = _consumedUnits,
            });
        }

        double SumCoin()
        {
            double s = 0;
            foreach (var kv in _ctx.Coin.All) s += kv.Value;
            s += _ctx.Treasury.Total;          // treasury is part of the money supply (E3)
            return s;
            // NOTE: this sum is order-dependent at the float-epsilon level too; left
            // unsorted as it only feeds the audit residual. Sort here as well if/when
            // bit-exact two-run state hashing is required (Phase 2 / parallel). F3.
        }

        /// Snapshot a registry's keys into `dst`, sorted by EntityId — the stable
        /// walk order any contended/float-sum pass needs (audit F3). Reuses the
        /// caller's buffer; allocates only when the entity set grows.
        static void SortedKeys<T>(IEnumerable<KeyValuePair<EntityId, T>> src, List<EntityId> dst)
        {
            dst.Clear();
            foreach (var kv in src) dst.Add(kv.Key);
            dst.Sort((a, b) => a.Value.CompareTo(b.Value));
        }

        /// Recirculate each settlement's treasury back to its residents as a flat civic
        /// dividend (poor relief) — the spend path that makes the progressive tax counter
        /// concentration instead of letting the treasury hoard. A transfer (treasury →
        /// residents), so the money supply is unchanged. Residents walked in EntityId
        /// order for determinism (F3).
        void DistributeTreasury(double gameMinutes)
        {
            var settlements = _ctx.Settlements.All;
            for (int si = 0; si < settlements.Count; si++)
            {
                var s = settlements[si];
                if (s.Residents.Count == 0) continue;
                double bal = _ctx.Treasury.Get(s.Treasury);
                if (bal <= 0) continue;
                double per = bal * CivicDividendPerMinute * gameMinutes / s.Residents.Count;
                if (per <= 0) continue;

                _order.Clear();
                for (int i = 0; i < s.Residents.Count; i++) _order.Add(s.Residents[i]);
                _order.Sort((a, b) => a.Value.CompareTo(b.Value));

                double paid = 0;
                for (int oi = 0; oi < _order.Count; oi++)
                {
                    _ctx.Coin.Set(_order[oi], _ctx.Coin.Get(_order[oi]) + per);
                    paid += per;
                }
                _ctx.Treasury.Add(s.Treasury, -paid);
            }
        }

        /// Collect a progressive wealth tax PER SETTLEMENT: each settlement taxes its
        /// own residents into its own treasury — each purse pays its settlement's rate
        /// on whatever it holds above TaxExemption (the poor pay nothing). A pure
        /// transfer (the money supply is unchanged, it just moves to the local commons).
        /// Fired once per month rollover. The RATE scales with settlement kind: a city
        /// taxes hard (it has a public sector to fund), a hamlet barely at all.
        void CollectMonthlyTax()
        {
            var settlements = _ctx.Settlements.All;
            for (int si = 0; si < settlements.Count; si++)
            {
                var s = settlements[si];
                double rate = TaxRateFor(s.Kind);
                if (rate <= 0) continue;

                // Sort this settlement's residents so the collected total accumulates
                // in a deterministic order (float addition isn't associative — order
                // shifts the treasury balance at the epsilon level, breaking two-run /
                // multi-server agreement). F3.
                _order.Clear();
                for (int i = 0; i < s.Residents.Count; i++) _order.Add(s.Residents[i]);
                _order.Sort((a, b) => a.Value.CompareTo(b.Value));

                double collected = 0;
                for (int oi = 0; oi < _order.Count; oi++)
                {
                    var id = _order[oi];
                    double coin = _ctx.Coin.Get(id);
                    double taxable = coin - TaxExemption;
                    if (taxable <= 0) continue;
                    double tax = taxable * rate;
                    _ctx.Coin.Set(id, coin - tax);
                    collected += tax;
                }
                if (collected > 0)
                {
                    _ctx.Treasury.Add(s.Treasury, collected);
                    _taxes += collected;
                }
            }
        }

        /// Monthly wealth-tax rate by settlement kind — cities run a real public sector
        /// and tax hard; hamlets and villages are poor and barely tax; farms and
        /// standalone temples/taverns are lighter still. ("Hamlets' taxes must be
        /// lower." TaxRatePerMonth is the city anchor.)
        static double TaxRateFor(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.City:    return TaxRatePerMonth;          // 0.30
                case SettlementKind.Hamlet:  return TaxRatePerMonth * 0.5;    // 0.15
                case SettlementKind.Village: return TaxRatePerMonth * 0.33;   // 0.10
                default:                     return TaxRatePerMonth * 0.17;    // ~0.05 (farm/temple/tavern/other)
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

        /// Payday (NewDaySimEvent): the only time wages move. Each hand is paid what it
        /// ACCRUED by working today (LaborDailyWage pro-rated by the workday), drawn from
        /// its employer's till — capped, so a business with no revenue can't make full
        /// payroll (it underpays: the honest signal the trade isn't paying). Guards draw a
        /// flat daily salary from the treasury, the crown minting only the shortfall.
        /// Keepers take no wage — they live on their sales. Deterministic order (shared tills).
        void PayDay(ref double guardPaid, ref double crownShortfall)
        {
            // Hands: settle accrued wages from employers' tills.
            _payOrder.Clear();
            foreach (var kv in _earnedToday) if (kv.Value > 0) _payOrder.Add(kv.Key);
            _payOrder.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < _payOrder.Count; i++)
            {
                var w = _payOrder[i];
                double paid = PayWage(w, _earnedToday[w]);             // employer → worker, capped at the till
                if (paid > 0) _ctx.Coin.Set(w, _ctx.Coin.Get(w) + paid);
            }
            _earnedToday.Clear();

            // Guards: flat daily salary from the local treasury; crown mints the gap.
            _payOrder.Clear();
            foreach (var kv in _ctx.Employment.All)
                if (kv.Value != null && !kv.Value.PublicOwner.IsNone) _payOrder.Add(kv.Key);
            _payOrder.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < _payOrder.Count; i++)
            {
                var g = _payOrder[i];
                if (!_ctx.Employment.TryGet(g, out var emp) || emp.PublicOwner.IsNone) continue;
                double drawn = PayGuard(emp.PublicOwner, GuardDailyWage);   // treasury → guard, capped
                _ctx.Coin.Set(g, _ctx.Coin.Get(g) + GuardDailyWage);       // paid in full
                guardPaid += drawn;
                crownShortfall += GuardDailyWage - drawn;                  // crown mints only the gap (F1)
            }
        }

        /// Produce a staffed workplace's output from the HANDS working it this tick
        /// (farm/fishery food, pasture wool, weaver cloth) — keeper-INDEPENDENT, so a
        /// site with laborers supplies the market even when its keeper idles (the fix
        /// for the empty-shelf deadlock). Hands-scaled (capped by FarmCapacity),
        /// recipe-gated (a weaver spins only the wool it holds). Free yield from
        /// land + labour (no coin in); the keeper's RunBusiness then sells/exports it.
        void ProduceAtWorkplace(int building, BuildingRow b, int hands, double gameMinutes)
        {
            if (hands > FarmCapacity) hands = FarmCapacity;
            var produced = GoodsCatalog.Produces(b.Kind);
            var recipeInputs = GoodsCatalog.Inputs(b.Kind);
            for (int i = 0; i < produced.Length; i++)
            {
                var good = produced[i];
                if (_ctx.Stock.Get(building, good) >= WarehouseCap) continue;     // glut → idle
                double output = hands * FarmProducePerWorkerMinute * gameMinutes;
                for (int k = 0; k < recipeInputs.Length; k++)                      // recipe: 1 input per unit out
                {
                    double have = _ctx.Stock.Get(building, recipeInputs[k]);
                    if (have < output) output = have;
                }
                if (output <= 0) continue;
                for (int k = 0; k < recipeInputs.Length; k++)
                    _ctx.Stock.Add(building, recipeInputs[k], -output);
                _ctx.Stock.Add(building, good, output);
                _producedUnits[(int)good] += output;
            }
        }

        /// A working keeper stocks their (non-staffed, lone-keeper) business and trades
        /// at the edges for every business:
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
            var recipeInputs = GoodsCatalog.Inputs(b.Kind);
            for (int i = 0; i < produced.Length; i++)
            {
                var good = produced[i];
                // Produce — unless the warehouse is already full of unsold stock (a
                // glutted producer idles instead of piling up forever). A craft keeper
                // makes a steady amount alone; a farm makes food scaled by the hands
                // working it this tick, capped by its land.
                // Staffed workplaces (farm/fishery/pasture/weaver) produce off their
                // HANDS in the dedicated harvest pass (keeper-independent, runs each
                // tick); here only lone-keeper craft (e.g. a clothing store) produces.
                if (!GoodsCatalog.IsStaffedWorkplace(b.Kind) && _ctx.Stock.Get(building, good) < WarehouseCap)
                {
                    double output = ProducePerMinute * gameMinutes;
                    // A recipe is GATED on its inputs (docs/industry_layers.md): a weaver
                    // makes cloth only from the wool it holds (sourced B2B), 1 input per
                    // unit out. No inputs in stock → no output — the dependency that
                    // drives inter-industry (and, region-wide, inter-town) trade.
                    for (int k = 0; k < recipeInputs.Length; k++)
                    {
                        double have = _ctx.Stock.Get(building, recipeInputs[k]);
                        if (have < output) output = have;
                    }
                    if (output > 0)
                    {
                        for (int k = 0; k < recipeInputs.Length; k++)
                            _ctx.Stock.Add(building, recipeInputs[k], -output);    // consume the recipe inputs
                        _ctx.Stock.Add(building, good, output);                    // produced from land/craft (now gated by inputs)
                        _producedUnits[(int)good] += output;
                    }
                }

                // Export the surplus above local-sale stock — but only if the world
                // market still pays enough. Demand saturates (price falls past the
                // daily quota), and below the reservation the producer HOLDS rather than
                // dumps (export isn't automatic). Every settlement competes for the same
                // demand, so a glut cuts the price for all of them.
                double surplus = _ctx.Stock.Get(building, good) - StockTarget;
                if (surplus > 0)
                {
                    double wholesalePrice = GoodsCatalog.PriceOf(good, PriceTier.Wholesale);
                    double price = ExportPrice(good, wholesalePrice);
                    if (price >= wholesalePrice * ExportFloorFraction)
                    {
                        _ctx.Stock.Add(building, good, -surplus);                  // surplus leaves town
                        _ctx.WorldMarket.Sell(good, surplus);                      // glut the market → lower price for the next seller
                        exports += surplus * price;
                        _exportedUnits[(int)good] += surplus;
                    }
                    // else: hold the surplus (warehoused) and wait for demand to recover.
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
                double wholesalePrice = GoodsCatalog.PriceOf(needs[i], PriceTier.Wholesale)
                                      * GoodsCatalog.Scarcity(_ctx.Stock.Get(source, needs[i]));   // a glutted source sells cheap, a scarce one dear
                wholesale += Acquire(building, needs[i], ImportPerMinute * gameMinutes,
                    wholesalePrice, source, available - imports - wholesale);
            }
            return imports;
        }

        /// The off-map buying price for a good right now: full wholesale until the day's
        /// world demand quota is met, then falling as the glut grows (price = wholesale
        /// ÷ how many times over the quota we are). Shared across settlements, so the
        /// more everyone exports, the less each unit fetches.
        double ExportPrice(Good good, double wholesale)
        {
            double quota = GoodsCatalog.Def(good).WorldDemandPerDay;
            if (quota <= 0) return wholesale;
            double fill = _ctx.WorldMarket.BoughtOf(good) / quota;
            return fill <= 1.0 ? wholesale : wholesale / fill;
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
            if (sourceBuilding < 0 && price > 0) _importedUnits[(int)good] += units;   // bought off-map
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
            double price = spec.SalePrice;
            if (goods)
            {
                double available = _ctx.Stock.Get(building, good);
                price *= GoodsCatalog.Scarcity(available);                       // local price floats with the shelf: scarce → dear, glutted → cheap (the contextual signal)
                if (units > available) units = available;                        // no stock → no sale
            }
            double affordable = price > 0 ? patronCoin / price : units;
            if (units > affordable) units = affordable;                          // can't buy what you can't pay for
            if (units <= 0) return 0;

            double bill = units * price;
            if (goods)
            {
                _ctx.Stock.Add(building, good, -units);                          // off the shelf (services have none)
                if (spec.Kind != ActivityKind.Buy) _consumedUnits[(int)good] += units;   // eaten on the spot (Buy → larder, counted at EatHome)
            }

            // Shopping for the larder: provisions bought (Buy) go home to the
            // household's food store (the larder), to be eaten later via EatHome.
            // Other sale goods (a tavern meal, drink, a craftsman's wares) are
            // consumed on the spot, not larded.
            if (goods && spec.Kind == ActivityKind.Buy && good == Good.Provisions)
            {
                int ph = HomeOf(patron);
                _ctx.Larder.Add(ph, units);
                _ctx.PlaceMemory.Note(patron, ph, PlaceFact.ProvisionsHere, 1, _tick);   // carried provisions home → remembers it's stocked
            }

            var keeper = KeeperOf(building);
            if (!keeper.IsNone && keeper != patron)
            {
                _ctx.Coin.Set(keeper, _ctx.Coin.Get(keeper) + bill);
                revenue += bill;
            }
            return bill;
        }

        /// The home building whose larder this agent stocks/eats from — its
        /// residency building (a home's residents share one larder). −1 if none.
        int HomeOf(EntityId id)
            => _ctx.Residency.TryGet(id, out var r) && r != null ? r.BuildingIndex : -1;

        /// Take provisions off a building's shelf into the thief's home larder — the
        /// free, coinless counterpart to PaySale's Buy. The crime's only "cost" is the
        /// conscience charge OddSystem reads when choosing it; here it's a pure stock →
        /// larder move (no coin, no revenue). Bounded by the shelf's provisions —
        /// nothing to take at a shop with none. Stock is single-writer, so the draw is
        /// safe inside the EntityId-ordered walk.
        void StealProvisions(EntityId thief, int building, ActivityCatalog.Spec spec, double gameMinutes)
        {
            if (spec == null || spec.SaleUnits <= 0 || spec.DurationMinutes <= 0) return;
            if (!_ctx.Buildings.TryGet(building, out var b) || b == null) return;
            double available = _ctx.Stock.Get(building, Good.Provisions);
            if (available <= 0) return;
            double units = spec.SaleUnits / spec.DurationMinutes * gameMinutes;     // this tick's share
            if (units > available) units = available;
            if (units <= 0) return;
            _ctx.Stock.Add(building, Good.Provisions, -units);
            // The loot does NOT vanish into a fungible larder: it becomes discrete
            // CARRIED loaves the thief holds (ItemSystem mints them; the keeper stays
            // the owner — held ≠ owned is the theft). The GoodsDef relief is still the
            // spec's Δ via NeedsSystem; only where the goods physically go has changed.
            _ctx.Events.Emit(new ProvisionsTakenEvent
            {
                Taker = thief, Owner = KeeperOf(building), Building = building, Units = units,
            });
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
