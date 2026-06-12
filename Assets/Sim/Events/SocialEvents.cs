namespace DaggerfallWorkshop.Sim
{
    /// Emitted once per pair direction when two civilians become properly
    /// acquainted (familiarity crosses the Met bar).
    public sealed class MetSimEvent : ISimEvent
    {
        public EntityId Who;
        public EntityId Other;
        public int Building;
    }

    /// Emitted once per pair direction when regard + familiarity cross the
    /// friendship bar. The first genuinely social fact the sim produces.
    public sealed class FriendshipFormedEvent : ISimEvent
    {
        public EntityId Who;
        public EntityId Other;
    }
}
