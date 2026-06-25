using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Keeps each entity's perceivable Activity atom in sync with its current Behavior — the
    /// observable "what it's doing now". Stateless by construction (the SimSystem contract): the
    /// "last activity" is read back from the entity's own Perceivable bag, so on a real change it
    /// emits clear(old) + stamp(new); when already in sync it emits nothing (the spec's on-change
    /// rule). Reads Behavior + Perceivable (both settled, read-only); the only writer of the bag
    /// is PerceivableRegistry, which applies these intents next tick.
    /// </summary>
    public sealed class PerceivableActivitySystem : SimSystem
    {
        readonly BehaviorRegistry _behavior;
        readonly PerceivableRegistry _perceivable;

        public PerceivableActivitySystem(EventBus events, BehaviorRegistry behavior, PerceivableRegistry perceivable)
            : base(events)
        {
            _behavior = behavior;
            _perceivable = perceivable;
        }

        public override void Update(long tick)
        {
            foreach (var kv in _behavior.All)
            {
                AtomTypeId current = CurrentActivityAtom(_perceivable.Bag(kv.Key));
                AtomTypeId desired = PerceivableAtoms.TryActivity(kv.Value.Activity, out var d) ? d : AtomTypeId.None;
                if (current.Value == desired.Value) continue;   // already in sync — no churn

                if (!current.IsNone)
                    Events.Publish(new ClearAtomIntent { Entity = kv.Key, Type = current });
                if (!desired.IsNone)
                    Events.Publish(new StampAtomIntent { Entity = kv.Key, Type = desired, Value = Fixed.One });
            }
        }

        /// <summary>The activity atom currently in the bag (AtomTypeId.None if none).</summary>
        static AtomTypeId CurrentActivityAtom(AtomBag bag)
        {
            for (int i = 0; i < bag.Count; i++)
                if (AtomCatalog.For(bag[i].Type).Category == AtomCategory.Activity)
                    return bag[i].Type;
            return AtomTypeId.None;
        }
    }
}
