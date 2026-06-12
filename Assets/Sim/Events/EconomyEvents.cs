namespace DaggerfallWorkshop.Sim
{
    /// Move coin between entities. From = EntityId.None mints from outside
    /// the town economy (explicit faucet); To = None destroys (explicit sink).
    /// Consumed by EconomySystem, the sole coin writer.
    public sealed class CoinTransferEvent : ISimEvent
    {
        public EntityId From;
        public EntityId To;
        public double Amount;
    }

    /// Someone asked for help and got it. Emitted by RequestSystem alongside
    /// the actual CoinTransferEvent and the relation impulses.
    public sealed class HelpGrantedEvent : ISimEvent
    {
        public EntityId Asker;
        public EntityId Giver;
        public double Amount;
    }

    /// Someone asked for help and was turned away. Grudges grow from these.
    public sealed class HelpRefusedEvent : ISimEvent
    {
        public EntityId Asker;
        public EntityId Refuser;
    }
}
