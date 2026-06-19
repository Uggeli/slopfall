namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the single-global WorldClock. Reuses WorldClockData from the
    // DaggerfallWorkshop.Sim namespace (accessible unqualified from this sub-namespace).
    // Sole writer is this registry, applying WorldClockSetIntent in Update().

    /// Intent: "set the world clock to these fields." Emitted by TimeSystem each tick;
    /// WorldClockRegistry is the sole applier (last write wins for this singleton).
    public struct WorldClockSetIntent : IEvent
    {
        public int Year, Month, Day, Hour, Minute;
        public float Second, TimeScale;
        public double DeltaGameSeconds;
    }

    /// Single-global world time. No per-entity storage.
    public sealed class WorldClockRegistry : Registry
    {
        WorldClockData _data = new WorldClockData();

        /// Read view (read phase only — no concurrent writer under phase separation).
        public WorldClockData Current => _data;

        public WorldClockRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<WorldClockSetIntent>();
            if (intents.Length == 0) return;
            var i = intents[intents.Length - 1];          // last write wins
            _data = new WorldClockData
            {
                Year = i.Year,
                Month = i.Month,
                Day = i.Day,
                Hour = i.Hour,
                Minute = i.Minute,
                Second = i.Second,
                TimeScale = i.TimeScale,
                DeltaGameSeconds = i.DeltaGameSeconds,
            };
        }
    }
}
