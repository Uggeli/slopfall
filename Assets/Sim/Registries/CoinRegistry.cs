using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Actual money, normalized (1.0 = comfortable). Unlike the CoinDef need
    /// axis — which is now DERIVED from this (CoinDef = 1 − Coin) — coin is a
    /// conserved quantity that moves between entities: tavern bills flow to
    /// the tavern keeper, alms flow from giver to beggar. Faucets and sinks
    /// are explicit in EconomySystem (non-tavern wages in, cost of living
    /// out). Sole writer: EconomySystem after TownLoader seeds it.
    public sealed class CoinRegistry
    {
        readonly ConcurrentDictionary<EntityId, double> _d = new ConcurrentDictionary<EntityId, double>();

        public void Set(EntityId id, double coin) => _d[id] = coin < 0 ? 0 : coin;
        public double Get(EntityId id) => _d.TryGetValue(id, out var c) ? c : 0;
        public bool TryGet(EntityId id, out double coin) => _d.TryGetValue(id, out coin);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, double>> All => _d;
    }
}
