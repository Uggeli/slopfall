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
        EatHome,     // home, serves Hunger weakly, free
        EatTavern,   // tavern, serves Hunger well, costs coin
        Socialize,   // tavern, serves SocialDef, evening-boosted
        Visit,       // growth drive: spend time at a landmark; engagement is the reward
        Buy,         // shop: refill household goods, pays the shopkeeper (revenue)
        Chat,        // interrupt: stopped on the street to greet a passing friend
        SeekHelp,    // embodied request: walking to someone to ask for alms
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
