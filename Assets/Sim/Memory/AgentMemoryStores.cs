namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// One agent's bounded record stores: PLACES (spatial), THINGS (entity dossiers), EVENTS
    /// (episodic + self). Caps are species config; these order-of-magnitude defaults come from
    /// the spec. The MEANINGS store (category nodes) has a different record shape and lands in
    /// A3 — only its cap is reserved here.
    /// </summary>
    public sealed class AgentMemoryStores
    {
        public const int PlacesCap = 128;
        public const int ThingsCap = 64;
        public const int EventsCap = 128;
        public const int MeaningsCap = 128;   // reserved; MEANINGS store added in A3

        public readonly MemoryStore Places = new MemoryStore(PlacesCap);
        public readonly MemoryStore Things = new MemoryStore(ThingsCap);
        public readonly MemoryStore Events = new MemoryStore(EventsCap);
    }
}
