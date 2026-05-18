namespace DaggerfallWorkshop.Sim
{
    /// Request that an effect be applied to an entity. Consumed by
    /// EffectLifecycleSystem.
    public sealed class ApplyEffectEvent : ISimEvent
    {
        public EntityId Target;
        public EntityId Source;
        public string Key;
        public int Magnitude;
        public int DurationTicks;       // 0 = instantaneous (apply, never persist)
        public bool AppliesPerTick;     // true → EffectTickSystem fires EffectTickedEvent each tick
    }

    /// Request removal of an effect by key. EffectLifecycleSystem honors the
    /// first matching active instance.
    public sealed class RemoveEffectEvent : ISimEvent
    {
        public EntityId Target;
        public string Key;
    }

    /// Fired by EffectLifecycleSystem after an effect lands. Useful for systems
    /// that need to react to "X just got poisoned" without polling.
    public sealed class EffectAppliedEvent : ISimEvent
    {
        public EntityId Target;
        public EntityId Source;
        public string Key;
        public int Magnitude;
    }

    /// Fired by EffectTickSystem on each per-tick effect each tick. DoT
    /// handlers (poison damage, regen) subscribe here and emit Damage/Heal.
    public sealed class EffectTickedEvent : ISimEvent
    {
        public EntityId Target;
        public EntityId Source;
        public string Key;
        public int Magnitude;
    }

    /// Emitted when an effect's remaining ticks reach zero (natural expiry).
    public sealed class EffectExpiredEvent : ISimEvent
    {
        public EntityId Target;
        public string Key;
    }
}
