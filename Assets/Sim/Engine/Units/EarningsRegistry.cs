using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS home for the legacy EconomySystem._earnedToday — the one piece of the
    // economy that genuinely lives ACROSS ticks. A hand ACCRUES wage (LaborDailyWage
    // pro-rated by the workday) every tick it works, and that accrual is settled in a
    // single employer→worker transfer on payday. Coin can't carry it (the accrual
    // isn't money yet), and a SimSystem holds no tick state, so it owns its own
    // registry: EntityId → accrued wage.
    //
    // Both intents are DELTA/RESET-shaped so accumulation is order-free, mirroring the
    // coin/stock/larder registries:
    //   - EarningsDeltaIntent sums this tick's accruals into a hand's running total;
    //   - EarningsResetIntent zeroes one hand's total (emitted on payday once settled).
    // DespawnedEvent drops a dead hand's accrual. Reset is applied AFTER the deltas so
    // a same-tick accrue+reset (a hand that works the rollover tick then gets paid)
    // resolves to zero — the legacy cleared the whole map on payday, so resetting the
    // hands that were paid is equivalent.

    /// Intent: add Delta to a worker's accrued (unpaid) wage this tick. Summed.
    public struct EarningsDeltaIntent : IEvent { public EntityId Id; public double Delta; }

    /// Intent: zero a worker's accrued wage (emitted on payday once it's been settled).
    public struct EarningsResetIntent : IEvent { public EntityId Id; }

    public sealed class EarningsRegistry : Registry
    {
        readonly Dictionary<EntityId, double> _d = new Dictionary<EntityId, double>();

        public EarningsRegistry(EventBus events) : base(events) { }

        // --- read view (read phase) ---
        public double Get(EntityId id) => _d.TryGetValue(id, out var w) ? w : 0;
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, double>> All => _d;

        public override void Update(long tick)
        {
            // 1) Sum this tick's accruals (order-free; a hand may accrue several deltas).
            foreach (var i in Events.GetEvents<EarningsDeltaIntent>())
            {
                _d.TryGetValue(i.Id, out var acc);
                _d[i.Id] = acc + i.Delta;
            }

            // 2) Payday resets, applied after the deltas so a same-tick accrue+pay nets
            //    to zero (matches the legacy whole-map clear on payday).
            foreach (var r in Events.GetEvents<EarningsResetIntent>())
                _d.Remove(r.Id);

            // 3) A despawned hand's accrual goes with it.
            foreach (var d in Events.GetEvents<DespawnedEvent>())
                _d.Remove(d.Entity);
        }
    }
}
