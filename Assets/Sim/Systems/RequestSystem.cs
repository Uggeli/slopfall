using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The emergent-quest seed, v1: an agent whose problem can't self-serve
    /// asks another agent for help. Today the only problem is poverty (coin
    /// pinned near zero with no income) and the only currency of help is
    /// alms — but the shape (detect problem → choose who to ask from the
    /// social fabric → they decide from regard and means → both remember)
    /// is the request mechanism the design always called for. The player
    /// later becomes just another askable agent.
    ///
    /// Owns nothing: reads needs/coin/relations, emits CoinTransferEvent
    /// (EconomySystem applies) and RelationImpulseEvents (SocialSystem, the
    /// relations owner, applies gratitude/resentment and writes memories).
    public sealed class RequestSystem : ISystem
    {
        public const double PovertyThreshold = 0.8;     // CoinDef at which one swallows pride
        public const double AlmsAmount = 0.25;
        public const double GiverKeepsAtLeast = 0.4;    // won't give below this
        public const double FriendBar = 0.15;           // regard that says yes
        public const double KeeperCharityBar = -0.1;    // keepers tolerate strangers
        const double AskCooldownGameMinutes = 360;      // 6h between asks

        SimulationContext _ctx;
        readonly Dictionary<EntityId, double> _nextAskAtGameMinutes = new Dictionary<EntityId, double>();
        readonly List<EntityId> _askers = new List<EntityId>();
        double _gameMinutes;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double dt = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            if (dt <= 0) return;
            _gameMinutes += dt;

            // Collect this tick's askers, sorted for determinism (transfers
            // couple agents, so processing order matters).
            _askers.Clear();
            foreach (var kv in _ctx.Needs.All)
            {
                if (kv.Value.V[NeedAxis.CoinDef] < PovertyThreshold) continue;
                if (_nextAskAtGameMinutes.TryGetValue(kv.Key, out var at) && _gameMinutes < at) continue;
                _askers.Add(kv.Key);
            }
            if (_askers.Count == 0) return;
            _askers.Sort((a, b) => a.Value.CompareTo(b.Value));

            foreach (var asker in _askers)
            {
                _nextAskAtGameMinutes[asker] = _gameMinutes + AskCooldownGameMinutes;
                Ask(asker);
            }
        }

        void Ask(EntityId asker)
        {
            var target = PickTarget(asker, out double regardTowardAsker);
            if (target.IsNone) return;      // nobody worth asking — stay hungry, stay proud

            bool isKeeper = _ctx.Residency.TryGet(target, out var res) && res.Role == ResidentRole.Keeper;
            double bar = isKeeper ? KeeperCharityBar : FriendBar;
            bool grants = regardTowardAsker >= bar
                && _ctx.Coin.Get(target) - AlmsAmount >= GiverKeepsAtLeast;

            if (grants)
            {
                _ctx.Events.Emit(new CoinTransferEvent { From = target, To = asker, Amount = AlmsAmount });
                _ctx.Events.Emit(new HelpGrantedEvent { Asker = asker, Giver = target, Amount = AlmsAmount });
                _ctx.Events.Emit(new RelationImpulseEvent
                {
                    Who = asker, Other = target, RegardDelta = +0.3, FamiliarityDelta = +0.05,
                    Memory = MemoryKind.ReceivedHelp, RecordMemory = true,
                });
                _ctx.Events.Emit(new RelationImpulseEvent
                {
                    Who = target, Other = asker, RegardDelta = +0.05, FamiliarityDelta = +0.05,
                    Memory = MemoryKind.GaveHelp, RecordMemory = true,
                });
            }
            else
            {
                _ctx.Events.Emit(new HelpRefusedEvent { Asker = asker, Refuser = target });
                _ctx.Events.Emit(new RelationImpulseEvent
                {
                    Who = asker, Other = target, RegardDelta = -0.2, FamiliarityDelta = +0.02,
                    Memory = MemoryKind.WasRefused, RecordMemory = true,
                });
                _ctx.Events.Emit(new RelationImpulseEvent
                {
                    Who = target, Other = asker, RegardDelta = -0.02, FamiliarityDelta = +0.02,
                    Memory = MemoryKind.RefusedToHelp, RecordMemory = true,
                });
            }
        }

        /// Warmest wealthy contact first; falling back to the richest keeper
        /// in town (strangers can be asked — keepers half-expect it).
        EntityId PickTarget(EntityId asker, out double regardTowardAsker)
        {
            EntityId best = EntityId.None;
            double bestRegard = double.MinValue;

            if (_ctx.Relations.TryGet(asker, out var relations))
            {
                var sorted = new List<KeyValuePair<EntityId, RelationData>>(relations.Of);
                sorted.Sort((a, b) =>
                {
                    int cmp = b.Value.Regard.CompareTo(a.Value.Regard);
                    return cmp != 0 ? cmp : a.Key.Value.CompareTo(b.Key.Value);
                });
                foreach (var kv in sorted)
                {
                    if (_ctx.Coin.Get(kv.Key) - AlmsAmount < GiverKeepsAtLeast) continue;
                    best = kv.Key;
                    break;
                }
            }

            if (best.IsNone)
            {
                // Richest keeper in town; deterministic tie-break by id.
                double bestCoin = GiverKeepsAtLeast + AlmsAmount;
                var keepers = new List<KeyValuePair<EntityId, ResidencyData>>();
                foreach (var kv in _ctx.Residency.All)
                    if (kv.Value.Role == ResidentRole.Keeper) keepers.Add(kv);
                keepers.Sort((a, b) => a.Key.Value.CompareTo(b.Key.Value));
                foreach (var kv in keepers)
                {
                    double coin = _ctx.Coin.Get(kv.Key);
                    if (coin > bestCoin) { bestCoin = coin; best = kv.Key; }
                }
            }

            regardTowardAsker = 0;
            if (!best.IsNone && _ctx.Relations.TryGet(best, out var theirRelations)
                && theirRelations.Of.TryGetValue(asker, out var rel))
                regardTowardAsker = rel.Regard;
            return best;
        }
    }
}
