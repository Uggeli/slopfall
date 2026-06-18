using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public enum ActivityKind
    {
        None,
        Idle,        // Object-Zero fallback: stand around
        Wander,      // Object-Zero fallback: amble nearby
        Sleep,       // home, serves EnergyDef, night-gated
        Work,        // own workplace (keepers), serves CoinDef, hours-gated
        Labor,       // residents' generic day-work for an employer, serves CoinDef, hours-gated
        Farm,        // working the settlement's farmland — primary food production, out at the fields
        Fish,        // working the coast — primary food production, out at the shore
        Mine,        // working the diggings — primary ore production (mountain regions), out in the hills
        EatHome,     // home, serves Hunger weakly, free
        EatTavern,   // tavern, serves Hunger well, costs coin
        Socialize,   // tavern, serves SocialDef, evening-boosted
        Visit,       // growth drive: spend time at a landmark; engagement is the reward
        Buy,         // shop: refill household goods, pays the shopkeeper (revenue)
        Chat,        // interrupt: stopped on the street to greet a passing friend
        SeekHelp,    // (legacy) embodied request — superseded by Beg; kept until the journey path is removed
        Beg,         // sit at a public venue and ask passers-by for alms (L4); RequestSystem drives the asking
        Steal,       // take provisions off a shop's shelf without paying (Take verb); crime — charged by conscience
        Flee,        // V2b: run from a perceived threat (the fear response) — breaks the percept, resets fear
        Attack,      // V2b: strike a threat (the Attack verb) — fight half of fight-or-flight; crime if the target is innocent
        UseItem,     // use a carried item per its affordance (v1: eat a held Edible) — item block
        StoreItem,   // carry a held item home and stash it (Drop into the home) — item block
    }

    public enum ActivityPhase
    {
        Moving,      // walking toward TargetX/Z; MovementSystem drives Position
        Doing,       // at the spot; NeedsSystem applies the activity's deltas
    }

    public sealed class BehaviorData
    {
        public ActivityKind Activity;
        public ActivityPhase Phase;
        public int TargetBuilding;          // BuildingRegistry key, -1 if none
        public float TargetX, TargetZ;
        public double RemainingGameMinutes; // counted down only while Doing
        /// Game minutes since the last decision. Long activities are promises,
        /// not contracts: OddSystem re-evaluates hourly, resuming seamlessly
        /// when the same activity still wins.
        public double SinceDecisionGameMinutes;
        public ItemId TargetItem;           // item a Take/Use/Drop block acts on; None otherwise
    }

    /// Current activity per civilian — Atoms would call these the minted
    /// drive-instances, as opposed to the persistent poles in NeedsRegistry.
    /// Sole writer: OddSystem (MovementSystem reports arrival via event).
    public sealed class BehaviorRegistry
    {
        readonly ConcurrentDictionary<EntityId, BehaviorData> _d = new ConcurrentDictionary<EntityId, BehaviorData>();

        public void Set(EntityId id, BehaviorData data) => _d[id] = data;
        public bool TryGet(EntityId id, out BehaviorData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, BehaviorData>> All => _d;
    }
}
