namespace DaggerfallWorkshop.Sim
{
    /// Fired by OddSystem when an entity commits to a new activity.
    public sealed class ActivityStartedEvent : ISimEvent
    {
        public EntityId Entity;
        public ActivityKind Activity;
        public int TargetBuilding;
    }

    /// Fired by MovementSystem when a Moving entity reaches its target spot.
    /// OddSystem flips the entity's phase to Doing on receipt (next tick).
    public sealed class ArrivedAtTargetEvent : ISimEvent
    {
        public EntityId Entity;
    }
}
