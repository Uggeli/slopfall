namespace DaggerfallWorkshop.Sim
{
    /// Emitted when an entity performs a skill check — swung a weapon, cast a
    /// spell, picked a lock, etc. SkillAdvancementSystem accumulates exp and
    /// promotes the skill once the threshold is met.
    public sealed class SkillUsedEvent : ISimEvent
    {
        public EntityId Entity;
        public string Skill;
        public int Magnitude;       // exp granted by this use (DFU's "skill advancement multiplier" maps to this)
    }

    /// Fired when a skill increments by one point.
    public sealed class SkillAdvancedEvent : ISimEvent
    {
        public EntityId Entity;
        public string Skill;
        public int NewValue;
    }
}
