namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.LightingRegistry. Reuses the existing
    // LightingData (reference type) from the parent namespace. Single global slot;
    // last write wins. Systems Publish a LightingSetIntent; this registry is the sole
    // applier in its Update().

    /// Intent: "set the global lighting state." Emitted by SunlightSystem; applied
    /// here last-wins.
    public struct LightingSetIntent : IEvent { public LightingData Value; }

    public sealed class LightingRegistry : Registry
    {
        LightingData _data = new LightingData();

        /// Read view (read phase only — no concurrent writer under phase separation).
        public LightingData Current => _data;

        public LightingRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<LightingSetIntent>();
            if (intents.Length == 0) return;
            var v = intents[intents.Length - 1].Value;   // last write wins
            if (v != null) _data = v;
        }
    }
}
