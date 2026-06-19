using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.RequestSystem (L4 directed
    // drives). The emergent-quest seed: a begging agent solicits an affordable
    // passer-by it senses (or the keeper of the venue it begs at), who decides from
    // regard + means; the grant moves a coin (CoinTransferEvent → CoinRegistry) and
    // both remember (the regard/memory shift is AffectsSystem's, off HelpGranted/
    // HelpRefused).
    //
    // Owns nothing here either, but its cross-tick cooldown state (the running game-
    // minute clock, the per-beggar cadence, the per-pair re-ask cooldown) now lives in
    // RequestCooldownRegistry — this system reads it and emits the tick/set intents that
    // advance it. The old _venueKeeper map was a per-tick derived index, so it stays a
    // LOCAL rebuilt each Update. Domain logic (PickMark/Consider/Resolve, the bars,
    // alms amount) is preserved EXACTLY.
    public sealed class RequestSystem : SimSystem
    {
        public const double AlmsAmount = 0.25;
        public const double GiverKeepsAtLeast = 0.4;    // won't give below this
        public const double FriendBar = 0.15;           // regard that says yes
        public const double KeeperCharityBar = -0.1;    // keepers tolerate strangers
        const double AskCooldownGameMinutes = 360;      // don't re-ask the same passer-by for 6h
        const double AskCadenceGameMinutes = 20;        // a beggar tries at most one passer-by per ~20 game-min

        readonly BehaviorRegistry _behavior;
        readonly ResidencyRegistry _residency;
        readonly SensedRegistry _sensed;
        readonly CoinRegistry _coin;
        readonly RelationsRegistry _relations;
        readonly PersonalityRegistry _personality;
        readonly WorldClockRegistry _clock;
        readonly RequestCooldownRegistry _cooldown;

        public RequestSystem(EventBus events, BehaviorRegistry behavior, ResidencyRegistry residency,
                             SensedRegistry sensed, CoinRegistry coin, RelationsRegistry relations,
                             PersonalityRegistry personality, WorldClockRegistry clock,
                             RequestCooldownRegistry cooldown) : base(events)
        {
            _behavior = behavior;
            _residency = residency;
            _sensed = sensed;
            _coin = coin;
            _relations = relations;
            _personality = personality;
            _clock = clock;
            _cooldown = cooldown;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;
            double dt = clock.DeltaGameSeconds / 60.0;
            if (dt <= 0) return;
            Events.Publish(new RequestCooldownTickIntent { Delta = dt });   // advance the cooldown clock

            // The cooldown clock the registry reports is last-tick's value; the comparisons
            // below all read it consistently (one tick of lag, same as the old per-tick
            // accumulator order would have produced for a fresh-this-tick window). Use the
            // registry's current game-minute as the asking "now".
            double now = _cooldown.GameMinutes + dt;

            // Everyone currently begging, in id order — transfers couple agents, so a
            // deterministic processing order matters.
            var beggars = new List<EntityId>();
            foreach (var kv in _behavior.All)
            {
                var b = kv.Value;
                if (b.Phase == ActivityPhase.Doing && b.Activity == ActivityKind.Beg) beggars.Add(kv.Key);
            }
            if (beggars.Count == 0) return;
            beggars.Sort((a, b) => a.Value.CompareTo(b.Value));

            // Map each venue to its (lowest-id) keeper once — the alms-giver at the
            // temple/shop door, who is Working indoors and so isn't "sensed" as a
            // passer-by. This is what makes begging actually work. Per-tick derived → LOCAL.
            var venueKeeper = new Dictionary<int, EntityId>();
            foreach (var kv in _residency.All)
            {
                if (kv.Value.Role != ResidentRole.Keeper) continue;
                int b = kv.Value.BuildingIndex;
                if (!venueKeeper.TryGetValue(b, out var cur) || kv.Key.Value < cur.Value)
                    venueKeeper[b] = kv.Key;
            }

            for (int i = 0; i < beggars.Count; i++)
            {
                var beggar = beggars[i];
                if (_cooldown.BeggarOnCooldown(beggar)) continue;          // between asks

                int venue = _behavior.TryGet(beggar, out var bb) ? bb.TargetBuilding : -1;
                var mark = PickMark(beggar, venue, venueKeeper);
                if (mark.IsNone) continue;                                 // nobody worth asking nearby

                Events.Publish(new RequestCooldownSetIntent
                {
                    Beggar = beggar,
                    Mark = mark,
                    NextAskAt = now + AskCadenceGameMinutes,
                    Until = now + AskCooldownGameMinutes,
                });
                Resolve(beggar, mark);
            }
        }

        /// The lowest-id affordable mark the beggar can ask, deterministically: an
        /// affordable passer-by it senses, or the keeper of the venue it begs at.
        /// "Affordable" = has coin to spare, so the destitute aren't asked (and so
        /// soured) for nothing.
        EntityId PickMark(EntityId beggar, int venue, Dictionary<int, EntityId> venueKeeper)
        {
            EntityId best = EntityId.None;
            var sensed = _sensed.Of(beggar);
            for (int i = 0; i < sensed.Count; i++)
                Consider(beggar, sensed[i], ref best);
            if (venue >= 0 && venueKeeper.TryGetValue(venue, out var keeper))
                Consider(beggar, keeper, ref best);
            return best;
        }

        void Consider(EntityId beggar, EntityId other, ref EntityId best)
        {
            if (other == beggar || other.IsNone) return;
            if (_coin.Get(other) - AlmsAmount < GiverKeepsAtLeast) return;
            if (_cooldown.PairOnCooldown(beggar, other)) return;
            if (best.IsNone || other.Value < best.Value) best = other;
        }

        void Resolve(EntityId asker, EntityId target)
        {
            double regardTowardAsker = 0;
            if (_relations.TryGet(target, out var theirRelations)
                && theirRelations.Of.TryGetValue(asker, out var rel))
                regardTowardAsker = rel.Regard;

            bool isKeeper = _residency.TryGet(target, out var res) && res.Role == ResidentRole.Keeper;
            double bar = isKeeper ? KeeperCharityBar : FriendBar;

            // Warmth moves the bar: a warm-hearted target gives to near strangers, a
            // cold one wants real friendship first — and a cold rich miser is where the
            // town's grudges come from.
            if (_personality.TryGet(target, out var person))
                bar += (0.5 - person.Trait(TraitIndex.Warmth)) * 0.5;

            bool grants = regardTowardAsker >= bar
                && _coin.Get(target) - AlmsAmount >= GiverKeepsAtLeast;

            if (grants)
            {
                Events.Publish(new CoinTransferEvent { From = target, To = asker, Amount = AlmsAmount });
                Events.Publish(new HelpGrantedEvent { Asker = asker, Giver = target, Amount = AlmsAmount });
                // The gratitude + the regard/memory shift are AffectsSystem's now (S2):
                // one place scales them to the interaction's stakes.
            }
            else
            {
                Events.Publish(new HelpRefusedEvent { Asker = asker, Refuser = target });
                // Resentment + the (now mild — routine begging) regard/memory shift are
                // AffectsSystem's now (S2).
            }
        }
    }
}
