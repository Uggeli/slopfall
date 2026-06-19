namespace DaggerfallWorkshop.Sim.Engine
{
    /// Single-global town walkability + baked connectivity. Seeded once by the loader
    /// (direct Set — load-time; connectivity is precomputed there). Static and
    /// read-only thereafter, so Update() is a no-op. Reuses TownGridData from the
    /// parent namespace.
    public sealed class TownGridRegistry : Registry
    {
        TownGridData _data;

        public TownGridRegistry(EventBus events) : base(events) { }

        public TownGridData Current => _data;
        public void Set(TownGridData data) => _data = data;   // load-time seed

        public override void Update(long tick) { }   // static after load
    }
}
