using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Sole writer of CoinRegistry. Coin moves, mostly conserved:
    ///   - tavern bills (EatTavern/Socialize) transfer patron → that
    ///     tavern's keeper at the rates the activity catalog promises;
    ///   - CoinTransferEvents (alms from RequestSystem) move coin directly;
    ///   - non-tavern keepers earn a wage while working (explicit faucet —
    ///     their customers aren't simulated yet);
    ///   - everyone pays a small cost of living (explicit sink).
    /// The CoinDef need axis is derived from coin by NeedsSystem, so scoring
    /// keeps working unchanged on top of real money.
    public sealed class EconomySystem : ISystem
    {
        public const double WagePerWorkMinute = 0.3 / 180.0;        // matches Work's promise
        public const double TavernMealPerMinute = 0.08 / 45.0;      // matches EatTavern
        public const double TavernSocialPerMinute = 0.04 / 90.0;    // matches Socialize
        // Sized against the wage faucet (~13 keepers × ~5 work-hours/day):
        // town-wide sink ≈ town-wide income, so the economy neither bleeds
        // out nor inflates. Revisit when residents get income of their own.
        public const double CostOfLivingPerHour = 0.001;

        SimulationContext _ctx;
        readonly List<CoinTransferEvent> _pending = new List<CoinTransferEvent>();
        readonly Dictionary<int, EntityId> _keeperOf = new Dictionary<int, EntityId>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<CoinTransferEvent>(e => _pending.Add(e));
        }

        public void ProcessEvents()
        {
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
                }
                if (!t.To.IsNone)
                    _ctx.Coin.Set(t.To, _ctx.Coin.Get(t.To) + amount);
            }
            _pending.Clear();
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            if (gameMinutes <= 0) return;

            double living = CostOfLivingPerHour * gameMinutes / 60.0;

            foreach (var kv in _ctx.Behavior.All)
            {
                var behavior = kv.Value;
                double coin = _ctx.Coin.Get(kv.Key);
                double next = coin - living;

                if (behavior.Phase == ActivityPhase.Doing)
                {
                    switch (behavior.Activity)
                    {
                        case ActivityKind.Work:
                            next += WagePerWorkMinute * gameMinutes;
                            break;
                        case ActivityKind.EatTavern:
                            next -= PayTavern(kv.Key, behavior.TargetBuilding, TavernMealPerMinute * gameMinutes, coin);
                            break;
                        case ActivityKind.Socialize:
                            next -= PayTavern(kv.Key, behavior.TargetBuilding, TavernSocialPerMinute * gameMinutes, coin);
                            break;
                    }
                }

                _ctx.Coin.Set(kv.Key, next < 0 ? 0 : next);
            }
        }

        /// Bills the patron up to what they can pay; the keeper's share is
        /// credited immediately (still inside this system's Update — single
        /// writer holds).
        double PayTavern(EntityId patron, int building, double bill, double patronCoin)
        {
            if (bill > patronCoin) bill = patronCoin;   // drinking on an empty purse is free... for now
            if (bill <= 0) return 0;

            var keeper = KeeperOf(building);
            if (!keeper.IsNone && keeper != patron)
                _ctx.Coin.Set(keeper, _ctx.Coin.Get(keeper) + bill);
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
