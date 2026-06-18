namespace DaggerfallWorkshop.Sim
{
    /// Provisions were lifted off a shop's shelf — emitted by EconomySystem (which
    /// owns Stock) as a Take happens. ItemSystem (which owns ItemRegistry) turns
    /// whole units into discrete, persistent, CARRIED loaves owned by the keeper,
    /// and charges the taker's conscience when it's theft (owner ≠ taker). The
    /// cross-system handoff keeps each registry single-writer.
    public sealed class ProvisionsTakenEvent : ISimEvent
    {
        public EntityId Taker;
        public EntityId Owner;     // the keeper the loaf still belongs to (None if unowned)
        public int Building;       // where it was lifted (provenance / recovery)
        public double Units;       // this tick's fractional take; ItemSystem accrues it to whole loaves
    }
}
