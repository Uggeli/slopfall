namespace DaggerfallWorkshop.Sim
{
    public enum DamageType
    {
        Physical,
        Magic,
        Fire,
        Cold,
        Shock,
        Poison,
        Disease,
        Age,        // old age (L2): AgingSystem's lethal hit, routed through the one death path
    }

    /// Apply damage to an entity. Handled by HealthSystem, which mutates
    /// VitalsRegistry and emits DeathSimEvent on a fatal hit.
    public sealed class DamageEvent : ISimEvent
    {
        public EntityId Target;
        public EntityId Source;     // EntityId.None = environmental / unknown
        public int Amount;          // pre-applied damage; HealthSystem handles clamping
        public DamageType Type;
    }

    /// Heal an entity. Mirror of DamageEvent.
    public sealed class HealEvent : ISimEvent
    {
        public EntityId Target;
        public EntityId Source;
        public int Amount;
    }

    /// Emitted when an entity's CurrentHealth drops to zero.
    public sealed class DeathSimEvent : ISimEvent
    {
        public EntityId Entity;
        public EntityId Killer;
        public DamageType FatalDamageType;
    }
}
