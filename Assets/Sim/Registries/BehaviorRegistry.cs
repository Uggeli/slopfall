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
        Weave,       // textile-sector primary work (shear/weave) for an employer; serves Attire in-kind (the looms' self-reward) + earns a wage. Appended (not grouped with Farm/Fish/Mine) to keep enum ordinals wire-stable.
        Patrol,      // guard duty: hold/patrol the town gate by day (Object-Zero injected, Δ=0)
        StandWatch,  // guard duty: hold the town gate by night (Object-Zero injected, Δ=0)
        Gossip,      // conversation activity (P3): bound social agents trade Inform turns from memory; spreads place facts by word of mouth. Appended to keep enum ordinals wire-stable.
        Negotiate,   // RESERVED (P3): the Offer-act conversation (trade/haggle). No spec yet — placed so the ordinal is reserved.
    }

    public enum ActivityPhase
    {
        Moving,      // walking toward TargetX/Z; MovementSystem drives Position
        Doing,       // at the spot; NeedsSystem applies the activity's deltas
        Queued,      // arrived at a serviced affordance; holding for a turn. Appended for wire stability.
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
}
