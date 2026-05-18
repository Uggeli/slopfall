namespace DaggerfallWorkshop.Sim
{
    /// Fired by ProgressionSystem when an entity's level increments.
    public sealed class LevelUpEvent : ISimEvent
    {
        public EntityId Entity;
        public int NewLevel;
    }
}
