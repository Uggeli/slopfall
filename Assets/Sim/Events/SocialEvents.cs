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

    /// Emitted once per pair direction when a previously-announced friendship
    /// decays (or is soured) back below the bar — L1's "relationships can end."
    /// Symmetric to FriendshipFormedEvent; re-crossing the bar re-announces.
    public sealed class FriendshipLapsedEvent : ISimEvent
    {
        public EntityId Who;
        public EntityId Other;
    }

    /// Request that Who's directed relation toward Other shift — emitted by
    /// systems that don't own RelationsRegistry (RequestSystem's gratitude and
    /// resentment). SocialSystem, the owner, applies it and records the
    /// memory. Keeps the single-writer rule intact.
    public sealed class RelationImpulseEvent : ISimEvent
    {
        public EntityId Who;
        public EntityId Other;
        public double RegardDelta;
        public double FamiliarityDelta;
        public MemoryKind Memory;
        public bool RecordMemory;
    }
}
