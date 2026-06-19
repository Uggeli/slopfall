using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// A chosen activity, waiting to be carried out — the handoff from deciding
    /// to acting. OddSystem (deliberation) writes one when an agent commits;
    /// ExecutionSystem reads it the same tick, reifies it into BehaviorRegistry,
    /// and clears it. Keeping the decision as *state* (not a fire-and-forget
    /// event) lets execution apply it in-tick with no timing change.
    public sealed class IntentData
    {
        public ActivityKind Activity;
        public int Building;        // BuildingRegistry key, -1 = at-self / street
        public float X, Z;          // target spot
        public bool Resume;         // continue the current activity (keep its remaining time)
        public double Duration;     // game-minutes for a fresh activity
        public ItemId Item;         // the item a Take/Use/Drop block acts on; None otherwise
    }
}
