namespace DaggerfallWorkshop.Sim
{
    /// Emitted by LifecycleSystem once an entity has been fully despawned (its
    /// per-entity registry rows removed). The hook for repopulation (L2.4) and
    /// any downstream cleanup. Distinct from DeathSimEvent: death is the cause,
    /// despawn is the structural removal one or more ticks later.
    public sealed class DespawnedEvent : ISimEvent
    {
        public EntityId Entity;
        public int Settlement;      // settlement id the dead belonged to (-1 if none)
        public int Building;        // residency building index (-1 if none) — the slot to backfill (L2.4)
        public ResidentRole Role;   // keeper/resident — preserved on replacement
    }

    /// A dead agent's purse passes to its settlement's treasury (escheat) so the
    /// money supply is conserved across death. Emitted by LifecycleSystem,
    /// applied by EconomySystem (sole writer of Coin + Treasury): purse →
    /// treasury, then the purse row is removed — a within-supply transfer, so no
    /// mint/sink. See docs/living_world_L2_lifecycle.md.
    public sealed class EscheatEvent : ISimEvent
    {
        public EntityId Dead;
        public OwnerId To;
    }
}
