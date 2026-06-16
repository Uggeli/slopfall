using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The emergent-quest seed, v1: an agent whose problem can't self-serve asks
    /// another for help. Today the only problem is poverty and the only help is
    /// alms. The poor pick the Beg ACTIVITY from the marketplace (its value comes
    /// from the coinDef gap — no bespoke trigger) and sit at a public venue; this
    /// system runs the asking: while an agent is Doing Beg it asks an affordable
    /// passer-by it senses (SensedRegistry), who decides from regard and means,
    /// and both remember. The shape (problem → ask → decide → remember) is the
    /// request mechanism; the player later is just another askable agent.
    ///
    /// Owns nothing: reads behavior/senses/coin/relations, emits CoinTransferEvent
    /// (EconomySystem applies) and RelationImpulseEvents (SocialSystem, the
    /// relations owner, applies gratitude/resentment and writes the memories).
    ///
    /// L4 (docs/living_world_L4_directed_drives.md) replaced the old walk-to-a-
    /// chosen-mark journey with this sit-and-solicit model — discovery folded into
    /// the marketplace (Beg), the directed action into the senses.
    public sealed class RequestSystem : ISystem
    {
        public const double AlmsAmount = 0.25;
        public const double GiverKeepsAtLeast = 0.4;    // won't give below this
        public const double FriendBar = 0.15;           // regard that says yes
        public const double KeeperCharityBar = -0.1;    // keepers tolerate strangers
        const double AskCooldownGameMinutes = 360;      // don't re-ask the same passer-by for 6h
        const double AskCadenceGameMinutes = 20;        // a beggar tries at most one passer-by per ~20 game-min
        const int CooldownPruneCap = 4096;              // bound the per-pair cooldown map

        SimulationContext _ctx;
        readonly List<EntityId> _beggars = new List<EntityId>();
        readonly Dictionary<EntityId, double> _nextAskAt = new Dictionary<EntityId, double>();  // per-beggar cadence
        readonly Dictionary<long, double> _pairCooldown = new Dictionary<long, double>();        // per (beggar, mark)
        readonly List<long> _stale = new List<long>();
        readonly Dictionary<int, EntityId> _venueKeeper = new Dictionary<int, EntityId>();       // building → its keeper
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

            if (_pairCooldown.Count > CooldownPruneCap) PruneCooldowns();

            // Everyone currently begging, in id order — transfers couple agents,
            // so a deterministic processing order matters.
            _beggars.Clear();
            foreach (var kv in _ctx.Behavior.All)
            {
                var b = kv.Value;
                if (b.Phase == ActivityPhase.Doing && b.Activity == ActivityKind.Beg) _beggars.Add(kv.Key);
            }
            if (_beggars.Count == 0) return;
            _beggars.Sort((a, b) => a.Value.CompareTo(b.Value));

            // Map each venue to its (lowest-id) keeper once — the alms-giver at
            // the temple/shop door, who is Working indoors and so isn't "sensed"
            // as a passer-by. This is what makes begging actually work.
            _venueKeeper.Clear();
            foreach (var kv in _ctx.Residency.All)
            {
                if (kv.Value.Role != ResidentRole.Keeper) continue;
                int b = kv.Value.BuildingIndex;
                if (!_venueKeeper.TryGetValue(b, out var cur) || kv.Key.Value < cur.Value)
                    _venueKeeper[b] = kv.Key;
            }

            for (int i = 0; i < _beggars.Count; i++)
            {
                var beggar = _beggars[i];
                if (_nextAskAt.TryGetValue(beggar, out var at) && _gameMinutes < at) continue;  // between asks

                int venue = _ctx.Behavior.TryGet(beggar, out var bb) ? bb.TargetBuilding : -1;
                var mark = PickMark(beggar, venue);
                if (mark.IsNone) continue;                 // nobody worth asking nearby

                _nextAskAt[beggar] = _gameMinutes + AskCadenceGameMinutes;
                _pairCooldown[PairKey(beggar, mark)] = _gameMinutes + AskCooldownGameMinutes;
                Resolve(beggar, mark);
            }
        }

        /// The lowest-id affordable mark the beggar can ask, deterministically:
        /// an affordable passer-by it senses, or the keeper of the venue it begs
        /// at. "Affordable" = has coin to spare, so the destitute aren't asked
        /// (and so soured) for nothing.
        EntityId PickMark(EntityId beggar, int venue)
        {
            EntityId best = EntityId.None;
            var sensed = _ctx.Sensed.Of(beggar);
            for (int i = 0; i < sensed.Count; i++)
                Consider(beggar, sensed[i], ref best);
            if (venue >= 0 && _venueKeeper.TryGetValue(venue, out var keeper))
                Consider(beggar, keeper, ref best);
            return best;
        }

        void Consider(EntityId beggar, EntityId other, ref EntityId best)
        {
            if (other == beggar || other.IsNone) return;
            if (_ctx.Coin.Get(other) - AlmsAmount < GiverKeepsAtLeast) return;
            if (_pairCooldown.TryGetValue(PairKey(beggar, other), out var until) && _gameMinutes < until) return;
            if (best.IsNone || other.Value < best.Value) best = other;
        }

        void PruneCooldowns()
        {
            _stale.Clear();
            foreach (var kv in _pairCooldown)
                if (_gameMinutes >= kv.Value) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++) _pairCooldown.Remove(_stale[i]);
        }

        static long PairKey(EntityId a, EntityId b) => ((long)a.Value << 32) | (uint)b.Value;

        void Resolve(EntityId asker, EntityId target)
        {
            double regardTowardAsker = 0;
            if (_ctx.Relations.TryGet(target, out var theirRelations)
                && theirRelations.Of.TryGetValue(asker, out var rel))
                regardTowardAsker = rel.Regard;

            bool isKeeper = _ctx.Residency.TryGet(target, out var res) && res.Role == ResidentRole.Keeper;
            double bar = isKeeper ? KeeperCharityBar : FriendBar;

            // Warmth moves the bar: a warm-hearted target gives to near strangers,
            // a cold one wants real friendship first — and a cold rich miser is
            // where the town's grudges come from.
            if (_ctx.Personality.TryGet(target, out var person))
                bar += (0.5 - person.Trait(TraitIndex.Warmth)) * 0.5;

            bool grants = regardTowardAsker >= bar
                && _ctx.Coin.Get(target) - AlmsAmount >= GiverKeepsAtLeast;

            if (grants)
            {
                _ctx.Events.Emit(new CoinTransferEvent { From = target, To = asker, Amount = AlmsAmount });
                _ctx.Events.Emit(new HelpGrantedEvent { Asker = asker, Giver = target, Amount = AlmsAmount });
                // The gratitude + the regard/memory shift are AffectsSystem's now
                // (S2): one place scales them to the interaction's stakes.
            }
            else
            {
                _ctx.Events.Emit(new HelpRefusedEvent { Asker = asker, Refuser = target });
                // Resentment + the (now mild — routine begging) regard/memory shift
                // are AffectsSystem's now (S2).
            }
        }
    }
}
